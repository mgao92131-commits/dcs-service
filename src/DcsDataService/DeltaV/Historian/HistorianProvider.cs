using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using DeltaV.Historian.Data;
using DeltaV.Historian.DvCHDataAccess;
using DcsDataService.Util;
using DvAccess = DeltaV.Historian.DvCHDataAccess.DvCHDataAccess;

namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistorianProvider
    {
        private const int MaxSplitDepth = 20;
        private static readonly TimeSpan MinimumSlice = TimeSpan.FromTicks(1);
        private static readonly object InitializeGate = new object();
        private static bool _initialized;
        private readonly object _tagCacheGate = new object();
        private readonly string _server;
        private readonly int _connectionTimeoutSeconds;
        private readonly ServiceLog _log;
        private readonly SourceTimeConverter _time;
        private readonly Dictionary<string, HistoryTagInfo> _tagCache = new Dictionary<string, HistoryTagInfo>(StringComparer.OrdinalIgnoreCase);

        public HistorianProvider(string server, int connectionTimeoutSeconds) : this(server, connectionTimeoutSeconds, null, new SourceTimeConverter(TimeZoneInfo.Local.Id)) { }
        public HistorianProvider(string server, int connectionTimeoutSeconds, ServiceLog log) : this(server, connectionTimeoutSeconds, log, new SourceTimeConverter(TimeZoneInfo.Local.Id)) { }
        public HistorianProvider(string server, int connectionTimeoutSeconds, ServiceLog log, SourceTimeConverter time)
        {
            if (time == null) throw new ArgumentNullException("time");
            _server = server; _connectionTimeoutSeconds = connectionTimeoutSeconds; _log = log; _time = time;
        }

        public HistorianStatus Probe()
        {
            int id = -1;
            try
            {
                EnsureInitialized();
                IDvCHDataAccess api = DvAccess.ReadInterface;
                id = api.createConnection(_server, "DcsDataService", _connectionTimeoutSeconds);
                DvCHReadConnection connection = api.connection(id);
                int state = connection.getDvCHServerState();
                Log("Historian connect server=" + _server + " state=" + state.ToString(CultureInfo.InvariantCulture));
                return new HistorianStatus { DllAvailable = true, Connected = true, Server = _server, ServerState = state, Message = "OK" };
            }
            catch (Exception ex) { Failure("Historian probe failure", ex); throw Wrap("Historian probe failed.", ex); }
            finally { Close(id); }
        }

        public IList<HistoryTagInfo> ResolveTags(IList<string> tags)
        {
            if (tags == null) throw new ArgumentNullException("tags");
            int id = -1;
            try { DvCHReadConnection c = Open(out id); return ResolveTagsConnected(c, tags); }
            catch (Exception ex) { Failure("Historian tag resolution failure", ex); throw Wrap("Historian tag resolution failed.", ex); }
            finally { Close(id); }
        }

        public void ReadRawStream(string tag, DateTime start, DateTime end, int readChunkSamples, TimeSpan streamWindow, Action<IList<HistorySample>> onBatch)
        {
            if (onBatch == null) throw new ArgumentNullException("onBatch");
            using (HistorianStream stream = PrepareRawStream(tag, start, end, readChunkSamples, streamWindow)) stream.Stream(onBatch);
        }

        public HistorianStream PrepareRawStream(string tag, DateTime start, DateTime end, int readChunkSamples, TimeSpan streamWindow)
        {
            ValidateStreamArguments(tag, start, end, readChunkSamples, streamWindow);
            int id = -1; HistorianStream result = null;
            try
            {
                DvCHReadConnection connection = Open(out id);
                HistoryTagInfo info = ResolveTagsConnected(connection, new List<string> { tag })[0];
                if (info.Status != HistoryTagStatus.HistoryTagOK) throw new ArgumentException("Tag is " + info.Status + ": " + info.Tag, "tag");
                result = new HistorianStream(this, connection, id, info, start, end, readChunkSamples, streamWindow); id = -1;
                Log("History stream prepared tag=" + tag + " from=" + FormatDate(start) + " to=" + FormatDate(end));
                return result;
            }
            catch (HistorianException) { throw; }
            catch (ArgumentException) { throw; }
            catch (Exception ex) { Failure("Historian stream preparation failure", ex); throw Wrap("Historian stream preparation failed.", ex); }
            finally { if (id >= 0) Close(id); }
        }

        public sealed class HistorianStream : IDisposable
        {
            private readonly HistorianProvider _owner;
            private readonly DvCHReadConnection _connection;
            private readonly HistoryTagInfo _tag;
            private readonly DateTime _start;
            private readonly DateTime _end;
            private readonly int _readChunkSamples;
            private readonly TimeSpan _streamWindow;
            private int _connectionId;
            private int _streamed;

            internal HistorianStream(HistorianProvider owner, DvCHReadConnection connection, int connectionId, HistoryTagInfo tag, DateTime start, DateTime end, int readChunkSamples, TimeSpan streamWindow)
            {
                _owner = owner; _connection = connection; _connectionId = connectionId; _tag = tag; _start = start; _end = end; _readChunkSamples = readChunkSamples; _streamWindow = streamWindow;
            }

            public string Tag { get { return _tag.Tag; } }
            public HistoryTagInfo TagInfo { get { return _tag; } }
            internal DvCHReadConnection Connection { get { return _connection; } }
            internal HistoryTagInfo ResolvedTag { get { return _tag; } }
            internal DateTime Start { get { return _start; } }
            internal DateTime End { get { return _end; } }
            internal int ReadChunkSamples { get { return _readChunkSamples; } }
            internal TimeSpan StreamWindow { get { return _streamWindow; } }

            public void Stream(Action<IList<HistorySample>> onBatch)
            {
                if (onBatch == null) throw new ArgumentNullException("onBatch");
                if (_connectionId < 0) throw new ObjectDisposedException("HistorianStream");
                if (Interlocked.Exchange(ref _streamed, 1) != 0) throw new InvalidOperationException("HistorianStream can only be consumed once.");
                try { _owner.StreamPrepared(this, onBatch); }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                int id = Interlocked.Exchange(ref _connectionId, -1);
                if (id >= 0) _owner.Close(id);
            }
        }

        private void StreamPrepared(HistorianStream stream, Action<IList<HistorySample>> onBatch)
        {
            try
            {
                DateTime windowStart = stream.Start; HistorySample previousEmittedSample = null; long totalSamples = 0;
                while (windowStart < stream.End)
                {
                    TimeSpan remaining = stream.End.Subtract(windowStart); DateTime windowEnd = remaining <= stream.StreamWindow ? stream.End : windowStart.Add(stream.StreamWindow); long windowSamples = 0;
                    ReadRecursive(stream.Connection, stream.ResolvedTag, windowStart, windowEnd, stream.ReadChunkSamples, 0, delegate(IList<HistorySample> normalized)
                    {
                        List<HistorySample> batch = new List<HistorySample>(normalized.Count);
                        for (int i = 0; i < normalized.Count; i++)
                        {
                            HistorySample sample = normalized[i];
                            if (previousEmittedSample != null && SameSample(previousEmittedSample, sample)) continue;
                            batch.Add(sample); previousEmittedSample = sample;
                        }
                        if (batch.Count == 0) return;
                        windowSamples += batch.Count; totalSamples += batch.Count; onBatch(batch);
                    });
                    Log("History window complete tag=" + stream.ResolvedTag.Tag + " windowStart=" + FormatDate(windowStart) + " windowEnd=" + FormatDate(windowEnd) + " sampleCount=" + windowSamples.ToString(CultureInfo.InvariantCulture));
                    windowStart = windowEnd;
                }
                Log("History stream provider complete tag=" + stream.ResolvedTag.Tag + " totalSamples=" + totalSamples.ToString(CultureInfo.InvariantCulture));
            }
            catch (HistorianException) { throw; }
            catch (ArgumentException) { throw; }
            catch (Exception ex) { Failure("Historian raw stream failure", ex); throw Wrap("Historian raw stream failed.", ex); }
        }

        private static void ValidateStreamArguments(string tag, DateTime start, DateTime end, int readChunkSamples, TimeSpan streamWindow)
        {
            if (String.IsNullOrEmpty(tag)) throw new ArgumentException("Tag is required.", "tag");
            if (end <= start) throw new ArgumentException("End time must be after start time.");
            if (readChunkSamples < 1) throw new ArgumentOutOfRangeException("readChunkSamples");
            if (streamWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("streamWindow");
        }

        private DvCHReadConnection Open(out int id)
        {
            EnsureInitialized(); IDvCHDataAccess api = DvAccess.ReadInterface;
            id = api.createConnection(_server, "DcsDataService", _connectionTimeoutSeconds);
            DvCHReadConnection connection = api.connection(id); Log("Historian connect server=" + _server + " connectionId=" + id.ToString(CultureInfo.InvariantCulture)); return connection;
        }

        private IList<HistoryTagInfo> ResolveTagsConnected(DvCHReadConnection connection, IList<string> tags)
        {
            List<HistoryTagInfo> cachedResult = new List<HistoryTagInfo>(); bool allCached = true;
            lock (_tagCacheGate) for (int i = 0; i < tags.Count; i++) { HistoryTagInfo cached; if (_tagCache.TryGetValue(tags[i], out cached) && (cached.Status != HistoryTagStatus.HistoryTagOK || !String.IsNullOrEmpty(cached.DataType))) cachedResult.Add(cached); else { allCached = false; break; } }
            if (allCached) return cachedResult;
            ArrayList names = new ArrayList(); for (int i = 0; i < tags.Count; i++) names.Add(tags[i]);
            int[] handles; int[] statuses; connection.getServerTagHandles(names, out handles, out statuses);
            List<HistoryTagInfo> result = new List<HistoryTagInfo>();
            for (int i = 0; i < tags.Count; i++)
            {
                int status = i < statuses.Length ? statuses[i] : -1;
                HistoryTagInfo info = new HistoryTagInfo { Tag = tags[i], Handle = i < handles.Length ? handles[i] : -1, Status = MapStatus(status), DataType = "" };
                lock (_tagCacheGate) { HistoryTagInfo cached; if (_tagCache.TryGetValue(info.Tag, out cached) && cached.Handle == info.Handle && cached.Status == info.Status) info.DataType = cached.DataType; _tagCache[info.Tag] = info; }
                result.Add(info);
            }
            PopulateTypeInfo(connection, result); return result;
        }

        private void PopulateTypeInfo(DvCHReadConnection connection, IList<HistoryTagInfo> tags)
        {
            ArrayList handles = new ArrayList();
            for (int i = 0; i < tags.Count; i++) if (tags[i].Status == HistoryTagStatus.HistoryTagOK) handles.Add(tags[i].Handle);
            if (handles.Count == 0) return;
            try
            {
                HistoryTagCollection metadata; connection.getTagTypeInfo(handles, out metadata);
                int index = 0; foreach (HistoryTag tag in metadata) { while (index < tags.Count && tags[index].Status != HistoryTagStatus.HistoryTagOK) index++; if (index >= tags.Count) break; tags[index].DataType = tag.dataType.ToString(); lock (_tagCacheGate) _tagCache[tags[index].Tag] = tags[index]; index++; }
            }
            catch (Exception ex) { if (_log != null) for (int i = 0; i < tags.Count; i++) if (tags[i].Status == HistoryTagStatus.HistoryTagOK) _log.Warning("Tag metadata unavailable tag=" + tags[i].Tag + " reason=" + ex.Message, ex); }
        }

        private static HistoryTagStatus MapStatus(int value)
        {
            if (value == (int)ServerHandleStatus.HistoryTagOK) return HistoryTagStatus.HistoryTagOK;
            if (value == (int)ServerHandleStatus.HistoryTagUnknown) return HistoryTagStatus.HistoryTagUnknown;
            if (value == (int)ServerHandleStatus.HistoryTagAmbiguous) return HistoryTagStatus.HistoryTagAmbiguous;
            return HistoryTagStatus.Error;
        }

        private void ReadRecursive(DvCHReadConnection connection, HistoryTagInfo tag, DateTime start, DateTime end, int maxSamples, int depth, Action<IList<HistorySample>> onBatch)
        {
            RawSegment segment = ReadSegment(connection, tag, start, end, maxSamples);
            if (segment.Truncated && depth < MaxSplitDepth && end.Subtract(start) > MinimumSlice)
            {
                DateTime middle = start.AddTicks((end.Ticks - start.Ticks) / 2);
                segment.Rows.Clear();
                ReadRecursive(connection, tag, start, middle, maxSamples, depth + 1, onBatch);
                ReadRecursive(connection, tag, middle, end, maxSamples, depth + 1, onBatch); return;
            }
            if (segment.Truncated) throw new HistorianException("Historian result remained truncated at depth " + depth.ToString(CultureInfo.InvariantCulture) + " for " + start.ToString("o", CultureInfo.InvariantCulture) + " to " + end.ToString("o", CultureInfo.InvariantCulture) + "; incomplete data was rejected.");
            IList<HistorySample> normalized = Normalize(segment.Rows); if (normalized.Count > 0) onBatch(normalized);
        }

        private RawSegment ReadSegment(DvCHReadConnection connection, HistoryTagInfo tag, DateTime start, DateTime end, int maxSamples)
        {
            int spanId = -1;
            try
            {
                IDvCHDataAccess api = DvAccess.ReadInterface; spanId = api.createTimeSpan(); DvCHTimeSpan span = api.getTimeSpan(spanId);
                // DeltaV 10.3 exposes DateTime and FILETIME overloads. The verified
                // legacy reader resolves the FILETIME overload, while the DateTime
                // overload can return an empty dataSamples collection for the same
                // source-local interval on a real DCS. Keep this call strong-typed
                // and preserve the verified wire representation.
                span.setAbsoluteStartTime(ToHistorianFileTime(start));
                span.setAbsoluteEndTime(ToHistorianFileTime(end));
                RawHistorySamples raw = connection.readRaw(spanId, tag.Handle, DataInclusionType.AllSamples, SampleBoundaryType.None, SampleBoundaryType.None, maxSamples);
                RawSegment result = new RawSegment(); result.Truncated = raw.dataTruncated;
                foreach (HistoryDataPoint point in raw.dataSamples)
                {
                    result.Rows.Add(new HistorySample { Tag = tag.Tag, Timestamp = point.timestamp, Value = point.value, DataType = point.dataType.ToString(), DeltaVStatus = point.deltaVStatus.ToString(), ArchiveStatus = point.archiveStatus.ToString(), SequenceNo = point.sequenceNo, IsHistoryHole = point.isHistoryHole, IsCRHole = point.isCRHole, IsManuallyDeleted = point.isManuallyDeleted, IsManuallyInserted = point.isManuallyInserted });
                }
                return result;
            }
            finally { if (spanId >= 0) DvAccess.ReadInterface.releaseTimeSpan(spanId); }
        }

        private FILETIME ToHistorianFileTime(DateTime sourceTime)
        {
            long value = _time.SourceToRawUtc(sourceTime).ToFileTimeUtc();
            FILETIME result = new FILETIME(); result.dwLowDateTime = unchecked((int)(value & 0xFFFFFFFFL)); result.dwHighDateTime = unchecked((int)(value >> 32)); return result;
        }

        public static IList<HistorySample> Normalize(IList<HistorySample> rows)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            List<HistorySample> sorted = new List<HistorySample>(rows); sorted.Sort(delegate(HistorySample a, HistorySample b) { return a.Timestamp.CompareTo(b.Timestamp); });
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal); List<HistorySample> result = new List<HistorySample>();
            for (int i = 0; i < sorted.Count; i++) { HistorySample s = sorted[i]; string key = SampleKey(s); if (!seen.ContainsKey(key)) { seen.Add(key, true); result.Add(s); } }
            return result;
        }

        private static bool SameSample(HistorySample a, HistorySample b) { return String.Equals(SampleKey(a), SampleKey(b), StringComparison.Ordinal); }

        private static string SampleKey(HistorySample s)
        {
            return s.Timestamp.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + ValueField(s.Value) + "|" + Field(s.DataType) + "|" + Field(s.DeltaVStatus) + "|" + Field(s.ArchiveStatus) + "|" + s.SequenceNo.ToString(CultureInfo.InvariantCulture) + "|" + s.IsHistoryHole.ToString() + "|" + s.IsCRHole.ToString() + "|" + s.IsManuallyDeleted.ToString() + "|" + s.IsManuallyInserted.ToString();
        }

        private static string ValueField(object value) { return value == null || value == DBNull.Value ? "null" : Field(Format(value)); }
        private static string Field(string value) { return value == null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }
        private static string Format(object value) { IFormattable f = value as IFormattable; return value == null ? "" : (f == null ? value.ToString() : f.ToString(null, CultureInfo.InvariantCulture)); }
        private static string FormatDate(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture); }
        private static void EnsureInitialized() { if (_initialized) return; lock (InitializeGate) { if (_initialized) return; DvAccess.Initialize(); _initialized = true; } }
        private static HistorianException Wrap(string message, Exception ex) { return ex as HistorianException ?? new HistorianException(message + " " + ex.Message, ex); }
        private void Log(string message) { if (_log != null) _log.Info(message); }
        private void Failure(string message, Exception ex) { if (_log != null) _log.Error(message, ex); }
        private void Close(int id) { if (id >= 0) try { DvAccess.closeConnection(id); } catch (Exception ex) { Failure("Historian closeConnection failed connectionId=" + id.ToString(CultureInfo.InvariantCulture), ex); } }
        private sealed class RawSegment { public bool Truncated; public readonly List<HistorySample> Rows = new List<HistorySample>(); }
    }
}
