using System;
using System.Collections.Generic;
using DcsDataService.Api.Handlers;

namespace DcsDataService.Api
{
    public sealed class Router
    {
        private sealed class Route { public string Method; public IApiHandler Handler; }
        private readonly Dictionary<string, Route> _routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        public Router(HandlerContext c)
        {
            Add("GET", "/health", new HealthHandler()); Add("GET", "/api/v1/info", new InfoHandler(c)); Add("GET", "/api/v1/tag", new TagHandler(c)); Add("GET", "/api/v1/history", new HistoryHandler(c)); Add("GET", "/api/v1/events", new DcsDataService.Api.Handlers.EventHandler(c));
        }
        private void Add(string method, string path, IApiHandler h) { _routes.Add(path, new Route { Method = method, Handler = h }); }
        public HttpResponse RouteRequest(HttpRequest request) { Route route; if (!_routes.TryGetValue(request.Path, out route)) throw new RouteException(404, "not_found", "Route not found."); if (!String.Equals(route.Method, request.Method, StringComparison.Ordinal)) throw new RouteException(405, "method_not_allowed", "Method not allowed."); return route.Handler.Handle(request); }
    }
    public sealed class RouteException : Exception { public readonly int Status; public readonly string Code; public RouteException(int status, string code, string message) : base(message) { Status = status; Code = code; } }
    public sealed class RequestTooLargeException : Exception { public RequestTooLargeException(string message) : base(message) { } }
}
