using System;
using System.Collections.Generic;

namespace DcsDataService.Api
{
    public sealed class HttpRequest
    {
        public string Method; public string Path; public string QueryString; public string Body;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
