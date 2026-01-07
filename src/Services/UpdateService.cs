using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

            // Find the ZIP asset
            var installerAsset = _latestRelease.Assets
                .FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

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
            var tempDir = Path.Combine(Path.GetTempPath(), "VRCGroupTools_Update");
            var zipPath = Path.Combine(Path.GetTempPath(), "VRCGroupTools_Update.zip");

            // Clean up old temp files
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // Download the ZIP
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(DownloadUrl);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(zipPath, System.IO.FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();

            // Extract the ZIP
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Find the new executable
            var newExePath = Directory.GetFiles(tempDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (newExePath == null)
            {
                throw new Exception("No executable found in update package");
            }

            var currentExePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath))
            {
                throw new Exception("Could not determine current executable path");
            }

            // Create a batch script to replace the exe after the app closes
            var batchPath = Path.Combine(Path.GetTempPath(), "VRCGroupTools_Update.bat");
            var batchContent = "@echo off\n" +
                "echo Updating VRC Group Tools...\n" +
                "timeout /t 2 /nobreak >nul\n" +
                $"copy /Y \"{newExePath}\" \"{currentExePath}\"\n" +
                "if errorlevel 1 (\n" +
                "    echo Update failed!\n" +
                "    pause\n" +
                "    exit /b 1\n" +
                ")\n" +
                "echo Update complete!\n" +
                "timeout /t 1 /nobreak >nul\n" +
                $"start \"\" \"{currentExePath}\"\n" +
                $"rd /s /q \"{tempDir}\"\n" +
                $"del \"{zipPath}\"\n" +
                "del \"%~f0\"\n";

            File.WriteAllText(batchPath, batchContent);

            // Launch the batch script and close the app
            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Failed to download and install update: {ex.Message}",
                "Update Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
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
