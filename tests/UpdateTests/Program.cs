using VRCGroupTools.Services;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;

var tests=new List<(string,Func<Task>)>();
void Add(string n,Action a)=>tests.Add((n,()=>{a();return Task.CompletedTask;}));
void Check(bool yes,string message){if(!yes)throw new Exception(message);}
string Url(string version)=>$"https://github.com/0xE69/VRCGT/releases/download/v{version}/VRCGroupTools-v{version}-win-x64.zip";
UpdateRelease Release(string v,bool draft=false,bool pre=false,bool zip=true)=>new("v"+v,draft,pre,zip?new[]{new UpdateAsset($"VRCGroupTools-v{v}-win-x64.zip",Url(v))}:Array.Empty<UpdateAsset>());
Add("Select highest version independent of GitHub ordering",()=>Check(UpdateCatalog.Select(new[]{Release("1.1.19"),Release("1.1.21"),Release("1.1.20")},"1.1.18")?.Version=="1.1.21","Wrong release ordering"));
Add("Draft prerelease malformed tags and no-ZIP releases skipped",()=>Check(UpdateCatalog.Select(new[]{Release("1.1.99",draft:true),Release("1.1.98",pre:true),Release("1.1.97",zip:false),Release("broken"),Release("1.1.21")},"1.1.18")?.Version=="1.1.21","Unsafe release selected"));
Add("Equal older and invalid current versions do not update",()=>{Check(UpdateCatalog.Select(new[]{Release("1.1.18")},"1.1.18")==null,"Equal update");Check(UpdateCatalog.Select(new[]{Release("1.1.17")},"1.1.18")==null,"Downgrade");Check(UpdateCatalog.Select(new[]{Release("1.1.21")},"bad")==null,"Invalid installed version accepted");});
foreach(var tag in new[]{"v1.1.21-beta","vv1.1.21","1.1","v999999999999999999999.1.1","","v1.1.21+build"})Add("Reject malformed stable tag: "+tag,()=>Check(UpdateCatalog.ParseVersion(tag)==null,"Invalid tag accepted"));
Add("Only exact portable ZIP from updater repository accepted",()=>{var r=new UpdateRelease("v1.1.21",false,false,new[]{new UpdateAsset("setup.zip",Url("1.1.21")),new UpdateAsset("VRCGroupTools-v1.1.21-win-x64.zip","https://evil.invalid/file.zip")});Check(UpdateCatalog.Select(new[]{r},"1.1.18")==null,"Wrong artifact source");});
Add("Updater has injected offline discovery and download boundaries",()=>Check(typeof(UpdateService).GetConstructors().Any(c=>c.GetParameters().Length>=2),"No offline update service seam"));
Add("Safe bounded package staging replaces arbitrary first-executable extraction",()=>Check(typeof(UpdateService).Assembly.GetType("VRCGroupTools.Services.UpdatePackage")!=null,"Safe staging missing"));
tests.AddRange(PackageTests.All());
tests.Add(("Actual v1.1.18 updater selects ZIP, downloads local fixture, stages, copies and relaunches isolated dummy",LegacyTests.UpgradeFrom118));
if(args.Length==1)
{
    var candidate=Path.GetFullPath(args[0]);
    tests.Add(("Actual 1.1.18 updater discovers/downloads/stages the frozen candidate without executing it",()=>LegacyTests.StageCandidate(candidate)));
    tests.Add(("New updater verifies and stages the actual candidate ZIP, sidecar and embedded EXE",async()=>{
        using var fixture=new UpdateFixture();var zip=File.ReadAllBytes(candidate);var sidecar=File.ReadAllBytes(candidate+".sha256");
        using var http=new System.Net.Http.HttpClient(new DownloadHandler(uri=>uri.EndsWith(".sha256")?sidecar:zip));
        var service=new UpdateService(()=>Task.FromResult<IReadOnlyList<UpdateRelease>>(new[]{PackageTests.FixtureRelease(true)}),http,"1.1.18");
        Check(await service.CheckForUpdateAsync(),"Frozen candidate was not discoverable in fixture");var staged=await service.DownloadAndStageAsync(Path.Combine(fixture.Dir,"actual-candidate"));
        using var archive=ZipFile.OpenRead(candidate);using var payload=archive.GetEntry("VRCGroupTools.exe")!.Open();
        Check(UpdatePackage.Hash(staged)==Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()&&FileVersionInfo.GetVersionInfo(staged).FileVersion=="1.1.21.0","Actual candidate staging bytes/version mismatch");
    }));
}
var failed=0;foreach(var(n,a)in tests)try{await a();Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.Message);}Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");return failed==0?0:1;