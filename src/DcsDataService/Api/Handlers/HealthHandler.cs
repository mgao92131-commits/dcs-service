using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class HealthHandler : IApiHandler
    {
        public HttpResponse Handle(HttpRequest request) { return new HttpResponse { StatusCode = 200, Body = JsonUtil.Serialize(new { status = "ok" }) }; }
    }
}
