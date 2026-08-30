using System;
using System.Globalization;
using System.IO;

namespace DcsDataService.Util
{
    public sealed class CsvWriter
    {
        private readonly TextWriter _writer;
        public CsvWriter(TextWriter writer) { if (writer == null) throw new ArgumentNullException("writer"); _writer = writer; }

        public void WriteRow(params object[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0) _writer.Write(',');
                WriteField(Format(values[i]));
            }
            _writer.Write("\r\n");
        }

        public static string Format(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            DateTime? nullableDate = value as DateTime?;
            if (nullableDate.HasValue) return nullableDate.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            DateTime date = value is DateTime ? (DateTime)value : DateTime.MinValue;
            if (value is DateTime) return date.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            bool? nullableBool = value as bool?;
            if (nullableBool.HasValue) return nullableBool.Value ? "true" : "false";
            if (value is bool) return (bool)value ? "true" : "false";
            IFormattable formattable = value as IFormattable;
            return formattable == null ? value.ToString() : formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        private void WriteField(string value)
        {
            bool quote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;
            if (!quote) { _writer.Write(value); return; }
            _writer.Write('"');
            for (int i = 0; i < value.Length; i++) { if (value[i] == '"') _writer.Write("\"\""); else _writer.Write(value[i]); }
            _writer.Write('"');
        }
    }
}
