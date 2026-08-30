using System;

namespace DcsDataService.Api.Handlers
{
    public sealed class HealthHandler : IApiHandler
    {
        public object Handle(HttpRequest request) { return new { status = "ok", time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") }; }
    }
}
