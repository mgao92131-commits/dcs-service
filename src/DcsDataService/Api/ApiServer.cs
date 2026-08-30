using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DcsDataService.Configuration;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Models;
using DcsDataService.Util;

namespace DcsDataService.Api
{
    public sealed class ApiServer
    {
        private readonly ServiceConfig _config; private readonly Router _router; private readonly ServiceLog _log; private TcpListener _listener; private volatile bool _stopping;
        public ApiServer(ServiceConfig config, Router router, ServiceLog log) { _config = config; _router = router; _log = log; }
        public void Run()
        {
            IPAddress address = IPAddress.Parse(_config.ApiBind == "localhost" ? "127.0.0.1" : _config.ApiBind); _listener = new TcpListener(address, _config.ApiPort); _listener.Start(); _log.Info("service start bind=" + address + " port=" + _config.ApiPort);
            while (!_stopping) { TcpClient client; try { client = _listener.AcceptTcpClient(); } catch (SocketException) { if (_stopping) break; throw; } ThreadPool.QueueUserWorkItem(delegate { HandleClient(client); }); }
            _log.Info("service stop");
        }
        public void Stop() { _stopping = true; if (_listener != null) _listener.Stop(); }
        private void HandleClient(TcpClient client)
        {
            Stopwatch clock = Stopwatch.StartNew(); string method = "?"; string path = "?"; int status = 500;
            NetworkStream stream = null;
            try
            {
                client.ReceiveTimeout = _config.RequestTimeoutSeconds * 1000; client.SendTimeout = _config.RequestTimeoutSeconds * 1000;
                stream = client.GetStream(); HttpRequest request = ReadRequest(stream); method = request.Method; path = request.Path;
                string key; if (!request.Headers.TryGetValue("X-DCS-API-Key", out key) || !FixedEquals(key, _config.ApiKey)) { Write(stream, Error(401, "unauthorized", "Invalid API key.")); status = 401; return; }
                object data = _router.RouteRequest(request); string json = JsonUtil.Serialize(ApiResponse.Success(data)); EnsureResponseSize(json, _config.MaxResponseBytes); HttpResponse response = new HttpResponse { StatusCode = 200, Body = json }; status = 200; Write(stream, response);
            }
            catch (RequestTooLargeException ex) { status = 413; SafeWrite(stream, Error(status, "request_too_large", ex.Message)); }
            catch (ResponseTooLargeException ex) { status = 413; SafeWrite(stream, Error(status, "response_too_large", ex.Message)); }
            catch (RouteException ex) { status = ex.Status; SafeWrite(stream, Error(status, ex.Code, ex.Message)); }
            catch (ArgumentException ex) { status = 400; SafeWrite(stream, Error(status, "invalid_request", ex.Message)); }
            catch (FileNotFoundException ex) { status = 503; _log.Error("Historian DLL failure", ex); SafeWrite(stream, Error(status, "historian_unavailable", "Historian DLL is unavailable.")); }
            catch (IOException ex) { status = 400; _log.Error("HTTP read failure or timeout", ex); SafeWrite(stream, Error(status, "request_timeout", "Request was incomplete or timed out.")); }
            catch (HistorianException ex) { status = 503; _log.Error("Historian failure", ex); SafeWrite(stream, Error(status, "historian_unavailable", "Historian is unavailable.")); }
            catch (HistoryQueryTooLargeException ex) { status = 413; _log.Error("History query limit", ex); SafeWrite(stream, Error(status, "query_too_large", ex.Message)); }
            catch (EventSourceUnsafeException ex) { status = 503; _log.Error("Event source unsafe", ex); SafeWrite(stream, Error(status, ex.ErrorCode, ex.Message)); }
            catch (EventCursorException ex) { status = 409; _log.Error("Event cursor rejected", ex); SafeWrite(stream, Error(status, ex.ErrorCode, ex.Message)); }
            catch (SqlException ex) { status = 503; _log.Error("Event SQL failure", ex); SafeWrite(stream, Error(status, "event_unavailable", "Event Journal is unavailable.")); }
            catch (InvalidOperationException ex) { status = 503; _log.Error("Event source failure", ex); SafeWrite(stream, Error(status, "event_unavailable", "Event Journal is unavailable or unsafe.")); }
            catch (Exception ex) { status = 500; _log.Error("Unhandled API error", ex); SafeWrite(stream, Error(status, "internal_error", "Internal server error.")); }
            finally { clock.Stop(); _log.Info("API method=" + method + " path=" + path + " status=" + status.ToString(CultureInfo.InvariantCulture) + " durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)); try { if (stream != null) stream.Close(); } catch { } try { client.Close(); } catch { } }
        }
        private HttpRequest ReadRequest(NetworkStream stream)
        {
            MemoryStream header = new MemoryStream(); int state = 0;
            while (state < 4) { int b = stream.ReadByte(); if (b < 0) throw new IOException("Unexpected end of HTTP headers."); header.WriteByte((byte)b); if (header.Length > 32768) throw new RequestTooLargeException("HTTP headers exceed 32768 bytes."); state = (state == 0 && b == 13) ? 1 : (state == 1 && b == 10) ? 2 : (state == 2 && b == 13) ? 3 : (state == 3 && b == 10) ? 4 : 0; }
            string text = Encoding.ASCII.GetString(header.ToArray()); string[] lines = text.Split(new string[] { "\r\n" }, StringSplitOptions.None); string[] first = lines[0].Split(' '); if (first.Length != 3 || first[2] != "HTTP/1.1") throw new ArgumentException("Only HTTP/1.1 is supported."); HttpRequest request = new HttpRequest { Method = first[0], Path = first[1].Split('?')[0] };
            for (int i = 1; i < lines.Length; i++) { int colon = lines[i].IndexOf(':'); if (colon > 0) request.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim(); }
            string lengthText; int length = 0; if (request.Headers.TryGetValue("Content-Length", out lengthText) && (!Int32.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0)) throw new ArgumentException("Invalid Content-Length."); if (length > _config.MaxRequestBytes) throw new RequestTooLargeException("Request body exceeds configured limit.");
            if (request.Method == "POST") { string contentType; if (!request.Headers.TryGetValue("Content-Type", out contentType) || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("POST requires application/json."); if (length == 0) throw new ArgumentException("POST requires Content-Length and a body."); }
            byte[] body = new byte[length]; int offset = 0; while (offset < length) { int n = stream.Read(body, offset, length - offset); if (n <= 0) throw new IOException("Unexpected end of HTTP body."); offset += n; } request.Body = Encoding.UTF8.GetString(body); return request;
        }
        private static bool FixedEquals(string a, string b) { if (a == null || b == null) return false; int diff = a.Length ^ b.Length; int n = Math.Max(a.Length, b.Length); for (int i = 0; i < n; i++) diff |= (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0); return diff == 0; }
        public static void EnsureResponseSize(string json, int maxBytes) { if (json == null) throw new ArgumentNullException("json"); if (maxBytes < 1) throw new ArgumentOutOfRangeException("maxBytes"); if (Encoding.UTF8.GetByteCount(json) > maxBytes) throw new ResponseTooLargeException("Response exceeds MaxResponseBytes=" + maxBytes.ToString(CultureInfo.InvariantCulture) + "."); }
        private static HttpResponse Error(int status, string code, string message) { return new HttpResponse { StatusCode = status, Body = JsonUtil.Serialize(ApiResponse.Failure(code, message)) }; }
        private static void Write(Stream stream, HttpResponse response) { byte[] bytes = response.ToBytes(); stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
        private static void SafeWrite(Stream stream, HttpResponse response) { try { if (stream != null) Write(stream, response); } catch { } }
    }
}
