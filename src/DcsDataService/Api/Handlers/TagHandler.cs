using System.Collections.Generic;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class TagHandler : IApiHandler
    {
        private readonly HandlerContext _c; public TagHandler(HandlerContext c) { _c = c; }
        public HttpResponse Handle(HttpRequest request)
        {
            IDictionary<string, string> query = QueryStringParser.Parse(request.QueryString); string tag = QueryStringParser.Required(query, "tag");
            using (_c.HistoryGate.Enter(_c.Config.RequestTimeoutSeconds * 1000))
            {
                IList<HistoryTagInfo> resolved = _c.Historian.ResolveTags(new List<string> { tag }); HistoryTagInfo info = resolved[0];
                _c.Log.Info("Tag resolve tag=" + tag + " status=" + info.Status);
                return new HttpResponse { StatusCode = 200, Body = JsonUtil.Serialize(new { tag = info.Tag, status = info.Status.ToString(), dataType = info.DataType }) };
            }
        }
    }
}
