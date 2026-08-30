using DcsDataService.Configuration;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class HandlerContext
    {
        public ServiceConfig Config; public HistorianProvider Historian; public EventProvider Events; public ServiceLog Log;
    }
    public interface IApiHandler { object Handle(HttpRequest request); }
}
