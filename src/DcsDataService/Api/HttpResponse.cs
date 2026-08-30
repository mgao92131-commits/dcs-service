using System;
using System.Globalization;
using System.Text;

namespace DcsDataService.Api
{
    public sealed class HttpResponse
    {
        public int StatusCode; public string Body;
        public byte[] ToBytes()
        {
            byte[] body = Encoding.UTF8.GetBytes(Body ?? ""); string reason = StatusCode == 200 ? "OK" : StatusCode == 400 ? "Bad Request" : StatusCode == 401 ? "Unauthorized" : StatusCode == 404 ? "Not Found" : StatusCode == 405 ? "Method Not Allowed" : StatusCode == 413 ? "Payload Too Large" : StatusCode == 503 ? "Service Unavailable" : "Internal Server Error";
            string head = "HTTP/1.1 " + StatusCode.ToString(CultureInfo.InvariantCulture) + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n";
            byte[] header = Encoding.ASCII.GetBytes(head); byte[] all = new byte[header.Length + body.Length]; Buffer.BlockCopy(header, 0, all, 0, header.Length); Buffer.BlockCopy(body, 0, all, header.Length, body.Length); return all;
        }
    }
}
