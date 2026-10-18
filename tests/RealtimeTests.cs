using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DcsDataService.Api;
using DcsDataService.Api.Handlers;
using DcsDataService.Configuration;
using DcsDataService.DeltaV.Realtime;
using DcsDataService.Util;
using DcsRealtime;

internal static class RealtimeTests
{
    private static int _count;
    private static void Check(bool value, string message) { _count++; if (!value) throw new Exception(message); }
    private sealed class FakeRuntime : IRealtimeRuntime
    {
        internal int Initializations, OwnerThread, Active, Maximum;
        internal bool FailInitialize;
        internal readonly ManualResetEvent Entered = new ManualResetEvent(false), Release = new ManualResetEvent(false);
        public void Initialize()
        {
            Initializations++; OwnerThread = Thread.CurrentThread.ManagedThreadId;
            if (FailInitialize) throw new FileNotFoundException("test runtime missing");
        }
        public ReadItem Read(string tag)
        {
            Check(Thread.CurrentThread.ManagedThreadId == OwnerThread, "All native calls must use their initialization thread.");
            int active = Interlocked.Increment(ref Active); Maximum = Math.Max(Maximum, active);
            try
            {
                if (tag.Contains("/WAIT/")) { Entered.Set(); Release.WaitOne(); }
                if (tag.Contains("/THROW/")) throw new InvalidOperationException("test read failure");
                if (tag.Contains("/BAD/")) return new ReadItem { tag=tag, ok=false, sourceTimeZone="China Standard Time", error=new ReadError { code="parameter_not_found", nativeStatus=32 } };
                object value = tag.Contains("/UTF8/") ? (object)"中文返回" : tag.Contains("/SMALL/") ? (object)1e-100 : 512.6943359375;
                return new ReadItem { tag=tag, ok=true, value=value, valueText=Convert.ToString(value,CultureInfo.InvariantCulture), valueType=value is string?"text":"number", nativeDataType=8, nativeTypeName="FLOAT32", observedAt="2026-10-17T16:00:00.000", sourceTimeZone="China Standard Time" };
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    private static ServiceConfig Config() { return new ServiceConfig { RealtimeMaxBatchTags=3, RealtimeSlotWaitSeconds=1 }; }
    private static void Failure(RealtimeProvider reader, string tag, string code)
    {
        try { reader.Read(new string[]{tag}); throw new Exception("Expected failure."); }
        catch(RealtimeException ex) { Check(ex.HttpStatus==503 && ex.Error.code==code,"Wrong error: "+ex.Error.code); }
    }
    public static int Main()
    {
        try
        {
            Check(new ServiceConfig().RealtimeEnabled,"Realtime must work without a Realtime config section.");
            FakeRuntime runtime=new FakeRuntime(); ServiceConfig config=Config();
            using(RealtimeProvider reader=new RealtimeProvider(config,null,runtime))
            {
                Check(runtime.Initializations==0,"Health/startup must not eagerly invoke native code.");
                ReadBatch batch=reader.Read(new string[]{"TEST/AI1/PV.CV","TEST/BAD/PV.CV","TEST/AI1/PV.CV"});
                Check(batch.complete && !batch.ok && batch.successCount==2 && batch.failureCount==1,"Partial batch counts.");
                Check(batch.items[0].tag==batch.items[2].tag && batch.items[1].error.nativeStatus==32,"Order, duplicates, native error.");
                Check((double)batch.items[0].value==512.6943359375,"FLOAT32 precision.");
                Check((double)reader.Read(new string[]{"TEST/SMALL/PV.CV"}).items[0].value==1e-100,"FLOAT64 precision.");
                Check((string)reader.Read(new string[]{"TEST/UTF8/PV.CV"}).items[0].value=="中文返回","Unicode values.");
                Check(batch.items[0].sourceTimestamp==null && batch.items[0].quality==null,"No fabricated metadata.");
                Check(reader.Read(new string[]{"TEST/BAD/PV.CV"}).failureCount==1,"All-point failure completes the batch.");
                Failure(reader,"TEST/THROW/PV.CV","realtime_read_failed");
                Check(reader.Read(new string[]{"TEST/AI1/PV.CV"}).ok,"Reader recovers after a managed read failure.");
                Check(runtime.Initializations==1,"Runtime initialized once across requests.");
                Thread[] calls=new Thread[5];Exception failure=null;
                for(int i=0;i<calls.Length;i++){calls[i]=new Thread(delegate(){try{reader.Read(new string[]{"TEST/AI1/PV.CV"});}catch(Exception ex){failure=ex;}});calls[i].Start();}
                foreach(Thread t in calls)Check(t.Join(3000),"Caller completes.");
                Check(failure==null && runtime.Maximum==1,"Concurrent callers serialize native access.");
                Thread blocked=new Thread(delegate(){try{reader.Read(new string[]{"TEST/WAIT/PV.CV"});}catch(Exception ex){failure=ex;}});blocked.Start();
                Check(runtime.Entered.WaitOne(2000,false),"Blocking native call entered.");
                Failure(reader,"TEST/AI1/PV.CV","service_busy");
                Check(runtime.Active==1,"Queue timeout must not abort or replace the native call.");
                runtime.Release.Set();Check(blocked.Join(2000) && failure==null,"Native call finishes normally.");
                Router router=new Router(new HandlerContext{Config=config,Realtime=reader});
                HttpRequest post=new HttpRequest{Method="POST",Path="/api/v1/realtime/batch",Body="{\"tags\":[\"TEST/AI1/PV.CV\",\"TEST/BAD/PV.CV\"]}"};post.Headers["Content-Type"]="application/json";
                Check(router.RouteRequest(post).Body.Contains("\"failureCount\":1"),"Batch API.");
                Check(router.RouteRequest(new HttpRequest{Method="GET",Path="/api/v1/realtime",QueryString="tag=TEST%2FBAD%2FPV.CV"}).StatusCode==404,"Single missing point API.");
                foreach(string invalid in new string[]{"{}","{\"tags\":[]}","{\"tags\":[1]}","{\"tags\":\"A\"}","{\"tags\":[\"MODULE\"]}","{\"tags\":[\"TEST/AI1/PV.CV\"],\"extra\":1}"})
                {post.Body=invalid;try{router.RouteRequest(post);throw new Exception("Invalid request accepted.");}catch(ArgumentException){_count++;}}
                HttpIntegration(config,reader);
                reader.Dispose();reader.Dispose();Failure(reader,"TEST/AI1/PV.CV","realtime_unavailable");
            }
            FakeRuntime unavailable=new FakeRuntime{FailInitialize=true};
            using(RealtimeProvider reader=new RealtimeProvider(Config(),null,unavailable))
            {Failure(reader,"TEST/AI1/PV.CV","realtime_unavailable");Failure(reader,"TEST/AI1/PV.CV","realtime_unavailable");Check(unavailable.Initializations==1,"A partially initialized runtime must not repeatedly load DLLs.");}
            ServiceConfig disabled=Config();disabled.RealtimeEnabled=false;FakeRuntime untouched=new FakeRuntime();
            using(RealtimeProvider reader=new RealtimeProvider(disabled,null,untouched)){Failure(reader,"TEST/AI1/PV.CV","realtime_disabled");Check(untouched.Initializations==0,"Disabled service does not load native runtime.");}
            FakeRuntime shutdown=new FakeRuntime();RealtimeProvider stopping=new RealtimeProvider(Config(),null,shutdown);Exception shutdownError=null;
            Thread current=new Thread(delegate(){try{stopping.Read(new string[]{"TEST/WAIT/PV.CV"});}catch(Exception ex){shutdownError=ex;}});current.Start();
            Check(shutdown.Entered.WaitOne(2000,false),"Shutdown call entered.");stopping.Dispose();Failure(stopping,"TEST/AI1/PV.CV","realtime_unavailable");shutdown.Release.Set();Check(current.Join(2000) && shutdownError==null,"Shutdown allows an active call to clean up.");
            Check(typeof(ServiceConfig).GetField("RealtimeWorkerTimeoutSeconds")==null,"No obsolete worker timeout setting.");
            Console.WriteLine("REALTIME TESTS PASSED ("+_count+")");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("REALTIME TEST FAILED: "+ex);return 1;}
    }
    private static void HttpIntegration(ServiceConfig config,RealtimeProvider reader)
    {
        TcpListener reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();config.ApiPort=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
        ServiceLog log=new ServiceLog(Path.Combine(Path.GetTempPath(),"dcs-inprocess-tests-"+Guid.NewGuid().ToString("N")));
        ApiServer server=new ApiServer(config,new Router(new HandlerContext{Config=config,Realtime=reader}),log);
        Thread thread=new Thread(server.Run);thread.IsBackground=true;thread.Start();
        try
        {
            bool ready=false;for(int i=0;i<40;i++){try{using(TcpClient c=new TcpClient("127.0.0.1",config.ApiPort)){ready=true;break;}}catch(SocketException){Thread.Sleep(50);}}
            Check(ready,"HTTP server started.");
            Check(Request(config.ApiPort,"GET","/api/v1/realtime?tag=TEST%2FAI1%2FPV.CV",null).Contains("512.6943359375"),"Actual HTTP single value.");
            Check(Request(config.ApiPort,"POST","/api/v1/realtime/batch","{\"tags\":[\"TEST/AI1/PV.CV\",\"TEST/BAD/PV.CV\"]}").Contains("\"failureCount\":1"),"Actual HTTP mixed batch.");
            Check(Request(config.ApiPort,"GET","/api/v1/realtime?tag=TEST%2FTHROW%2FPV.CV",null).StartsWith("HTTP/1.1 503"),"Actual HTTP managed failure.");
            Check(Request(config.ApiPort,"GET","/health",null).StartsWith("HTTP/1.1 200"),"Health remains available.");
            Check(Request(config.ApiPort,"GET","/api/v1/info",null).Contains("in_process"),"Info reports integrated runtime.");
        }
        finally{server.Stop();Check(thread.Join(3000),"HTTP server stops.");}
    }
    private static string Request(int port,string method,string path,string body)
    {
        using(TcpClient client=new TcpClient("127.0.0.1",port))
        {
            NetworkStream stream=client.GetStream();stream.ReadTimeout=5000;byte[] data=Encoding.UTF8.GetBytes(body??"");
            byte[] header=Encoding.ASCII.GetBytes(method+" "+path+" HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: "+data.Length+"\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(data,0,data.Length);
            using(StreamReader input=new StreamReader(stream,Encoding.UTF8))return input.ReadToEnd();
        }
    }
}
