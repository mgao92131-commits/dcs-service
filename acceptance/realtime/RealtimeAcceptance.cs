using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class RealtimeAcceptance
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8388608, RecursionLimit = 32 };
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
    private static readonly string StateFile = Path.Combine(Root, "test-service.json");
    private static readonly List<object> Checks = new List<object>();
    private static bool Cancelled;
    private static string RunDirectory, BaseUrl;
    private static Process Server;
    private static Dictionary<string, object> Summary = new Dictionary<string, object>();
    private static string Serialize(object value) { lock(Json)return Json.Serialize(value); }
    private static object Deserialize(string value) { lock(Json)return Json.DeserializeObject(value); }
    private static int Number(object value) { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
    private static Dictionary<string,object> Object(object value) { return (Dictionary<string,object>)value; }
    private static void Save(string filename, string text) { File.WriteAllText(Path.Combine(RunDirectory, filename), text, new UTF8Encoding(true)); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, bool condition, string message)
    {
        Checks.Add(new Dictionary<string,object> { {"name",name}, {"ok",condition}, {"message",message} });
        Console.WriteLine((condition ? "PASS " : "FAIL ") + name + " : " + message);
        Require(condition, message);
    }
    private sealed class Reply { internal int Status; internal Dictionary<string,object> Data; }
    private static Reply Request(string label, string method, string path, object body)
    {
        if (Cancelled) throw new OperationCanceledException("Test cancelled.");
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(BaseUrl + path);
        request.Method = method; request.Timeout = 45000; request.ReadWriteTimeout = 45000; request.KeepAlive = false;
        if (body != null)
        {
            byte[] data = Encoding.UTF8.GetBytes(Serialize(body)); request.ContentType = "application/json; charset=utf-8"; request.ContentLength = data.Length;
            using(Stream stream = request.GetRequestStream())stream.Write(data,0,data.Length);
        }
        HttpWebResponse response;
        try { response = (HttpWebResponse)request.GetResponse(); }
        catch(WebException ex) { response = ex.Response as HttpWebResponse; if(response==null)throw; }
        using(response)
        using(StreamReader reader = new StreamReader(response.GetResponseStream(),Encoding.UTF8))
        {
            string text = reader.ReadToEnd(); Save(label + ".json",text);
            int status = (int)response.StatusCode; Save(label + ".http.txt",method+" "+path+"\r\nHTTP "+status+"\r\n"+response.Headers);
            return new Reply { Status=status,Data=Object(Deserialize(text)) };
        }
    }
    private static void VerifyNativeModules()
    {
        string nativeBin=@"C:\DeltaV\bin";
        Server.Refresh();
        foreach(string name in new string[]{"bss.dll","Sec.dll","oss.dll","RtStringTable.dll","roc.dll"})
        {
            bool found=false;
            foreach(ProcessModule module in Server.Modules)
                if(String.Equals(module.ModuleName,name,StringComparison.OrdinalIgnoreCase))
                {
                    Require(String.Equals(Path.GetFullPath(module.FileName),Path.Combine(nativeBin,name),StringComparison.OrdinalIgnoreCase),"Native module loaded from an unexpected directory: "+module.FileName);
                    found=true;
                }
            Require(found,"Native module is not loaded in the main service: "+name);
        }
        Check("native_modules_in_service",true,"All five native DLLs are loaded in the main service from C:\\DeltaV\\bin.");
    }
    private static string TagPath(string tag) { return "/api/v1/realtime?tag=" + Uri.EscapeDataString(tag); }
    private static void StartService(string nativeBin, bool disabled)
    {
        string exe = Path.Combine(Root,"bin/DcsDataService.exe");
        Require(File.Exists(exe),"Service EXE is missing.");
        if (!disabled) Require(Directory.Exists(nativeBin),"DeltaV bin directory is missing: "+nativeBin);
        TcpListener reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();int port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
        BaseUrl="http://127.0.0.1:"+port;Summary["baseUrl"]=BaseUrl;
        string config=Path.Combine(RunDirectory,"test-config.ini");
        string siteConfig=Path.Combine(Root,"site-config.ini");
        string sitePrefix=File.Exists(siteConfig)?File.ReadAllText(siteConfig)+"\r\n":"";
        File.WriteAllText(config,sitePrefix+"[Api]\r\nPort="+port+"\r\n[Realtime]\r\nEnabled="+(!disabled).ToString().ToLowerInvariant()+"\r\nMaxBatchTags=16\r\nSlotWaitSeconds=5\r\n[Files]\r\nLogs="+Path.Combine(RunDirectory,"service-logs")+"\r\n",new UTF8Encoding(false));
        ProcessStartInfo start=new ProcessStartInfo(exe,"serve --config \""+config+"\"");start.UseShellExecute=false;start.CreateNoWindow=true;start.WorkingDirectory=Root;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        Server=Process.Start(start);
        Dictionary<string,object> state=new Dictionary<string,object>{{"pid",Server.Id},{"startTimeUtcTicks",Server.StartTime.ToUniversalTime().Ticks},{"exe",exe}};
        File.WriteAllText(StateFile,Serialize(state),new UTF8Encoding(false));
        for(int i=0;i<100;i++)
        {
            if(Server.HasExited)throw new InvalidOperationException("Test service exited before startup.");
            try { HttpWebRequest probe=(HttpWebRequest)WebRequest.Create(BaseUrl+"/health"); probe.Timeout=1000; using(HttpWebResponse response=(HttpWebResponse)probe.GetResponse()){ if((int)response.StatusCode==200)return; } } catch(WebException){Thread.Sleep(100);}
        }
        throw new InvalidOperationException("Test service did not listen in time.");
    }
    private static void StopOwnedService()
    {
        if(Server==null)return;
        if(Server!=null)
        {
            if(!Server.HasExited){Server.Kill();Require(Server.WaitForExit(5000),"Test service did not stop.");}
            Save("service-stdout.log",Server.StandardOutput.ReadToEnd());Save("service-stderr.log",Server.StandardError.ReadToEnd());
            Server.Dispose();Server=null;
        }
        if(File.Exists(StateFile))File.Delete(StateFile);
    }
    private static void StopRecordedService()
    {
        if(!File.Exists(StateFile)){Console.WriteLine("No recorded test service.");return;}
        Dictionary<string,object> state=Object(Deserialize(File.ReadAllText(StateFile)));
        try
        {
            using(Process process=Process.GetProcessById(Number(state["pid"])))
            {
                Require(process.StartTime.ToUniversalTime().Ticks==Convert.ToInt64(state["startTimeUtcTicks"],CultureInfo.InvariantCulture),"PID identity mismatch; no process was stopped.");
                string expected=Path.Combine(Root,"bin/DcsDataService.exe");
                Require(String.Equals(Path.GetFullPath(process.MainModule.FileName),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase),"EXE identity mismatch; no process was stopped.");
                process.Kill();Require(process.WaitForExit(5000),"Test service did not stop.");
            }
        }
        catch(ArgumentException){Console.WriteLine("Recorded test service has already exited.");}
        File.Delete(StateFile);Console.WriteLine("Recorded test service stopped.");
    }
    private static void ValidateValue(Dictionary<string,object> item,string tag)
    {
        Require(item.ContainsKey("ok") && (bool)item["ok"] && (string)item["tag"]==tag,"Parameter read did not succeed: "+Serialize(item));
        Require(item["value"]!=null && item["nativeDataType"]!=null,"Missing value/type.");
        Require(item["sourceTimestamp"]==null && item["quality"]==null,"Unsupported source metadata must remain null.");
        DateTime parsed;Require(DateTime.TryParseExact((string)item["observedAt"],"yyyy-MM-ddTHH:mm:ss.fff",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed),"Invalid observation time.");
    }
    public static int Main(string[] args)
    {
        Console.OutputEncoding=new UTF8Encoding(false); Console.InputEncoding=new UTF8Encoding(false);
        Console.CancelKeyPress+=delegate(object sender,ConsoleCancelEventArgs e){Cancelled=true;e.Cancel=true;Console.WriteLine("Stopping after the current bounded request...");};
        if(args.Length==1 && args[0]=="stop"){try{StopRecordedService();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
        string mode=args.Length==0?"quick":args[0];
        if(mode=="offline")return Offline();
        if(mode!="quick" && mode!="stress" && mode!="disabled" && mode!="regression"){Console.Error.WriteLine("Usage: DcsRealtimeAcceptance.exe quick | stress | disabled | stop");return 2;}
        Directory.CreateDirectory(Path.Combine(Root,"TestResults"));
        RunDirectory=Path.Combine(Path.Combine(Root,"TestResults"),DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture));Directory.CreateDirectory(RunDirectory);
        string nativeBin=@"C:\DeltaV\bin";
        string tag="SY-218/AI1/PV.CV",expected="",unhistorized="",extra="";double tolerance=0.5;
        int exit=1; FileStream testLock=null;
        Summary["mode"]=mode;Summary["startedAtLocal"]=DateTime.Now.ToString("s");Summary["checks"]=Checks;Summary["nativeBin"]=nativeBin;
        try
        {
            testLock=new FileStream(Path.Combine(Root,"test-run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            Require(!File.Exists(StateFile),"A previous test service is recorded. Run STOP-TEST-SERVICE.cmd first.");
            Console.WriteLine("DeltaV bin: "+nativeBin);Console.WriteLine("Results: "+RunDirectory);
            if(mode!="disabled" && mode!="regression")
            {
                Console.Write("完整参数路径 [SY-218/AI1/PV.CV]：");string entered=Console.ReadLine();if(!String.IsNullOrEmpty(entered))tag=entered.Trim();
                Console.Write("同期画面参考值（回车跳过比较，默认数值容差 0.5）：");expected=Console.ReadLine()??"";
                if(expected!="")
                {
                    Console.Write("数值比较绝对容差 [0.5]：");string toleranceInput=(Console.ReadLine()??"").Trim();
                    if(toleranceInput!="")Require(Double.TryParse(toleranceInput,NumberStyles.Float,CultureInfo.InvariantCulture,out tolerance) && !Double.IsNaN(tolerance) && !Double.IsInfinity(tolerance) && tolerance>=0,"Tolerance must be a finite nonnegative number.");
                }
                if(mode=="quick")
                {
                    Console.Write("已确认未入历史库的参数路径（可回车跳过）：");unhistorized=(Console.ReadLine()??"").Trim();
                    Console.Write("其他类型参数路径，用分号分隔（可回车跳过）：");extra=(Console.ReadLine()??"").Trim();
                }
            }
            Summary["tag"]=tag;Summary["screenReference"]=expected;Summary["comparisonTolerance"]=tolerance;Summary["screenComparison"]="not_performed";
            StartService(nativeBin,mode=="disabled");
            Check("health",Request("01-health","GET","/health",null).Status==200,"Test service is listening.");
            Reply info=Request("02-info","GET","/api/v1/info",null);Check("info",info.Status==200 && (bool)info.Data["realtimeEnabled"]==(mode!="disabled"),"Realtime configuration is reported correctly.");
            if(mode=="disabled")
            {
                Reply disabled=Request("03-disabled","GET",TagPath(tag),null);Check("disabled",disabled.Status==503 && (string)Object(disabled.Data["error"])["code"]=="realtime_disabled","Disabled feature returns a realtime-specific error.");
            }
            else if(mode=="regression")
            {
                Regression((string)info.Data["sourceTimeZone"]);
            }
            else
            {
                Reply single=Request("03-single","GET",TagPath(tag),null);Require(single.Status==200,"Single request failed; see 03-single.json and service-logs.");ValidateValue(single.Data,tag);
                VerifyNativeModules();
                Check("single",true,"value="+Convert.ToString(single.Data["value"],CultureInfo.InvariantCulture)+"; valueText="+single.Data["valueText"]+"; nativeType="+single.Data["nativeTypeName"]);
                Summary["singleValue"]=single.Data;
                if(expected!="")
                {
                    double reference,actual;bool match;
                    if(Double.TryParse(expected,NumberStyles.Float,CultureInfo.InvariantCulture,out reference) && Double.TryParse(Convert.ToString(single.Data["value"],CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out actual))match=!Double.IsNaN(actual)&&!Double.IsInfinity(actual)&&!Double.IsNaN(reference)&&!Double.IsInfinity(reference)&&Math.Abs(reference-actual)<=tolerance;
                    else match=String.Equals(expected,Convert.ToString(single.Data["value"],CultureInfo.InvariantCulture),StringComparison.Ordinal);
                    Summary["screenComparison"]=match?"matched":"mismatched";Check("screen_reference",match,"Reference comparison, numeric tolerance="+tolerance.ToString(CultureInfo.InvariantCulture));
                }
                string bad=tag.Substring(0,tag.LastIndexOf('.')+1)+"__DCS_SERVICE_INVALID_FIELD__";
                Reply batch=Request("04-mixed-batch","POST","/api/v1/realtime/batch",new { tags=new string[]{tag,bad,tag} });
                Require(batch.Status==200,"Batch request failed.");object[] items=(object[])batch.Data["items"];
                Check("mixed_batch",(bool)batch.Data["complete"] && !(bool)batch.Data["ok"] && Number(batch.Data["successCount"])==2 && Number(batch.Data["failureCount"])==1 && items.Length==3,"Two successful entries plus one invalid field.");
                ValidateValue(Object(items[0]),tag);ValidateValue(Object(items[2]),tag);Check("batch_order",(string)Object(items[1])["tag"]==bad && !(bool)Object(items[1])["ok"],"Input order and duplicates preserved.");
                Reply failed=Request("05-all-invalid","POST","/api/v1/realtime/batch",new { tags=new string[]{bad,bad} });Check("all_failed_batch",failed.Status==200 && (bool)failed.Data["complete"] && Number(failed.Data["successCount"])==0 && Number(failed.Data["failureCount"])==2,"Complete batch distinguishes point failures from request failure.");
                string[] tooMany=new string[17];for(int i=0;i<tooMany.Length;i++)tooMany[i]=tag;
                Check("batch_limit",Request("06-batch-limit","POST","/api/v1/realtime/batch",new { tags=tooMany }).Status==400,"Configured batch limit enforced.");
                Check("validation",Request("07-invalid-input","POST","/api/v1/realtime/batch",new { tags="not-an-array" }).Status==400,"Non-array input rejected.");
                if(unhistorized!="")
                {
                    Reply live=Request("08-unhistorized","GET",TagPath(unhistorized),null);Require(live.Status==200,"User-declared unhistorized point failed.");ValidateValue(live.Data,unhistorized);Summary["userDeclaredUnhistorizedPoint"]=unhistorized;Check("unhistorized",true,"User-declared unhistorized parameter returned a value.");
                }
                else Summary["unhistorizedVerification"]="skipped_no_parameter_supplied";
                if(extra!="")
                {
                    int index=0;foreach(string candidate in extra.Split(';'))if(candidate.Trim()!=""){string path=candidate.Trim();Reply typed=Request("09-type-"+(++index),"GET",TagPath(path),null);Require(typed.Status==200,"Additional parameter failed.");ValidateValue(typed.Data,path);Check("additional_type_"+index,true,"type="+typed.Data["nativeTypeName"]+" value="+typed.Data["value"]);}
                }
                if(mode=="stress")Stress(tag);
                Check("health_after_reads",Request("10-health-after","GET","/health",null).Status==200,"Main service remains available.");
            }
            Summary["ok"]=true;exit=0;
        }
        catch(Exception ex){Summary["ok"]=false;Summary["error"]=ex.ToString();Console.Error.WriteLine("TEST FAILED: "+ex.Message);}
        finally
        {
            try { StopOwnedService(); } catch(Exception ex){Summary["cleanupError"]=ex.ToString();Summary["ok"]=false;exit=1;}
            if(testLock!=null)testLock.Dispose();
            Summary["finishedAtLocal"]=DateTime.Now.ToString("s");Save("summary.json",Serialize(Summary));Console.WriteLine("Results saved: "+RunDirectory);
        }
        Console.WriteLine(exit==0?"REALTIME API FIELD TEST PASSED":"REALTIME API FIELD TEST FAILED");return exit;
    }
    private static int Offline()
    {
        Directory.CreateDirectory(Path.Combine(Root,"TestResults"));
        RunDirectory=Path.Combine(Path.Combine(Root,"TestResults"),DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture));Directory.CreateDirectory(RunDirectory);
        List<object> results=new List<object>();bool ok=true;
        try
        {
            using(FileStream testLock=new FileStream(Path.Combine(Root,"test-run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                foreach(string file in new string[]{"DcsDataService.CoreTests.exe","DcsDataService.RealtimeTests.exe"})
                {
                    ProcessStartInfo info=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,file));info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
                    using(Process child=Process.Start(info))
                    {
                        if(!child.WaitForExit(60000)){child.Kill();child.WaitForExit(3000);throw new InvalidOperationException("Offline test timed out: "+file);}
                        string output=child.StandardOutput.ReadToEnd(),error=child.StandardError.ReadToEnd();Save(file+".stdout.log",output);Save(file+".stderr.log",error);
                        results.Add(new { test=file,exitCode=child.ExitCode });Console.WriteLine(output);if(error!="")Console.Error.WriteLine(error);
                        if(child.ExitCode!=0){ok=false;break;}
                    }
                }
            }
        }
        catch(Exception ex){ok=false;Save("error.txt",ex.ToString());Console.Error.WriteLine(ex.Message);}
        Save("summary.json",Serialize(new { mode="offline_in_process_runtime",ok=ok,nativeDcsReads=false,tests=results }));Console.WriteLine("Results saved: "+RunDirectory);return ok?0:1;
    }
    private static void Regression(string zone)
    {
        Console.Write("历史库中确认存在的完整 TAG（必填）：");string tag=(Console.ReadLine()??"").Trim();Require(tag!="","A known historical tag is required.");
        Reply diagnostic=Request("03-history-tag","GET","/api/v1/tag?tag="+Uri.EscapeDataString(tag),null);
        Check("history_tag",diagnostic.Status==200 && (string)diagnostic.Data["status"]=="HistoryTagOK","Historian tag diagnostic succeeded.");
        DateTime to=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(zone));DateTime from=to.AddMinutes(-2);
        string range="from="+Uri.EscapeDataString(from.ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture))+"&to="+Uri.EscapeDataString(to.ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture));
        Csv("04-history", "/api/v1/history?tag="+Uri.EscapeDataString(tag)+"&"+range,"Timestamp,Value,");
        Csv("05-events", "/api/v1/events?"+range,"DateTime,FracSec,Ord,");
        Check("health_after_regression",Request("06-health-after","GET","/health",null).Status==200,"Service remains available after history/event requests.");
    }
    private static void Csv(string label,string path,string header)
    {
        HttpWebRequest request=(HttpWebRequest)WebRequest.Create(BaseUrl+path);request.Timeout=120000;request.ReadWriteTimeout=120000;request.KeepAlive=false;
        string part=Path.Combine(RunDirectory,label+".csv.part"),final=Path.Combine(RunDirectory,label+".csv");
        using(HttpWebResponse response=(HttpWebResponse)request.GetResponse())
        {
            Require((int)response.StatusCode==200 && response.ContentType.StartsWith("text/csv",StringComparison.OrdinalIgnoreCase),"CSV response was not successful.");
            Save(label+".http.txt","GET "+path+"\r\n"+response.Headers);
            using(Stream source=response.GetResponseStream())using(FileStream output=File.Create(part))
            {
                byte[] buffer=new byte[8192];int n;long count=0;
                while((n=source.Read(buffer,0,buffer.Length))>0){count+=n;Require(count<=16777216,"CSV exceeds this test's 16 MiB bound; use a shorter/manual range.");output.Write(buffer,0,n);}
            }
        }
        using(StreamReader check=new StreamReader(part,Encoding.UTF8)){string line=check.ReadLine();Require(line!=null && line.StartsWith(header,StringComparison.Ordinal),"Unexpected CSV header.");}
        File.Move(part,final);Check(label,true,"Complete bounded CSV saved: "+final);
    }
    private static void Stress(string tag)
    {
        List<object> samples=new List<object>();
        for(int i=0;i<30;i++)
        {
            Reply reply=Request("stress-"+i.ToString("00"),"GET",TagPath(tag),null);Require(reply.Status==200,"Repeated read failed.");ValidateValue(reply.Data,tag);
            Server.Refresh();
            samples.Add(new { iteration=i+1, workingSet=Server.WorkingSet64, privateBytes=Server.PrivateMemorySize64, handles=Server.HandleCount });
        }
        Save("stress-metrics.json",Serialize(samples));Summary["realtimeExecution"]="in_process";
        // Concurrent callers exercise queueing; every accepted read must remain valid.
        Exception failure=null;int accepted=0,rejected=0;Thread[] callers=new Thread[3];object gate=new object();
        for(int i=0;i<callers.Length;i++)
        {
            int index=i;callers[i]=new Thread(delegate(){try{Reply r=Request("parallel-"+index,"GET",TagPath(tag),null);if(r.Status==503 && r.Data.ContainsKey("error") && (string)Object(r.Data["error"])["code"]=="service_busy")Interlocked.Increment(ref rejected);else{Require(r.Status==200,"Concurrent request failed: "+r.Status);ValidateValue(r.Data,tag);Interlocked.Increment(ref accepted);}}catch(Exception ex){lock(gate)failure=ex;}});callers[i].Start();
        }
        foreach(Thread thread in callers)Require(thread.Join(50000),"Concurrent caller did not finish.");
        Require(failure==null && accepted>0 && accepted+rejected==3,"Concurrent read failure: "+failure);
        Summary["parallelAccepted"]=accepted;Summary["parallelBusy"]=rejected;
        Check("stress",true,"30 sequential reads plus 3 concurrent callers completed; controlled service_busy rejections are counted. Inspect stress-metrics.json for memory/handle trends.");
        Summary["stabilityReview"]="metrics_collected_manual_review_required";
    }
}
