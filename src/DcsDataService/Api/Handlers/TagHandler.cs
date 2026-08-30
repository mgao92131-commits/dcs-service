using System.Collections.Generic;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class TagHandler : IApiHandler
    {
        private readonly HandlerContext _c; public TagHandler(HandlerContext c) { _c = c; }
        public object Handle(HttpRequest request) { Dictionary<string, object> body = JsonUtil.Object(request.Body); IList<string> tags = JsonUtil.Strings(body, "tags"); if (tags.Count == 0 || tags.Count > _c.Config.MaxTagsPerRequest) throw new System.ArgumentException("tags count must be between 1 and " + _c.Config.MaxTagsPerRequest + "."); IList<DcsDataService.DeltaV.Historian.HistoryTagInfo> resolved = _c.Historian.ResolveTags(tags); List<object> wire = new List<object>(); for (int i = 0; i < resolved.Count; i++) wire.Add(new { tag = resolved[i].Tag, handle = resolved[i].Handle, status = resolved[i].Status.ToString(), dataType = resolved[i].DataType }); _c.Log.Info("Tag resolve tagCount=" + tags.Count); return new { tags = wire, sourceTimeZone = _c.Config.SourceTimeZone }; }
    }
}
