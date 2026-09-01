using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using DcsDataService.Configuration;
using DcsDataService.Util;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventProvider
    {
        private readonly ServiceConfig _config;
        private readonly string _table;
        private readonly object _stateGate = new object();
        private readonly ServiceLog _log;
        private EventSourceInfo _cachedState;
        private DateTime _stateExpiresUtc = DateTime.MinValue;

        public EventProvider(ServiceConfig config) : this(config, null) { }
        public EventProvider(ServiceConfig config, ServiceLog log) { _config = config; _log = log; _table = Quote(config.EventsSchema) + "." + Quote(config.EventsTable); }

        public EventSourceInfo Probe()
        {
            using (SqlConnection connection = Open())
            {
                EnsureCursorFields(connection); EventSourceInfo info = ReadSourceInfo(connection); info.OverflowHasRows = ReadOverflow(connection); return info;
            }
        }

        public EventSourceInfo ValidateRuntimeSourceState() { using (SqlConnection connection = Open()) return RuntimeState(connection); }
        public EventCursor GetLatestCursor() { using (SqlConnection connection = Open()) return ReadEdge(connection, false); }
        public EventCursor GetEarliestCursor() { using (SqlConnection connection = Open()) return ReadEdge(connection, true); }

        public void StreamRange(DateTime from, DateTime to, Action<EventRecord> onRecord)
        {
            if (onRecord == null) throw new ArgumentNullException("onRecord");
            using (EventStream stream = PrepareRangeStream(from, to, TimeSpan.FromMinutes(_config.EventStreamWindowMinutes))) stream.Stream(onRecord);
        }

        public void StreamAfter(EventCursor cursor, DateTime to, string sourceGeneration, Action<EventRecord> onRecord)
        {
            if (onRecord == null) throw new ArgumentNullException("onRecord");
            using (EventStream stream = PrepareAfterStream(cursor, to, sourceGeneration, TimeSpan.FromMinutes(_config.EventStreamWindowMinutes))) stream.Stream(onRecord);
        }

        public EventStream PrepareRangeStream(DateTime from, DateTime to, TimeSpan streamWindow)
        {
            ValidateRange(from, to);
            ValidateStreamWindow(streamWindow);
            SqlConnection connection = Open(); EventStream result = null;
            try
            {
                EventSourceInfo state = RuntimeState(connection); EnsureCursorFields(connection); EventCursor earliest = ReadEdge(connection, true);
                if (earliest != null && from < earliest.DateTimeValue) throw new EventCursorException("retention_gap", "Requested range starts before the earliest retained Journal event.");
                result = new EventStream(this, connection, from, to, null, state.Generation, streamWindow, earliest); connection = null; return result;
            }
            finally { if (result == null && connection != null) connection.Dispose(); }
        }

        public EventStream PrepareAfterStream(EventCursor cursor, DateTime to, string sourceGeneration, TimeSpan streamWindow)
        {
            if (cursor == null) throw new ArgumentNullException("cursor");
            if (String.IsNullOrEmpty(sourceGeneration)) throw new ArgumentException("sourceGeneration is required.", "sourceGeneration");
            ValidateRange(cursor.DateTimeValue, to);
            ValidateStreamWindow(streamWindow);
            SqlConnection connection = Open(); EventStream result = null;
            try
            {
                EventSourceInfo state = RuntimeState(connection); EnsureCursorFields(connection); EventCursor earliest = ReadEdge(connection, true); EventCursor latest = ReadEdge(connection, false);
                ValidateCursorWindow(cursor, earliest, latest, sourceGeneration, state.Generation);
                result = new EventStream(this, connection, cursor.DateTimeValue, to, cursor, state.Generation, streamWindow, earliest); connection = null; return result;
            }
            finally { if (result == null && connection != null) connection.Dispose(); }
        }

        public sealed class EventStream : IDisposable
        {
            private readonly EventProvider _owner;
            private readonly SqlConnection _connection;
            private readonly DateTime _from;
            private readonly DateTime _to;
            private readonly EventCursor _after;
            private readonly string _sourceGeneration;
            private readonly TimeSpan _streamWindow;
            private readonly EventCursor _initialEarliest;
            private int _disposed;
            private int _streamed;

            internal EventStream(EventProvider owner, SqlConnection connection, DateTime from, DateTime to, EventCursor after, string sourceGeneration, TimeSpan streamWindow, EventCursor initialEarliest)
            {
                if (streamWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("streamWindow");
                _owner = owner; _connection = connection; _from = from; _to = to; _after = after; _sourceGeneration = sourceGeneration; _streamWindow = streamWindow; _initialEarliest = initialEarliest;
            }

            public string SourceGeneration { get { return _sourceGeneration; } }
            internal SqlConnection Connection { get { return _connection; } }
            internal DateTime From { get { return _from; } }
            internal DateTime To { get { return _to; } }
            internal EventCursor After { get { return _after; } }
            internal TimeSpan StreamWindow { get { return _streamWindow; } }
            internal EventCursor InitialEarliest { get { return _initialEarliest; } }

            public void Stream(Action<EventRecord> onRecord)
            {
                Stream(onRecord, null);
            }

            public void Stream(Action<EventRecord> onRecord, Action<DateTime, DateTime, long> onWindowComplete)
            {
                if (onRecord == null) throw new ArgumentNullException("onRecord");
                if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) throw new ObjectDisposedException("EventStream");
                if (Interlocked.Exchange(ref _streamed, 1) != 0) throw new InvalidOperationException("EventStream can only be consumed once.");
                try { _owner.StreamPrepared(this, onRecord, onWindowComplete); }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) _connection.Dispose();
            }
        }

        private void StreamPrepared(EventStream stream, Action<EventRecord> onRecord, Action<DateTime, DateTime, long> onWindowComplete)
        {
            Stopwatch clock = Stopwatch.StartNew(); long totalRows = 0; int windowCount = 0; bool firstWindow = true;
            TimeWindowSplitter.ForEach(stream.From, stream.To, stream.StreamWindow, delegate(DateTime windowStart, DateTime windowEnd)
            {
                EventSourceInfo before = RuntimeState(stream.Connection);
                EnsureGenerationUnchanged(stream.SourceGeneration, before.Generation);
                ValidateWindowRetention(stream, windowStart, ReadEdge(stream.Connection, true), firstWindow);

                bool useCursor = firstWindow && stream.After != null;
                long windowRows = ReadWindow(stream.Connection, windowStart, windowEnd, useCursor, stream.After, onRecord);

                EventSourceInfo after = RuntimeState(stream.Connection);
                EnsureGenerationUnchanged(stream.SourceGeneration, after.Generation);
                ValidateWindowRetention(stream, windowStart, ReadEdge(stream.Connection, true), firstWindow);
                windowCount++; totalRows += windowRows;
                Log("Event window complete windowStart=" + FormatDate(windowStart) + " windowEnd=" + FormatDate(windowEnd) + " rowCount=" + windowRows.ToString(CultureInfo.InvariantCulture));
                if (onWindowComplete != null) onWindowComplete(windowStart, windowEnd, windowRows);
                firstWindow = false;
            });
            clock.Stop(); Log("Event stream provider complete windowCount=" + windowCount.ToString(CultureInfo.InvariantCulture) + " rows=" + totalRows.ToString(CultureInfo.InvariantCulture) + " durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
        }

        private long ReadWindow(SqlConnection connection, DateTime from, DateTime to, bool useCursor, EventCursor cursor, Action<EventRecord> onRecord)
        {
            long rows = 0; string sql = useCursor ? AfterSql() : RangeSql();
            using (SqlCommand command = Command(connection, sql))
            {
                command.Parameters.Add("@windowFrom", SqlDbType.DateTime).Value = from;
                command.Parameters.Add("@windowTo", SqlDbType.DateTime).Value = to;
                if (useCursor) AddCursor(command, cursor);
                using (SqlDataReader reader = command.ExecuteReader()) while (reader.Read()) { onRecord(ReadRecord(reader)); rows++; }
            }
            return rows;
        }

        private static void ValidateWindowRetention(EventStream stream, DateTime windowStart, EventCursor earliest, bool firstWindow)
        {
            if (stream.After != null)
            {
                if (earliest == null) throw new EventCursorException("event_cursor_expired", "Source retention removed all Event rows while the query was running.");
                if (firstWindow && earliest.CompareTo(stream.After) > 0) throw new EventCursorException("event_cursor_expired", "Source retention advanced past the requested cursor while the query was running.");
                if (earliest.DateTimeValue > windowStart) throw new EventCursorException("event_cursor_expired", "Source retention advanced into the requested Event window while the query was running.");
                return;
            }
            if (earliest == null)
            {
                if (stream.InitialEarliest != null) throw new EventCursorException("retention_gap", "Source retention removed all Event rows while the query was running.");
                return;
            }
            if (earliest != null && earliest.DateTimeValue > windowStart) throw new EventCursorException("retention_gap", "Source retention advanced into the requested Event window while the query was running.");
        }

        public static void ValidateRange(DateTime from, DateTime to) { if (to <= from) throw new ArgumentException("Range end must be after range start."); }
        private static void ValidateStreamWindow(TimeSpan streamWindow) { if (streamWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("streamWindow"); }

        public static void EnsureSourceSafe(EventSourceInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            if (info.OverflowHasRows) throw new EventSourceUnsafeException("event_overflow", "EJOverflow contains rows; refusing to return an incomplete Event Journal view.");
            if (info.IsFull) throw new EventSourceUnsafeException("event_journal_full", "EJournal reports IsFull=1; source-store review is required.");
        }

        public static void ValidateCursorWindow(EventCursor cursor, EventCursor earliest, EventCursor latest, string expectedGeneration, string actualGeneration)
        {
            if (cursor == null) throw new ArgumentNullException("cursor");
            if (!String.IsNullOrEmpty(expectedGeneration) && !String.Equals(expectedGeneration, actualGeneration, StringComparison.Ordinal)) throw new EventCursorException("source_changed", "Event Journal sourceGeneration changed; refusing to reuse the cursor.");
            if (earliest == null || latest == null) throw new EventCursorException("cursor_window_empty", "Journal cursor range is empty while a cursor was supplied.");
            if (earliest.CompareTo(cursor) > 0) throw new EventCursorException("event_cursor_expired", "Requested cursor is older than the earliest retained Event Journal row.");
            if (latest.CompareTo(cursor) < 0) throw new EventCursorException("cursor_ahead", "Latest Journal cursor is older than the supplied cursor; the source may have been rebuilt.");
        }

        private EventSourceInfo RuntimeState(SqlConnection connection)
        {
            EventSourceInfo observed = ReadSourceIdentity(connection);
            lock (_stateGate)
            {
                if (_cachedState != null && DateTime.UtcNow < _stateExpiresUtc && String.Equals(_cachedState.Generation, observed.Generation, StringComparison.Ordinal)) observed.OverflowHasRows = _cachedState.OverflowHasRows;
                else { observed.OverflowHasRows = ReadOverflow(connection); _stateExpiresUtc = DateTime.UtcNow.AddSeconds(_config.EventsStateCacheSeconds); }
                EnsureSourceSafe(observed); _cachedState = observed; return observed;
            }
        }

        private static void EnsureGenerationUnchanged(string before, string after) { if (!String.Equals(before, after, StringComparison.Ordinal)) throw new EventCursorException("source_changed", "Event Journal sourceGeneration changed while the query was running."); }

        private string RangeSql() { return Select() + " WHERE [Date_Time]>=@windowFrom AND [Date_Time]<@windowTo AND [FracSec] IS NOT NULL ORDER BY [Date_Time],[FracSec],[Ord];"; }
        private string AfterSql() { return Select() + " WHERE [Date_Time]>=@windowFrom AND [Date_Time]<@windowTo AND [FracSec] IS NOT NULL AND ([Date_Time]>@cursorDateTime OR ([Date_Time]=@cursorDateTime AND [FracSec]>@cursorFracSec) OR ([Date_Time]=@cursorDateTime AND [FracSec]=@cursorFracSec AND [Ord]>@cursorOrd)) ORDER BY [Date_Time],[FracSec],[Ord];"; }
        private string Select() { return "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT [Date_Time],[FracSec],[Event_Type],[Event_SubType],[Category],[Area],[Node],[Unit],[Module],[Module_Description],[Attribute],[State],[Event_Level],[Desc1],[Desc2],[IsArchived],[Ord] FROM " + _table; }

        private static EventRecord ReadRecord(SqlDataReader reader)
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(16)) throw new InvalidOperationException("Journal contains a null Date_Time, FracSec or Ord cursor field.");
            return new EventRecord { DateTimeValue = reader.GetDateTime(0), FracSec = Convert.ToInt16(reader.GetValue(1), CultureInfo.InvariantCulture), EventType = S(reader, 2), EventSubType = S(reader, 3), Category = S(reader, 4), Area = S(reader, 5), Node = S(reader, 6), Unit = S(reader, 7), Module = S(reader, 8), ModuleDescription = S(reader, 9), Attribute = S(reader, 10), State = S(reader, 11), EventLevel = S(reader, 12), Desc1 = S(reader, 13), Desc2 = S(reader, 14), IsArchived = reader.IsDBNull(15) ? (short?)null : Convert.ToInt16(reader.GetValue(15), CultureInfo.InvariantCulture), Ord = reader.GetInt32(16) };
        }

        private EventCursor ReadEdge(SqlConnection connection, bool earliest)
        {
            string direction = earliest ? "ASC" : "DESC";
            using (SqlCommand command = Command(connection, ReadEdgeSql(earliest)))
            using (SqlDataReader reader = command.ExecuteReader()) return reader.Read() ? Cursor(reader, 0) : null;
        }

        private string ReadEdgeSql(bool earliest)
        {
            string direction = earliest ? "ASC" : "DESC";
            return "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT TOP 1 [Date_Time],[FracSec],[Ord] FROM " + _table + " WHERE [Date_Time] IS NOT NULL AND [FracSec] IS NOT NULL ORDER BY [Date_Time] " + direction + ",[FracSec] " + direction + ",[Ord] " + direction + ";";
        }

        private EventSourceInfo ReadSourceInfo(SqlConnection connection)
        {
            EventSourceInfo info = ReadSourceIdentity(connection);
            using (SqlCommand command = Command(connection, "SELECT COALESCE(SUM(p.rows),0) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE s.name=@schema AND t.name=@table;")) { command.Parameters.AddWithValue("@schema", _config.EventsSchema); command.Parameters.AddWithValue("@table", _config.EventsTable); info.EstimatedRows = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); }
            return info;
        }

        private EventSourceInfo ReadSourceIdentity(SqlConnection connection)
        {
            EventSourceInfo info = new EventSourceInfo();
            using (SqlCommand command = Command(connection, ReadSourceIdentitySql()))
            using (SqlDataReader reader = command.ExecuteReader()) { if (!reader.Read()) throw new InvalidOperationException("JournalProperties is empty."); info.SourceNode = reader.IsDBNull(0) ? "" : reader.GetString(0); if (reader.IsDBNull(1)) throw new InvalidOperationException("JournalProperties.CreateTime is null."); info.CreateTime = reader.GetDateTime(1); info.IsFull = !reader.IsDBNull(2) && Convert.ToInt16(reader.GetValue(2), CultureInfo.InvariantCulture) != 0; }
            info.Generation = info.SourceNode + "|" + info.CreateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture); return info;
        }

        private static string ReadSourceIdentitySql() { return "SELECT TOP 1 [SourceNode],[CreateTime],[IsFull] FROM [dbo].[JournalProperties];"; }

        private bool ReadOverflow(SqlConnection connection)
        {
            string original = connection.Database; bool changed = false;
            try { connection.ChangeDatabase("EJOverflow"); changed = true; using (SqlCommand command = Command(connection, ReadOverflowSql())) return command.ExecuteScalar() != null; }
            catch (Exception ex) { throw new InvalidOperationException("Unable to verify whether EJOverflow is empty (fail-closed).", ex); }
            finally { if (changed && !connection.Database.Equals(original, StringComparison.OrdinalIgnoreCase)) connection.ChangeDatabase(original); }
        }

        private static string ReadOverflowSql() { return "SELECT TOP 1 1 FROM [dbo].[Journal] WITH (NOLOCK);"; }

        private void EnsureCursorFields(SqlConnection connection)
        {
            string[] fields = { "Date_Time", "FracSec", "Ord" };
            for (int i = 0; i < fields.Length; i++) using (SqlCommand command = Command(connection, "IF EXISTS(SELECT 1 FROM " + _table + " WITH (NOLOCK) WHERE [" + fields[i] + "] IS NULL) SELECT 1 ELSE SELECT 0;")) if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0) throw new InvalidOperationException("Journal contains null " + fields[i] + " values; cursor would be unsafe.");
        }

        private SqlConnection Open() { SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder { DataSource = _config.EventsServer, InitialCatalog = _config.EventsDatabase, IntegratedSecurity = true, ConnectTimeout = Math.Min(30, _config.EventsCommandTimeoutSeconds), ApplicationName = "DcsDataService" }; SqlConnection connection = new SqlConnection(builder.ConnectionString); connection.Open(); return connection; }
        private SqlCommand Command(SqlConnection connection, string sql) { SqlCommand command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = _config.EventsCommandTimeoutSeconds; return command; }
        private static void AddCursor(SqlCommand command, EventCursor cursor) { command.Parameters.Add("@cursorDateTime", SqlDbType.DateTime).Value = cursor.DateTimeValue; command.Parameters.Add("@cursorFracSec", SqlDbType.SmallInt).Value = cursor.FracSec; command.Parameters.Add("@cursorOrd", SqlDbType.Int).Value = cursor.Ord; }
        private static EventCursor Cursor(SqlDataReader reader, int start) { return new EventCursor { DateTimeValue = reader.GetDateTime(start), FracSec = Convert.ToInt16(reader.GetValue(start + 1), CultureInfo.InvariantCulture), Ord = reader.GetInt32(start + 2) }; }
        private static string S(SqlDataReader reader, int ordinal) { return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture); }
        private static string Quote(string identifier) { return "[" + identifier.Replace("]", "]]" ) + "]"; }
        private static string FormatDate(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture); }
        private void Log(string message) { if (_log != null) _log.Info(message); }
    }
}
