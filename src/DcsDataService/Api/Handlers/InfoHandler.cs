namespace DcsDataService.Api.Handlers
{
    public sealed class InfoHandler : IApiHandler
    {
        private readonly HandlerContext _c; public InfoHandler(HandlerContext c) { _c = c; }
        public object Handle(HttpRequest request) { return new { service = "DcsDataService", version = Program.Version, historianServer = _c.Config.HistorianServer, eventServer = _c.Config.EventsServer, eventDatabase = _c.Config.EventsDatabase, sourceTimeZone = _c.Config.SourceTimeZone, readOnly = true }; }
    }
}
