using System;
using System.Threading;
using DcsDataService.Configuration;
using DcsDataService.Util;
using DcsRealtime;

namespace DcsDataService.DeltaV.Realtime
{
    public sealed class RealtimeException : Exception
    {
        public readonly int HttpStatus;
        public readonly ReadError Error;
        public RealtimeException(int status, string code, string message) : this(status, new ReadError { code = code, message = message }) { }
        public RealtimeException(int status, ReadError error) : base(error.message) { HttpStatus = status; Error = error; }
    }
    public interface IRealtimeRuntime { void Initialize(); ReadItem Read(string tag); }
    public sealed class RealtimeProvider : IDisposable
    {
        private sealed class Request
        {
            internal string[] Tags;
            internal ReadBatch Result;
            internal RealtimeException Error;
            internal readonly ManualResetEvent Done = new ManualResetEvent(false);
        }
        private readonly ServiceConfig _config;
        private readonly ServiceLog _log;
        private readonly IRealtimeRuntime _runtime;
        private readonly ConcurrencyGate _gate = new ConcurrencyGate(1);
        private readonly object _sync = new object();
        private readonly AutoResetEvent _available = new AutoResetEvent(false);
        private Thread _thread;
        private Request _pending;
        private bool _stopping;
        public RealtimeProvider(ServiceConfig config, ServiceLog log) : this(config, log, new NativeRuntime(config.SourceTimeZone)) { }
        public RealtimeProvider(ServiceConfig config, ServiceLog log, IRealtimeRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException("runtime");
            _config = config; _log = log; _runtime = runtime;
        }
        public ReadBatch Read(string[] tags)
        {
            ReadValidation.Tags(tags, _config.RealtimeMaxBatchTags);
            if (!_config.RealtimeEnabled) throw new RealtimeException(503, "realtime_disabled", "Native realtime reads are disabled in configuration.");
            lock (_sync) if (_stopping) throw Stopped();
            IDisposable lease;
            try { lease = _gate.Enter(_config.RealtimeSlotWaitSeconds * 1000); }
            catch (ConcurrencyGateTimeoutException) { throw new RealtimeException(503, "service_busy", "Timed out waiting for the realtime reader."); }
            using (lease)
            {
                Request request = new Request { Tags = (string[])tags.Clone() };
                try
                {
                    lock (_sync)
                    {
                        if (_stopping) throw Stopped();
                        if (_thread == null)
                        {
                            _thread = new Thread(ReadLoop); _thread.IsBackground = true; _thread.Name = "DeltaV realtime";
                            _thread.Start();
                        }
                        _pending = request; _available.Set();
                    }
                    // An in-process synchronous native call cannot be safely aborted.
                    // Retain its single slot until it returns; never launch replacement readers.
                    request.Done.WaitOne();
                    if (request.Error != null) throw request.Error;
                    return request.Result;
                }
                finally { request.Done.Close(); }
            }
        }
        private static RealtimeException Stopped() { return new RealtimeException(503, "realtime_unavailable", "Service is stopping."); }
        private void ReadLoop()
        {
            bool initialized = false;
            RealtimeException initializationError = null;
            try
            {
                while (true)
                {
                    _available.WaitOne();
                    Request request;
                    lock (_sync)
                    {
                        request = _pending; _pending = null;
                        if (request == null && _stopping) return;
                    }
                    if (request == null) continue;
                    try
                    {
                        lock (_sync) if (_stopping) throw Stopped();
                        if (initializationError != null) throw initializationError;
                        if (!initialized)
                        {
                            try { _runtime.Initialize(); initialized = true; }
                            catch (Exception ex) { initializationError = Error(ex, "realtime_unavailable"); throw initializationError; }
                        }
                        ReadItem[] items = new ReadItem[request.Tags.Length];
                        for (int i = 0; i < items.Length; i++) items[i] = _runtime.Read(request.Tags[i]);
                        request.Result = ReadValidation.Completed(items);
                    }
                    catch (Exception ex) { request.Error = Error(ex, "realtime_read_failed"); }
                    finally { request.Done.Set(); }
                    lock (_sync) if (_stopping) return;
                }
            }
            finally { _available.Close(); }
        }
        private RealtimeException Error(Exception ex, string code)
        {
            RealtimeException realtime = ex as RealtimeException;
            if (realtime != null) return realtime;
            if (_log != null) _log.Error("Realtime read failure", ex);
            NativeFailure native = ex as NativeFailure;
            return native != null ? new RealtimeException(503, native.Error) : new RealtimeException(503, code, ex.Message);
        }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_stopping) return;
                _stopping = true;
                if (_thread == null) _available.Close(); else _available.Set();
            }
        }
    }
}
