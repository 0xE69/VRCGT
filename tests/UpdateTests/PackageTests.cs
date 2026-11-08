using VRCGroupTools.Services;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Net.Http;

static class PackageTests
{
    public static IEnumerable<(string,Func<Task>)> All()
    {
        yield return ("Stage exact root executable with both SHA256 checks",()=>{
            using var f=new UpdateFixture();var exe=Encoding.ASCII.GetBytes("MZ fixture executable");var zip=f.Zip(("VRCGroupTools.exe",exe),("SHA256SUMS.txt",Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant()+"  VRCGroupTools.exe\n")));
            var stage=UpdatePackage.Stage(zip,Path.Combine(f.Dir,"stage"),UpdatePackage.Hash(zip));Check(File.ReadAllBytes(stage).SequenceEqual(exe),"Staged bytes changed");return Task.CompletedTask;});
        foreach(var name in new[]{"../escape.txt","..\\escape.txt","/absolute.txt","C:/escape.txt","file:ads","folder/../escape.txt","other.exe","nested/VRCGroupTools.exe"})
            yield return ("Reject unsafe/unexpected ZIP entry: "+name,()=>{using var f=new UpdateFixture();var zip=f.Zip(("VRCGroupTools.exe",new byte[]{77,90,0}),(name,new byte[]{1}));Reject(()=>UpdatePackage.Stage(zip,Path.Combine(f.Dir,"stage")));return Task.CompletedTask;});
        yield return ("Reject missing exact root executable",()=>{using var f=new UpdateFixture();Reject(()=>UpdatePackage.Stage(f.Zip(("readme.txt",new byte[]{1})),Path.Combine(f.Dir,"stage")));return Task.CompletedTask;});
        yield return ("Reject SHA256 mismatches and clean staged executable",()=>{using var f=new UpdateFixture();var zip=f.Zip(("VRCGroupTools.exe",new byte[]{77,90,0}),("SHA256SUMS.txt",Encoding.ASCII.GetBytes(new string('0',64)+"  VRCGroupTools.exe\n")));Reject(()=>UpdatePackage.Stage(zip,Path.Combine(f.Dir,"a"),new string('0',64)));Reject(()=>UpdatePackage.Stage(zip,Path.Combine(f.Dir,"b")));Check(!File.Exists(Path.Combine(f.Dir,"b","VRCGroupTools.exe")),"Bad staging left executable");return Task.CompletedTask;});
        yield return ("Reject duplicate case-insensitive ZIP entries",()=>{using var f=new UpdateFixture();Reject(()=>UpdatePackage.Stage(f.Zip(("VRCGroupTools.exe",new byte[]{77,90,0}),("vrcgrouptools.exe",new byte[]{77,90,0})),Path.Combine(f.Dir,"stage")));return Task.CompletedTask;});
        yield return ("Bounded streaming rejects oversized content without trusting Content-Length",async()=>{using var input=new MemoryStream(new byte[20]);using var output=new MemoryStream();try{await UpdatePackage.CopyBoundedAsync(input,output,10);throw new Exception("Size limit ignored");}catch(InvalidDataException){} });
        yield return ("Repeated update checks clear stale version and download URL on error/no ZIP",async()=>{
            IReadOnlyList<UpdateRelease> releases=new[]{FixtureRelease("1.1.21")};var fail=false;
            using var http=new HttpClient(new DownloadHandler(_=>new byte[0]));var service=new UpdateService(()=>fail?throw new IOException("offline"):Task.FromResult(releases),http,"1.1.18");
            Check(await service.CheckForUpdateAsync(),"Initial candidate missing");fail=true;Check(!await service.CheckForUpdateAsync()&&service.DownloadUrl==null&&service.LatestVersion==null,"Failed check retained stale URL");fail=false;releases=Array.Empty<UpdateRelease>();Check(!await service.CheckForUpdateAsync()&&service.DownloadUrl==null,"Empty check retained candidate");});
        yield return ("Offline discovery-download-stage verifies fixture ZIP and checksum",async()=>{
            using var f=new UpdateFixture();var zip=f.Zip(("VRCGroupTools.exe",new byte[]{77,90,0,1}));var bytes=File.ReadAllBytes(zip);var checksum=Encoding.ASCII.GetBytes(UpdatePackage.Hash(zip)+"  VRCGroupTools-v1.1.21-win-x64.zip\n");
            var handler=new DownloadHandler(uri=>uri.EndsWith(".sha256")?checksum:bytes);using var http=new HttpClient(handler);var release=FixtureRelease("1.1.21", true);var service=new UpdateService(()=>Task.FromResult<IReadOnlyList<UpdateRelease>>(new[]{release}),http,"1.1.18");
            Check(await service.CheckForUpdateAsync(),"No fixture update");var staged=await service.DownloadAndStageAsync(Path.Combine(f.Dir,"download"));Check(File.Exists(staged)&&handler.Calls==2,"Fixture download/stage incorrect");});
        yield return ("Replacement helper updates isolated dummy executable and actually relaunches",async()=>{
            using var f=new UpdateFixture();var source=await f.Probe();var target=Path.Combine(f.Dir,"app space ' & ü.exe");File.WriteAllText(target,"old dummy");var marker=Path.Combine(f.Dir,"relaunch.marker");var log=Path.Combine(f.Dir,"update.log");
            var info=UpdateReplacement.StartInfo(UpdateReplacement.Script(source,target,2147483646,log,UpdatePackage.Hash(source),false));info.Environment["VRCGT_UPDATE_PROBE"]=marker;
            var exit=await Run(info);Check(exit==0,"Helper failed: "+(File.Exists(log)?File.ReadAllText(log):"no log"));
            for(var i=0;i<100&&!File.Exists(marker);i++)await Task.Delay(20);
            Check(File.Exists(marker)&&UpdatePackage.Hash(target)==UpdatePackage.Hash(source),"Replacement/relaunch not real");Check(Directory.GetFiles(f.Dir,"*.update-backup-*").Length==1,"Backup missing");});
        yield return ("Replacement launch failure rolls back and logs visible error",async()=>{
            using var f=new UpdateFixture();var source=Path.Combine(f.Dir,"invalid.exe");File.WriteAllText(source,"MZ not a real PE");var target=Path.Combine(f.Dir,"dummy.exe");File.WriteAllText(target,"old bytes");var log=Path.Combine(f.Dir,"failure.log");
            var exit=await Run(UpdateReplacement.StartInfo(UpdateReplacement.Script(source,target,0,log,UpdatePackage.Hash(source),false)));Check(exit==1&&File.ReadAllText(target)=="old bytes"&&File.ReadAllText(log).Contains("restored"),"Rollback/error visibility missing: "+File.ReadAllText(log));});
        yield return ("Replacement rejects staged tampering before changing target",async()=>{
            using var f=new UpdateFixture();var source=Path.Combine(f.Dir,"invalid.exe");File.WriteAllText(source,"MZ source");var target=Path.Combine(f.Dir,"dummy.exe");File.WriteAllText(target,"old bytes");var log=Path.Combine(f.Dir,"failure.log");var exit=await Run(UpdateReplacement.StartInfo(UpdateReplacement.Script(source,target,0,log,new string('0',64),false)));Check(exit==1&&File.ReadAllText(target)=="old bytes","Tamper changed target");});
    }
    public static UpdateRelease FixtureRelease(string version, bool checksum=false){var name=$"VRCGroupTools-v{version}-win-x64.zip";var url=$"https://github.com/0xE69/VRCGT/releases/download/v{version}/";return new("v"+version,false,false,checksum?new[]{new UpdateAsset(name,url+name),new UpdateAsset(name+".sha256",url+name+".sha256")}:new[]{new UpdateAsset(name,url+name)});}
    public static async Task<int> Run(ProcessStartInfo info){info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;using var p=Process.Start(info)!;var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));var text=await stdout+await stderr;if(p.ExitCode!=0)Console.WriteLine("HELPER "+text);return p.ExitCode;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Unsafe package accepted");}
}
sealed class DownloadHandler(Func<string,byte[]> data):HttpMessageHandler
{
    public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data(r.RequestUri!.AbsoluteUri))});}
}
sealed class UpdateFixture:IDisposable
{
    public string Dir=Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")??Path.GetTempPath(),"vrcgt-updater-"+Guid.NewGuid());
    public UpdateFixture(){Directory.CreateDirectory(Dir);}
    public string Zip(params (string,byte[])[] entries){var path=Path.Combine(Dir,Guid.NewGuid()+".zip");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var(name,bytes)in entries){using var target=zip.CreateEntry(name).Open();target.Write(bytes);}return path;}
    public async Task<string> Probe(){var path=Path.Combine(Dir,"probe.exe");var code="public class Probe { public static void Main() { System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable(\"VRCGT_UPDATE_PROBE\"),\"relaunch verified\"); } }";var script="Add-Type -TypeDefinition '"+code+"' -OutputAssembly '"+path.Replace("'","''")+"' -OutputType ConsoleApplication";var exit=await PackageTests.Run(UpdateReplacement.StartInfo(script));if(exit!=0)throw new Exception("Probe compilation failed");return path;}
    public void Dispose(){for(var i=0;i<30;i++){try{Directory.Delete(Dir,true);return;}catch(IOException){Thread.Sleep(100);}catch(UnauthorizedAccessException){Thread.Sleep(100);}}}
}
namespace Legacy
{
    static class LegacySandbox
    {
        public static string Repository="0xE69/VRCGT",Version="1.1.18",Temp="",Target="";
        public static ProcessStartInfo? Launch;public static bool DidShutdown;
        public static void Start(ProcessStartInfo info)=>Launch=info;
        public static void Shutdown()=>DidShutdown=true;
    }
}
