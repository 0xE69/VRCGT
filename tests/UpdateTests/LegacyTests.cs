using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Octokit;

static class LegacyTests
{
    public static Task UpgradeFrom118() => Run(null, "1.1.21");
    public static Task StageCandidate(string zip, string version) => Run(zip, version);
    private static async Task Run(string? candidateZip, string version)
    {
        using var fixture=new UpdateFixture();var probe=candidateZip==null?await fixture.Probe():null;
        var bytes=candidateZip==null?File.ReadAllBytes(fixture.Zip(("VRCGroupTools.exe",File.ReadAllBytes(probe!)),("SHA256SUMS.txt",Encoding.ASCII.GetBytes("legacy ignores metadata")))):File.ReadAllBytes(candidateZip);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var address=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var requests=new List<string>();
        var serve=Task.Run(async()=>{
            for(var i=0;i<2;i++)
            {
                using var client=await listener.AcceptTcpClientAsync();using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                var first=await reader.ReadLineAsync();requests.Add(first??"");while(!string.IsNullOrEmpty(await reader.ReadLineAsync())){}
                var release=JsonSerializer.Serialize(new[]{new { tag_name="v"+version,prerelease=false,draft=false,assets=new[]{new{name=$"VRCGroupTools_Setup_{version}.exe",browser_download_url=address+"never-installer.exe"},new{name=$"VRCGroupTools-v{version}-win-x64.zip",browser_download_url=address+"fixture.zip"}}}});
                var payload=first!.Contains("/releases")?Encoding.UTF8.GetBytes(release):bytes;
                var headers=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nContent-Type: {(first.Contains("/releases")?"application/json":"application/zip")}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers);await stream.WriteAsync(payload);
            }
        });
        try
        {
            Legacy.LegacySandbox.Temp=fixture.Dir;Legacy.LegacySandbox.Target=Path.Combine(fixture.Dir,"old-installed-dummy.exe");File.WriteAllText(Legacy.LegacySandbox.Target,"old 1.1.18 dummy");Legacy.LegacySandbox.Launch=null;Legacy.LegacySandbox.DidShutdown=false;
            var service=new Legacy.UpdateService();typeof(Legacy.UpdateService).GetField("_gitHubClient",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(service,new GitHubClient(new ProductHeaderValue("offline-legacy-test"),new Uri(address)));
            if(!await service.CheckForUpdateAsync()||service.LatestVersion!=version||service.DownloadUrl!=address+"fixture.zip")throw new Exception("Actual 1.1.18 selection failed");
            await service.DownloadAndInstallUpdateAsync();await serve.WaitAsync(TimeSpan.FromSeconds(10));
            if(!Legacy.LegacySandbox.DidShutdown||Legacy.LegacySandbox.Launch==null)throw new Exception("Legacy staging did not request helper/shutdown");
            if(candidateZip!=null)
            {
                using var zip=System.IO.Compression.ZipFile.OpenRead(candidateZip);
                using var payload=zip.GetEntry("VRCGroupTools.exe")!.Open();
                var expected=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
                var selected=Directory.GetFiles(Path.Combine(fixture.Dir,"VRCGroupTools_Update"),"*.exe",SearchOption.AllDirectories).First();
                if(VRCGroupTools.Services.UpdatePackage.Hash(selected)!=expected||FileVersionInfo.GetVersionInfo(selected).FileVersion!=version+".0")throw new Exception("Legacy candidate staging selected wrong bytes/version");
                // Deliberately do not run the captured helper: that would execute the real production application.
                return;
            }
            var marker=Path.Combine(fixture.Dir,"legacy-relaunch.marker");var captured=Legacy.LegacySandbox.Launch;captured.FileName=Path.Combine(Environment.SystemDirectory,"cmd.exe");captured.UseShellExecute=false;captured.Environment["VRCGT_UPDATE_PROBE"]=marker;
            captured.Environment["PATH"]=Environment.SystemDirectory+";"+Environment.GetEnvironmentVariable("PATH");
            // Legacy deletes its own batch while cmd is reading it, so exit code can be nonzero after successful copy/start.
            await PackageTests.Run(captured);
            for(var i=0;i<100&&!File.Exists(marker);i++)await Task.Delay(20);
            if(!File.Exists(marker)||VRCGroupTools.Services.UpdatePackage.Hash(Legacy.LegacySandbox.Target)!=VRCGroupTools.Services.UpdatePackage.Hash(probe!))throw new Exception("Actual legacy helper did not replace and relaunch dummy executable");
            if(requests.Count!=2||requests.Any(r=>r.Contains("never-installer")))throw new Exception("Legacy selected installer/wrong request count");
        }
        finally{listener.Stop();}
    }
}
