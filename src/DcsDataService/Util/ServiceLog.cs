using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DcsDataService.Util
{
    public sealed class ServiceLog
    {
        private readonly object _gate = new object(); private readonly string _directory;
        public ServiceLog(string directory) { _directory = String.IsNullOrEmpty(directory) ? "logs" : directory; }
        public void Info(string message) { Write("INFO", message, null); }
        public void Error(string message, Exception ex) { Write("ERROR", message, ex); }
        private void Write(string level, string message, Exception ex)
        {
            lock (_gate)
            {
                if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);
                string path = Path.Combine(_directory, "service_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                using (StreamWriter w = new StreamWriter(path, true, Encoding.UTF8)) { w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + level + " " + message); if (ex != null) w.WriteLine(ex.ToString()); }
            }
        }
    }
}
