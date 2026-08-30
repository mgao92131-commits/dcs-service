namespace DcsDataService.Api.Handlers
{
    public sealed class InfoHandler : IApiHandler
    {
        private readonly HandlerContext _c; public InfoHandler(HandlerContext c) { _c = c; }
        public HttpResponse Handle(HttpRequest request) { return new HttpResponse { StatusCode = 200, Body = DcsDataService.Util.JsonUtil.Serialize(new { service = "DcsDataService", version = Program.Version, historianServer = _c.Config.HistorianServer, sourceTimeZone = _c.Config.SourceTimeZone, historyMaxConcurrent = _c.Config.HistoryMaxConcurrent, eventMaxConcurrent = _c.Config.EventMaxConcurrent, readOnly = true }) }; }
    }
}
