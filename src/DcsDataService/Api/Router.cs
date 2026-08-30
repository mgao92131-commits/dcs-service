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
            Add("GET", "/health", new HealthHandler()); Add("GET", "/api/v1/info", new InfoHandler(c)); Add("POST", "/api/v1/tags/resolve", new TagHandler(c)); Add("POST", "/api/v1/history/query", new HistoryHandler(c)); Add("POST", "/api/v1/events/query", new DcsDataService.Api.Handlers.EventHandler(c, false)); Add("POST", "/api/v1/events/after", new DcsDataService.Api.Handlers.EventHandler(c, true));
        }
        private void Add(string method, string path, IApiHandler h) { _routes.Add(path, new Route { Method = method, Handler = h }); }
        public object RouteRequest(HttpRequest request) { Route route; if (!_routes.TryGetValue(request.Path, out route)) throw new RouteException(404, "not_found", "Route not found."); if (!String.Equals(route.Method, request.Method, StringComparison.Ordinal)) throw new RouteException(405, "method_not_allowed", "Method not allowed."); return route.Handler.Handle(request); }
    }
    public sealed class RouteException : Exception { public readonly int Status; public readonly string Code; public RouteException(int status, string code, string message) : base(message) { Status = status; Code = code; } }
    public sealed class RequestTooLargeException : Exception { public RequestTooLargeException(string message) : base(message) { } }
}
