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
        private const int MaximumRequestBodyBytes = 1048576;
        public static readonly IPAddress ListenAddress = IPAddress.Loopback;
        private readonly ServiceConfig _config; private readonly Router _router; private readonly ServiceLog _log; private TcpListener _listener; private volatile bool _stopping; private int _requestsInSystem;
        public ApiServer(ServiceConfig config, Router router, ServiceLog log) { _config = config; _router = router; _log = log; }
        public void Run()
        {
            IPAddress address = ListenAddress; _listener = new TcpListener(address, _config.ApiPort); _listener.Start(); _log.Info("service start bind=" + address + " port=" + _config.ApiPort);
            while (!_stopping) { TcpClient client; try { client = _listener.AcceptTcpClient(); } catch (SocketException) { if (_stopping) break; throw; } if (Interlocked.Increment(ref _requestsInSystem) > _config.RequestQueueLimit) { Interlocked.Decrement(ref _requestsInSystem); RejectBusy(client); continue; } ThreadPool.QueueUserWorkItem(delegate { try { HandleClient(client); } finally { Interlocked.Decrement(ref _requestsInSystem); } }); }
            _log.Info("service stop");
        }
        public void Stop() { _stopping = true; if (_listener != null) _listener.Stop(); }
        private void HandleClient(TcpClient client)
        {
            Stopwatch clock = Stopwatch.StartNew(); string method = "?"; string path = "?"; int status = 500;
            NetworkStream stream = null; HttpResponse response = null;
            try
            {
                client.ReceiveTimeout = _config.SocketReadSeconds * 1000; client.SendTimeout = _config.SocketWriteSeconds * 1000;
                stream = client.GetStream(); stream.ReadTimeout = _config.SocketReadSeconds * 1000; stream.WriteTimeout = _config.SocketWriteSeconds * 1000; HttpRequest request = ReadRequest(stream); method = request.Method; path = request.Path;
                response = _router.RouteRequest(request); status = response.StatusCode; Write(stream, response);
            }
            catch (RequestTooLargeException ex) { ReplyOrAbort(response, stream, 413, Error(413, "request_too_large", ex.Message), "HTTP request too large", ex, ref status); }
            catch (RouteException ex) { ReplyOrAbort(response, stream, ex.Status, Error(ex.Status, ex.Code, ex.Message), "HTTP route failure", ex, ref status); }
            catch (ArgumentException ex) { ReplyOrAbort(response, stream, 400, Error(400, "invalid_request", ex.Message), "HTTP request validation failure", ex, ref status); }
            catch (FileNotFoundException ex) { ReplyOrAbort(response, stream, 503, Error(503, "historian_unavailable", "Historian DLL is unavailable."), "Historian DLL failure", ex, ref status); }
            catch (IOException ex) { ReplyOrAbort(response, stream, 400, Error(400, "request_timeout", "Request was incomplete or timed out."), "HTTP read/write failure or timeout", ex, ref status); }
            catch (HistorianException ex) { ReplyOrAbort(response, stream, 503, Error(503, "historian_unavailable", "Historian is unavailable."), "Historian failure", ex, ref status); }
            catch (ConcurrencyGateTimeoutException ex) { ReplyOrAbort(response, stream, 503, Error(503, "service_busy", "Timed out waiting for a provider slot."), "Provider concurrency wait timeout", ex, ref status); }
            catch (EventSourceUnsafeException ex) { ReplyOrAbort(response, stream, 503, Error(503, ex.ErrorCode, ex.Message), "Event source unsafe", ex, ref status); }
            catch (EventCursorException ex) { ReplyOrAbort(response, stream, 409, Error(409, ex.ErrorCode, ex.Message), "Event cursor rejected", ex, ref status); }
            catch (SqlException ex) { ReplyOrAbort(response, stream, 503, Error(503, "event_unavailable", "Event Journal is unavailable."), "Event SQL failure", ex, ref status); }
            catch (InvalidOperationException ex) { ReplyOrAbort(response, stream, 503, Error(503, "event_unavailable", "Event Journal is unavailable or unsafe."), "Event source failure", ex, ref status); }
            catch (Exception ex) { ReplyOrAbort(response, stream, 500, Error(500, "internal_error", "Internal server error."), "Unhandled API error", ex, ref status); }
            finally { clock.Stop(); _log.Info("API method=" + method + " path=" + path + " status=" + status.ToString(CultureInfo.InvariantCulture) + " durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)); try { if (stream != null) stream.Close(); } catch { } try { client.Close(); } catch { } }
        }
        private HttpRequest ReadRequest(NetworkStream stream)
        {
            MemoryStream header = new MemoryStream(); int state = 0;
            while (state < 4) { int b = stream.ReadByte(); if (b < 0) throw new IOException("Unexpected end of HTTP headers."); header.WriteByte((byte)b); if (header.Length > 32768) throw new RequestTooLargeException("HTTP headers exceed 32768 bytes."); state = (state == 0 && b == 13) ? 1 : (state == 1 && b == 10) ? 2 : (state == 2 && b == 13) ? 3 : (state == 3 && b == 10) ? 4 : 0; }
            string text = Encoding.ASCII.GetString(header.ToArray()); string[] lines = text.Split(new string[] { "\r\n" }, StringSplitOptions.None); string[] first = lines[0].Split(' '); if (first.Length != 3 || first[2] != "HTTP/1.1") throw new ArgumentException("Only HTTP/1.1 is supported."); int question = first[1].IndexOf('?'); HttpRequest request = new HttpRequest { Method = first[0], Path = question < 0 ? first[1] : first[1].Substring(0, question), QueryString = question < 0 ? "" : first[1].Substring(question + 1) };
            for (int i = 1; i < lines.Length; i++) { int colon = lines[i].IndexOf(':'); if (colon > 0) request.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim(); }
            string lengthText; int length = 0; if (request.Headers.TryGetValue("Content-Length", out lengthText) && (!Int32.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0)) throw new ArgumentException("Invalid Content-Length."); if (length > MaximumRequestBodyBytes) throw new RequestTooLargeException("Request body exceeds the HTTP safety limit.");
            byte[] body = new byte[length]; int offset = 0; while (offset < length) { int n = stream.Read(body, offset, length - offset); if (n <= 0) throw new IOException("Unexpected end of HTTP body."); offset += n; } request.Body = Encoding.UTF8.GetString(body); return request;
        }
        private static HttpResponse Error(int status, string code, string message) { return new HttpResponse { StatusCode = status, Body = JsonUtil.Serialize(ApiResponse.Failure(code, message)) }; }
        private static void Write(Stream stream, HttpResponse response) { response.WriteTo(stream); }
        private static void SafeWrite(Stream stream, HttpResponse response) { try { if (stream != null) Write(stream, response); } catch { } }
        private void ReplyOrAbort(HttpResponse response, NetworkStream stream, int errorStatus, HttpResponse error, string message, Exception exception, ref int status)
        {
            if (response != null && response.IsStarted) { status = response.StatusCode; _log.Error(message + " after response started; closing incomplete response", exception); return; }
            status = errorStatus; _log.Error(message, exception); SafeWrite(stream, error);
        }
        private static void RejectBusy(TcpClient client) { try { NetworkStream stream = client.GetStream(); byte[] discard = new byte[1024]; while (stream.DataAvailable) stream.Read(discard, 0, discard.Length); Write(stream, Error(429, "service_busy", "DCS data service request queue is full.")); try { client.Client.Shutdown(SocketShutdown.Send); } catch { } } catch { } finally { try { client.Close(); } catch { } } }
    }
}
