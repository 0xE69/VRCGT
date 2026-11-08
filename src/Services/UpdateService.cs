using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Octokit;
namespace VRCGroupTools.Services;

public interface IUpdateService
{
    string? LatestVersion { get; }
    string? DownloadUrl { get; }
    Task<bool> CheckForUpdateAsync();
    Task DownloadAndInstallUpdateAsync();
}

public class UpdateService : IUpdateService
{
    private readonly Func<Task<IReadOnlyList<UpdateRelease>>> _releases;
    private readonly HttpClient _http;
    private readonly string _current;
    private UpdateCandidate? _candidate;
    public string? LatestVersion => _candidate?.Version;
    public string? DownloadUrl => _candidate?.ZipUrl;
    public UpdateService() : this(ReadReleasesAsync, new HttpClient { Timeout=TimeSpan.FromMinutes(10) }, App.Version) { }
    public UpdateService(Func<Task<IReadOnlyList<UpdateRelease>>> releases, HttpClient httpClient, string? currentVersion = null)
    { _releases=releases; _http=httpClient; _current=currentVersion??App.Version; }
    private static async Task<IReadOnlyList<UpdateRelease>> ReadReleasesAsync()
    {
        var parts=App.GitHubRepo.Split('/');
        if(parts.Length!=2)throw new InvalidOperationException("Invalid update repository");
        var client=new GitHubClient(new ProductHeaderValue("VRCGroupTools"));
        var releases=await client.Repository.Release.GetAll(parts[0],parts[1]);
        return releases.Select(r=>new UpdateRelease(r.TagName,r.Draft,r.Prerelease,r.Assets.Select(a=>new UpdateAsset(a.Name,a.BrowserDownloadUrl)).ToArray())).ToArray();
    }
    public async Task<bool> CheckForUpdateAsync()
    {
        _candidate=null;
        try { _candidate=UpdateCatalog.Select(await _releases(),_current);return _candidate!=null; }
        catch(Exception ex){Debug.WriteLine($"Update check failed: {ex.GetType().Name}");return false;}
    }
    public async Task<string> DownloadAndStageAsync(string workspace, CancellationToken token=default)
    {
        var candidate=_candidate??throw new InvalidOperationException("Check for an available update first");
        Directory.CreateDirectory(workspace);
        string? expectedHash=null;
        if(candidate.ChecksumUrl!=null)
        {
            using var check=await _http.GetAsync(candidate.ChecksumUrl,HttpCompletionOption.ResponseHeadersRead,token);check.EnsureSuccessStatusCode();
            await using var input=await check.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();
            await UpdatePackage.CopyBoundedAsync(input,output,4096,token);
            var text=Encoding.UTF8.GetString(output.ToArray()).TrimEnd((char)13, (char)10);
            var parts=text.Split(new[] { "  " }, StringSplitOptions.None);
            if(parts.Length is < 1 or > 2 || !Regex.IsMatch(parts[0], @"\A[a-fA-F0-9]{64}\z") || parts.Length == 2 && parts[1] != $"VRCGroupTools-v{candidate.Version}-win-x64.zip")
                throw new InvalidDataException("Invalid update checksum asset");
            expectedHash=parts[0];
        }
        var zip=Path.Combine(workspace,"update.zip");
        using(var response=await _http.GetAsync(candidate.ZipUrl,HttpCompletionOption.ResponseHeadersRead,token))
        {
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>UpdatePackage.MaxBytes)throw new InvalidDataException("Update download exceeds size limit");
            await using var input=await response.Content.ReadAsStreamAsync(token);await using var output=new FileStream(zip,System.IO.FileMode.CreateNew);
            await UpdatePackage.CopyBoundedAsync(input,output,UpdatePackage.MaxBytes,token);
        }
        return UpdatePackage.Stage(zip,Path.Combine(workspace,"staged"),expectedHash);
    }
    public async Task DownloadAndInstallUpdateAsync()
    {
        if(_candidate==null)throw new InvalidOperationException("No newer usable release is available");
        try
        {
            var workspace=Path.Combine(Path.GetTempPath(),"VRCGroupTools_Update_"+Guid.NewGuid().ToString("N"));
            var staged=await DownloadAndStageAsync(workspace);
            var info=FileVersionInfo.GetVersionInfo(staged);
            if(info.FileVersion!=_candidate.Version+".0")throw new InvalidDataException("Downloaded executable version does not match the selected release");
            var current=Environment.ProcessPath??throw new InvalidOperationException("Cannot determine application executable");
            var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VRCGroupTools","update-logs");Directory.CreateDirectory(logs);
            var script=UpdateReplacement.Script(staged,current,Environment.ProcessId,Path.Combine(logs,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".log"),UpdatePackage.Hash(staged));
            using var helper=Process.Start(UpdateReplacement.StartInfo(script))??throw new InvalidOperationException("Could not start replacement helper");
            System.Windows.Application.Current.Shutdown();
        }
        catch(Exception ex)
        {
            System.Windows.MessageBox.Show($"Update could not be staged. The existing application is unchanged.\n{ex.Message}","Update Error",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);throw;
        }
    }
}
