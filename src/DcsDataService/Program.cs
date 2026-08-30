using System;
using System.Globalization;
using System.IO;
using DcsDataService.Api;
using DcsDataService.Api.Handlers;
using DcsDataService.Configuration;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

namespace DcsDataService
{
    public static class Program
    {
        public const string Version = "1.0.0";
        private static ApiServer _server;
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("DcsDataService " + Version + " (.NET Framework 3.5 x86)"); return 0; }
            string command = args.Length == 0 ? "" : args[0].ToLowerInvariant(); string configPath = FindOption(args, "--config") ?? "config.ini";
            if (command != "probe" && command != "serve") { Usage(); return 2; }
            try
            {
                ServiceConfig config = IniConfigLoader.Load(configPath); ServiceLog log = new ServiceLog(config.LogDirectory);
                HistorianProvider historian = new HistorianProvider(config.HistorianServer, config.HistorianConnectionTimeoutSeconds, log); EventProvider events = new EventProvider(config);
                if (command == "probe") return Probe(config, historian, events, log);
                if (String.IsNullOrEmpty(config.ApiKey) || String.Equals(config.ApiKey, "CHANGE_ME", StringComparison.Ordinal)) throw new ConfigurationException("Api.ApiKey must be changed before serve.");
                HandlerContext context = new HandlerContext { Config = config, Historian = historian, Events = events, Log = log }; _server = new ApiServer(config, new Router(context), log);
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; if (_server != null) _server.Stop(); };
                Console.WriteLine("DcsDataService " + Version + " listening on http://" + config.ApiBind + ":" + config.ApiPort.ToString(CultureInfo.InvariantCulture)); _server.Run(); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FATAL: " + ex.Message); return 1; }
        }
        private static int Probe(ServiceConfig config, HistorianProvider historian, EventProvider events, ServiceLog log)
        {
            Console.WriteLine("DcsDataService Probe\n"); bool ok = true;
            bool dllOk = false; try { Line("Historian DLL", "OK (" + HistorianApiProbe.CheckDll() + ")"); dllOk = true; } catch (Exception ex) { ok = false; Line("Historian DLL", "FAILED: " + ex.Message); log.Error("Historian DLL probe failure", ex); }
            try { if (dllOk) { HistorianProbeResult r = new HistorianApiProbe(historian).Run(config.HistorianTestTag); Line("Historian Server", r.Status.Server); Line("Connection", "OK"); Line("Server State", r.Status.ServerState.ToString(CultureInfo.InvariantCulture)); Console.WriteLine(); Line("Test Tag", r.Tag.Tag); Line("Tag Resolve", r.Tag.Status.ToString()); Line("Tag Handle", r.Tag.Handle.ToString(CultureInfo.InvariantCulture)); Line("Raw sample read", "OK"); Line("Samples", r.Samples.ToString(CultureInfo.InvariantCulture)); } }
            catch (Exception ex) { ok = false; Line("Historian", "FAILED: " + ex.Message); log.Error("Historian probe failure", ex); }
            Console.WriteLine();
            try { EventSourceInfo info = events.Probe(); EventCursor first = events.GetEarliestCursor(); EventCursor last = events.GetLatestCursor(); Line("Event Journal", "OK (" + info.Generation + ")"); Line("Earliest Cursor", first == null ? "(empty)" : first.ToString()); Line("Latest Cursor", last == null ? "(empty)" : last.ToString()); }
            catch (Exception ex) { ok = false; Line("Event Journal", "FAILED: " + ex.Message); log.Error("Event probe failure", ex); }
            Console.WriteLine(); Console.WriteLine(ok ? "PROBE PASSED" : "PROBE FAILED"); return ok ? 0 : 10;
        }
        private static void Line(string name, string value) { Console.WriteLine(name.PadRight(20) + ": " + value); }
        private static string FindOption(string[] args, string name) { for (int i = 1; i < args.Length; i++) if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) { if (i + 1 >= args.Length) throw new ArgumentException(name + " requires a value."); return args[i + 1]; } return null; }
        private static void Usage() { Console.WriteLine("Usage:\n  DcsDataService.exe --version\n  DcsDataService.exe probe [--config config.ini]\n  DcsDataService.exe serve [--config config.ini]"); }
    }
}
