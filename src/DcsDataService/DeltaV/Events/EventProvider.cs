using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using DcsDataService.Configuration;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventProvider
    {
        private readonly ServiceConfig _config; private readonly string _table;
        public EventProvider(ServiceConfig config) { _config = config; _table = Quote(config.EventsSchema) + "." + Quote(config.EventsTable); }
        public EventSourceInfo Probe() { using (SqlConnection c = Open()) { EnsureCursorFields(c); EventSourceInfo info = ReadSourceInfo(c); info.OverflowHasRows = ReadOverflow(c); return info; } }
        public EventCursor GetLatestCursor() { return ReadEdge(false); }
        public EventCursor GetEarliestCursor() { return ReadEdge(true); }
        private EventCursor ReadEdge(bool earliest) { using (SqlConnection c = Open()) using (SqlCommand cmd = Command(c, "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT TOP 1 [Date_Time],[FracSec],[Ord] FROM " + _table + " WHERE [Date_Time] IS NOT NULL AND [FracSec] IS NOT NULL ORDER BY [Date_Time] " + (earliest ? "ASC" : "DESC") + ",[FracSec] " + (earliest ? "ASC" : "DESC") + ",[Ord] " + (earliest ? "ASC" : "DESC") + ";")) using (SqlDataReader r = cmd.ExecuteReader()) { return r.Read() ? Cursor(r, 0) : null; } }
        public IList<EventRecord> ReadAfter(EventCursor cursor, int limit)
        {
            if (cursor == null) throw new ArgumentNullException("cursor"); ValidateLimit(limit);
            string sql = Select(limit) + " WHERE [Date_Time]>=@cursorDateTime AND ([Date_Time]>@cursorDateTime OR ([Date_Time]=@cursorDateTime AND [FracSec]>@cursorFracSec) OR ([Date_Time]=@cursorDateTime AND [FracSec]=@cursorFracSec AND [Ord]>@cursorOrd)) ORDER BY [Date_Time],[FracSec],[Ord];";
            using (SqlConnection c = Open()) using (SqlCommand cmd = Command(c, sql)) { AddCursor(cmd, cursor); return Execute(cmd); }
        }
        public IList<EventRecord> ReadRange(DateTime from, DateTime to, EventCursor after, int limit)
        {
            if (to <= from) throw new ArgumentException("Range end must be after range start."); ValidateLimit(limit);
            string afterSql = after == null ? "" : " AND [Date_Time]>=@cursorDateTime AND ([Date_Time]>@cursorDateTime OR ([Date_Time]=@cursorDateTime AND [FracSec]>@cursorFracSec) OR ([Date_Time]=@cursorDateTime AND [FracSec]=@cursorFracSec AND [Ord]>@cursorOrd))";
            using (SqlConnection c = Open()) using (SqlCommand cmd = Command(c, Select(limit) + " WHERE [Date_Time]>=@from AND [Date_Time]<@to AND [FracSec] IS NOT NULL" + afterSql + " ORDER BY [Date_Time],[FracSec],[Ord];")) { cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from; cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = to; if (after != null) AddCursor(cmd, after); return Execute(cmd); }
        }
        private string Select(int limit) { return "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT TOP " + limit.ToString(CultureInfo.InvariantCulture) + " [Date_Time],[FracSec],[Event_Type],[Event_SubType],[Category],[Area],[Node],[Unit],[Module],[Module_Description],[Attribute],[State],[Event_Level],[Desc1],[Desc2],[IsArchived],[Ord] FROM " + _table; }
        private IList<EventRecord> Execute(SqlCommand cmd) { List<EventRecord> rows = new List<EventRecord>(); using (SqlDataReader r = cmd.ExecuteReader()) while (r.Read()) { if (r.IsDBNull(0) || r.IsDBNull(1)) throw new InvalidOperationException("Journal contains a null Date_Time or FracSec cursor field."); rows.Add(new EventRecord { DateTimeValue = r.GetDateTime(0), FracSec = Convert.ToInt16(r.GetValue(1), CultureInfo.InvariantCulture), EventType = S(r, 2), EventSubType = S(r, 3), Category = S(r, 4), Area = S(r, 5), Node = S(r, 6), Unit = S(r, 7), Module = S(r, 8), ModuleDescription = S(r, 9), Attribute = S(r, 10), State = S(r, 11), EventLevel = S(r, 12), Desc1 = S(r, 13), Desc2 = S(r, 14), IsArchived = r.IsDBNull(15) ? (short?)null : Convert.ToInt16(r.GetValue(15), CultureInfo.InvariantCulture), Ord = r.GetInt32(16) }); } return rows; }
        private EventSourceInfo ReadSourceInfo(SqlConnection c) { EventSourceInfo i = new EventSourceInfo(); using (SqlCommand cmd = Command(c, "SELECT TOP 1 [SourceNode],[CreateTime],[IsFull] FROM [dbo].[JournalProperties];")) using (SqlDataReader r = cmd.ExecuteReader()) { if (!r.Read()) throw new InvalidOperationException("JournalProperties is empty."); i.SourceNode = r.IsDBNull(0) ? "" : r.GetString(0); if (r.IsDBNull(1)) throw new InvalidOperationException("JournalProperties.CreateTime is null."); i.CreateTime = r.GetDateTime(1); i.IsFull = !r.IsDBNull(2) && Convert.ToInt16(r.GetValue(2), CultureInfo.InvariantCulture) != 0; } i.Generation = i.SourceNode + "|" + i.CreateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture); using (SqlCommand cmd = Command(c, "SELECT COALESCE(SUM(p.rows),0) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE s.name=@schema AND t.name=@table;")) { cmd.Parameters.AddWithValue("@schema", _config.EventsSchema); cmd.Parameters.AddWithValue("@table", _config.EventsTable); i.EstimatedRows = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); } return i; }
        private bool ReadOverflow(SqlConnection c) { string original = c.Database; bool changed = false; try { c.ChangeDatabase("EJOverflow"); changed = true; using (SqlCommand cmd = Command(c, "SELECT TOP 1 1 FROM [dbo].[Journal] WITH (NOLOCK);")) return cmd.ExecuteScalar() != null; } catch (Exception ex) { throw new InvalidOperationException("Unable to verify whether EJOverflow is empty (fail-closed).", ex); } finally { if (changed && !c.Database.Equals(original, StringComparison.OrdinalIgnoreCase)) c.ChangeDatabase(original); } }
        private void EnsureCursorFields(SqlConnection c) { string[] fields = { "Date_Time", "FracSec" }; for (int i = 0; i < fields.Length; i++) using (SqlCommand cmd = Command(c, "IF EXISTS(SELECT TOP 1 1 FROM " + _table + " WITH (NOLOCK) WHERE [" + fields[i] + "] IS NULL) SELECT 1 ELSE SELECT 0;")) if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 0) throw new InvalidOperationException("Journal contains null " + fields[i] + " values; cursor would be unsafe."); }
        private SqlConnection Open() { SqlConnectionStringBuilder b = new SqlConnectionStringBuilder { DataSource = _config.EventsServer, InitialCatalog = _config.EventsDatabase, IntegratedSecurity = true, ConnectTimeout = Math.Min(30, _config.EventsCommandTimeoutSeconds), ApplicationName = "DcsDataService" }; SqlConnection c = new SqlConnection(b.ConnectionString); c.Open(); return c; }
        private SqlCommand Command(SqlConnection c, string sql) { SqlCommand cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = _config.EventsCommandTimeoutSeconds; return cmd; }
        private static void AddCursor(SqlCommand cmd, EventCursor c) { cmd.Parameters.Add("@cursorDateTime", SqlDbType.DateTime).Value = c.DateTimeValue; cmd.Parameters.Add("@cursorFracSec", SqlDbType.SmallInt).Value = c.FracSec; cmd.Parameters.Add("@cursorOrd", SqlDbType.Int).Value = c.Ord; }
        private static EventCursor Cursor(SqlDataReader r, int s) { return new EventCursor { DateTimeValue = r.GetDateTime(s), FracSec = Convert.ToInt16(r.GetValue(s + 1), CultureInfo.InvariantCulture), Ord = r.GetInt32(s + 2) }; }
        private static string S(SqlDataReader r, int n) { return r.IsDBNull(n) ? null : Convert.ToString(r.GetValue(n), CultureInfo.InvariantCulture); }
        private void ValidateLimit(int n) { if (n < 1 || n > _config.MaxEventRows) throw new ArgumentOutOfRangeException("limit"); }
        private static string Quote(string s) { return "[" + s.Replace("]", "]]" ) + "]"; }
    }
}
