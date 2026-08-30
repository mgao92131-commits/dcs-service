using System;
using System.Threading;

namespace DcsDataService.Util
{
    public sealed class ConcurrencyGate
    {
        private readonly Semaphore _semaphore;
        public ConcurrencyGate(int maximum) { if (maximum < 1) throw new ArgumentOutOfRangeException("maximum"); _semaphore = new Semaphore(maximum, maximum); }
        public IDisposable Enter(int timeoutMilliseconds)
        {
            if (!_semaphore.WaitOne(timeoutMilliseconds, false)) throw new ConcurrencyGateTimeoutException();
            return new Releaser(_semaphore);
        }
        private sealed class Releaser : IDisposable
        {
            private Semaphore _value; public Releaser(Semaphore value) { _value = value; }
            public void Dispose() { Semaphore value = Interlocked.Exchange(ref _value, null); if (value != null) value.Release(); }
        }
    }
    public sealed class ConcurrencyGateTimeoutException : Exception { public ConcurrencyGateTimeoutException() : base("Timed out waiting for a provider concurrency slot.") { } }
}
