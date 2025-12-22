using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
    private readonly GitHubClient _gitHubClient;
    private Release? _latestRelease;

    public string? LatestVersion => _latestRelease?.TagName?.TrimStart('v');
    public string? DownloadUrl { get; private set; }

    public UpdateService()
    {
        _gitHubClient = new GitHubClient(new ProductHeaderValue("VRCGroupTools"));
    }

    public async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            var repoParts = App.GitHubRepo.Split('/');
            if (repoParts.Length != 2) return false;

            var releases = await _gitHubClient.Repository.Release.GetAll(repoParts[0], repoParts[1]);
            _latestRelease = releases.FirstOrDefault(r => !r.Prerelease);

            if (_latestRelease == null) return false;

            var latestVersion = _latestRelease.TagName.TrimStart('v');
            var currentVersion = App.Version;

            // Find the installer asset
            var installerAsset = _latestRelease.Assets
                .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                    a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

            if (installerAsset != null)
            {
                DownloadUrl = installerAsset.BrowserDownloadUrl;
            }

            return CompareVersions(latestVersion, currentVersion) > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            return false;
        }
    }

    public async Task DownloadAndInstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(DownloadUrl)) return;

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "VRCGroupTools_Update.exe");

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(DownloadUrl);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(tempPath, System.IO.FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();

            // Launch installer and close current app
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex.Message}");
            throw;
        }
    }

    private static int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(int.Parse).ToArray();
        var parts2 = v2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;

            if (p1 > p2) return 1;
            if (p1 < p2) return -1;
        }

        return 0;
    }
}
