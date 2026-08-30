using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;
using DeltaV.Historian.Data;
using DeltaV.Historian.DvCHDataAccess;
using DcsDataService.Util;
using DvAccess = DeltaV.Historian.DvCHDataAccess.DvCHDataAccess;

namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistorianProvider
    {
        private const int MaxSplitDepth = 20;
        private static readonly TimeSpan MinimumSlice = TimeSpan.FromSeconds(2);
        private static readonly object InitializeGate = new object();
        private static bool _initialized;
        private readonly object _gate = new object();
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
            lock (_gate)
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
        }

        public IList<HistoryTagInfo> ResolveTags(IList<string> tags)
        {
            if (tags == null) throw new ArgumentNullException("tags");
            lock (_gate)
            {
                int id = -1;
                try { DvCHReadConnection c = Open(out id); return ResolveTagsConnected(c, tags); }
                catch (Exception ex) { Failure("Historian tag resolution failure", ex); throw Wrap("Historian tag resolution failed.", ex); }
                finally { Close(id); }
            }
        }

        public IList<HistorySample> ReadRaw(string tag, DateTime start, DateTime end, int maxSamples)
        {
            IDictionary<string, IList<HistorySample>> result = ReadRaw(new List<string> { tag }, start, end, maxSamples, Int32.MaxValue);
            return result[tag];
        }

        public IDictionary<string, IList<HistorySample>> ReadRaw(IList<string> tags, DateTime start, DateTime end, int maxSamples)
        {
            return ReadRaw(tags, start, end, maxSamples, Int32.MaxValue);
        }

        public IDictionary<string, IList<HistorySample>> ReadRaw(IList<string> tags, DateTime start, DateTime end, int maxSamples, int maxTotalSamples)
        {
            if (tags == null) throw new ArgumentNullException("tags");
            if (tags.Count == 0) throw new ArgumentException("At least one tag is required.", "tags");
            if (end <= start) throw new ArgumentException("End time must be after start time.");
            if (maxSamples < 1) throw new ArgumentOutOfRangeException("maxSamples");
            if (maxTotalSamples < 1) throw new ArgumentOutOfRangeException("maxTotalSamples");
            Dictionary<string, bool> unique = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < tags.Count; i++) { if (String.IsNullOrEmpty(tags[i])) throw new ArgumentException("Tag names cannot be empty.", "tags"); if (unique.ContainsKey(tags[i])) throw new ArgumentException("Duplicate tag: " + tags[i], "tags"); unique.Add(tags[i], true); }
            lock (_gate)
            {
                int id = -1;
                try
                {
                    DvCHReadConnection connection = Open(out id);
                    IList<HistoryTagInfo> resolved = ResolveTagsConnected(connection, tags);
                    Dictionary<string, IList<HistorySample>> result = new Dictionary<string, IList<HistorySample>>(StringComparer.OrdinalIgnoreCase);
                    HistorySampleBudget budget = new HistorySampleBudget(maxTotalSamples);
                    for (int i = 0; i < resolved.Count; i++)
                    {
                        HistoryTagInfo info = resolved[i];
                        if (info.Status != HistoryTagStatus.HistoryTagOK) throw new ArgumentException("Tag is " + info.Status + ": " + info.Tag, "tags");
                        List<HistorySample> rows = new List<HistorySample>();
                        ReadRecursive(connection, info, start, end, maxSamples, 0, rows, budget);
                        result[info.Tag] = Normalize(rows);
                    }
                    return result;
                }
                catch (HistorianException) { throw; }
                catch (HistoryQueryTooLargeException) { throw; }
                catch (ArgumentException) { throw; }
                catch (Exception ex) { Failure("Historian multi-tag raw read failure", ex); throw Wrap("Historian raw read failed.", ex); }
                finally { Close(id); }
            }
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
            for (int i = 0; i < tags.Count; i++) { HistoryTagInfo cached; if (_tagCache.TryGetValue(tags[i], out cached) && (cached.Status != HistoryTagStatus.HistoryTagOK || !String.IsNullOrEmpty(cached.DataType))) cachedResult.Add(cached); else { allCached = false; break; } }
            if (allCached) return cachedResult;
            ArrayList names = new ArrayList(); for (int i = 0; i < tags.Count; i++) names.Add(tags[i]);
            int[] handles; int[] statuses; connection.getServerTagHandles(names, out handles, out statuses);
            List<HistoryTagInfo> result = new List<HistoryTagInfo>();
            for (int i = 0; i < tags.Count; i++)
            {
                int status = i < statuses.Length ? statuses[i] : -1;
                HistoryTagInfo info = new HistoryTagInfo { Tag = tags[i], Handle = i < handles.Length ? handles[i] : -1, Status = MapStatus(status), DataType = "" };
                HistoryTagInfo cached; if (_tagCache.TryGetValue(info.Tag, out cached) && cached.Handle == info.Handle && cached.Status == info.Status) info.DataType = cached.DataType;
                _tagCache[info.Tag] = info; result.Add(info);
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
                int index = 0; foreach (HistoryTag tag in metadata) { while (index < tags.Count && tags[index].Status != HistoryTagStatus.HistoryTagOK) index++; if (index >= tags.Count) break; tags[index].DataType = tag.dataType.ToString(); _tagCache[tags[index].Tag] = tags[index]; index++; }
            }
            catch (Exception ex) { Failure("Historian getTagTypeInfo failed; tag handles remain usable", ex); }
        }

        private static HistoryTagStatus MapStatus(int value)
        {
            if (value == (int)ServerHandleStatus.HistoryTagOK) return HistoryTagStatus.HistoryTagOK;
            if (value == (int)ServerHandleStatus.HistoryTagUnknown) return HistoryTagStatus.HistoryTagUnknown;
            if (value == (int)ServerHandleStatus.HistoryTagAmbiguous) return HistoryTagStatus.HistoryTagAmbiguous;
            return HistoryTagStatus.Error;
        }

        private void ReadRecursive(DvCHReadConnection connection, HistoryTagInfo tag, DateTime start, DateTime end, int maxSamples, int depth, List<HistorySample> output, HistorySampleBudget budget)
        {
            RawSegment segment = ReadSegment(connection, tag, start, end, maxSamples);
            if (segment.Truncated && depth < MaxSplitDepth && end.Subtract(start) > MinimumSlice)
            {
                DateTime middle = start.AddTicks((end.Ticks - start.Ticks) / 2);
                ReadRecursive(connection, tag, start, middle, maxSamples, depth + 1, output, budget);
                ReadRecursive(connection, tag, middle, end, maxSamples, depth + 1, output, budget); return;
            }
            if (segment.Truncated) throw new HistorianException("Historian result remained truncated at depth " + depth.ToString(CultureInfo.InvariantCulture) + " for " + start.ToString("o", CultureInfo.InvariantCulture) + " to " + end.ToString("o", CultureInfo.InvariantCulture) + "; incomplete data was rejected.");
            budget.Add(segment.Rows.Count); output.AddRange(segment.Rows);
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
            FILETIME result = new FILETIME();
            result.dwLowDateTime = unchecked((int)(value & 0xFFFFFFFFL));
            result.dwHighDateTime = unchecked((int)(value >> 32));
            return result;
        }

        public static IList<HistorySample> Normalize(IList<HistorySample> rows)
        {
            List<HistorySample> sorted = new List<HistorySample>(rows); sorted.Sort(delegate(HistorySample a, HistorySample b) { return a.Timestamp.CompareTo(b.Timestamp); });
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal); List<HistorySample> result = new List<HistorySample>();
            for (int i = 0; i < sorted.Count; i++) { HistorySample s = sorted[i]; string key = s.Timestamp.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + Format(s.Value) + "|" + s.DataType + "|" + s.DeltaVStatus + "|" + s.ArchiveStatus + "|" + s.SequenceNo.ToString(CultureInfo.InvariantCulture) + "|" + s.IsHistoryHole + "|" + s.IsCRHole + "|" + s.IsManuallyDeleted + "|" + s.IsManuallyInserted; if (!seen.ContainsKey(key)) { seen.Add(key, true); result.Add(s); } }
            return result;
        }

        private static string Format(object value) { IFormattable f = value as IFormattable; return value == null ? "" : (f == null ? value.ToString() : f.ToString(null, CultureInfo.InvariantCulture)); }
        private static void EnsureInitialized() { if (_initialized) return; lock (InitializeGate) { if (_initialized) return; DvAccess.Initialize(); _initialized = true; } }
        private static HistorianException Wrap(string message, Exception ex) { return ex as HistorianException ?? new HistorianException(message + " " + ex.Message, ex); }
        private void Log(string message) { if (_log != null) _log.Info(message); }
        private void Failure(string message, Exception ex) { if (_log != null) _log.Error(message, ex); }
        private void Close(int id) { if (id >= 0) try { DvAccess.closeConnection(id); } catch (Exception ex) { Failure("Historian closeConnection failed connectionId=" + id.ToString(CultureInfo.InvariantCulture), ex); } }
        private sealed class RawSegment { public bool Truncated; public readonly List<HistorySample> Rows = new List<HistorySample>(); }
    }
}
