using System;
using System.Threading;

namespace DcsDataService.Util
{
    public sealed class DisposablePair : IDisposable
    {
        private IDisposable _first;
        private IDisposable _second;

        public DisposablePair(IDisposable first, IDisposable second) { _first = first; _second = second; }

        public void Dispose()
        {
            IDisposable first = Interlocked.Exchange(ref _first, null);
            IDisposable second = Interlocked.Exchange(ref _second, null);
            try { if (first != null) first.Dispose(); }
            finally { if (second != null) second.Dispose(); }
        }
    }
}
