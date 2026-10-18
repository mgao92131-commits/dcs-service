using System;
using System.Collections;
using System.Collections.Generic;
using DcsDataService.DeltaV.Realtime;
using DcsDataService.Util;
using DcsRealtime;

namespace DcsDataService.Api.Handlers
{
    public sealed class RealtimeHandler : IApiHandler
    {
        private readonly HandlerContext _context;
        private readonly bool _batch;
        public RealtimeHandler(HandlerContext context, bool batch) { _context = context; _batch = batch; }
        public HttpResponse Handle(HttpRequest request)
        {
            string[] tags;
            if (!_batch) tags = new string[] { QueryStringParser.Required(QueryStringParser.Parse(request.QueryString), "tag") };
            else
            {
                if (request.Body == null || request.Body.Length > 262144) throw new ArgumentException("Realtime JSON body is empty or exceeds the size limit.");
                string contentType;
                if (!request.Headers.TryGetValue("Content-Type", out contentType) || !String.Equals(contentType.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use Content-Type: application/json.");
                try
                {
                    IDictionary<string, object> body = JsonUtil.Deserialize<Dictionary<string, object>>(request.Body);
                    if (body == null || body.Count != 1 || !body.ContainsKey("tags")) throw new ArgumentException("Expected a JSON object containing only tags.");
                    IEnumerable values = body["tags"] as IEnumerable;
                    if (values == null || body["tags"] is string || body["tags"] is IDictionary) throw new ArgumentException("tags must be a JSON array of strings.");
                    List<string> list = new List<string>();
                    foreach (object tag in values) { if (list.Count >= _context.Config.RealtimeMaxBatchTags) throw new ArgumentException("Too many realtime tags."); if (!(tag is string)) throw new ArgumentException("Every tag must be a string."); list.Add((string)tag); }
                    tags = list.ToArray();
                }
                catch (ArgumentException) { throw; }
                catch (Exception) { throw new ArgumentException("Invalid realtime JSON request."); }
            }
            ReadBatch result = _context.Realtime.Read(tags);
            if (!_batch)
            {
                ReadItem item = result.items[0];
                int status = item.ok ? 200 : item.error.code == "parameter_not_found" ? 404 : 503;
                return new HttpResponse { StatusCode = status, Body = JsonUtil.Serialize(item) };
            }
            return new HttpResponse { StatusCode = 200, Body = JsonUtil.Serialize(result) };
        }
    }
}
