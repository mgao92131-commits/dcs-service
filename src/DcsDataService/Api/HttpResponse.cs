using System;
using System.Globalization;
using System.Text;
using System.IO;
using System.Collections.Generic;

namespace DcsDataService.Api
{
    public sealed class HttpResponse
    {
        public int StatusCode; public string Body; public string ContentType = "application/json; charset=utf-8";
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Action<Stream> BodyWriter;
        public bool IsChunked;
        public IDisposable BodyResource;
        public bool IsStarted { get; private set; }

        public void WriteTo(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            byte[] body = BodyWriter == null ? Encoding.UTF8.GetBytes(Body ?? "") : null;
            string reason = StatusCode == 200 ? "OK" : StatusCode == 400 ? "Bad Request" : StatusCode == 404 ? "Not Found" : StatusCode == 405 ? "Method Not Allowed" : StatusCode == 409 ? "Conflict" : StatusCode == 413 ? "Payload Too Large" : StatusCode == 429 ? "Too Many Requests" : StatusCode == 503 ? "Service Unavailable" : "Internal Server Error";
            try
            {
                StringBuilder head = new StringBuilder(); head.Append("HTTP/1.1 ").Append(StatusCode.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append("\r\nContent-Type: ").Append(ContentType).Append("\r\n");
                if (IsChunked) head.Append("Transfer-Encoding: chunked\r\n"); else if (body != null) head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                foreach (KeyValuePair<string, string> pair in Headers) head.Append(pair.Key).Append(": ").Append(SafeHeader(pair.Value)).Append("\r\n");
                head.Append("Connection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n"); byte[] header = Encoding.ASCII.GetBytes(head.ToString()); IsStarted = true; stream.Write(header, 0, header.Length);
                if (IsChunked)
                {
                    ChunkedWriteStream chunked = new ChunkedWriteStream(stream);
                    if (BodyWriter == null) chunked.Write(body, 0, body.Length); else BodyWriter(chunked);
                    chunked.Complete();
                }
                else if (BodyWriter == null) stream.Write(body, 0, body.Length); else BodyWriter(stream);
                stream.Flush();
            }
            finally
            {
                IDisposable resource = BodyResource; BodyResource = null;
                if (resource != null) try { resource.Dispose(); } catch { }
            }
        }
        private static string SafeHeader(string value) { if (value == null) return ""; return value.Replace("\r", "").Replace("\n", ""); }
    }
}
