using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string Version => "1.0.0";
    public static string GitHubRepo => "YourUsername/VRCGroupTools"; // Change this to your repo

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        Console.WriteLine("==================================================");
        Console.WriteLine($"  VRC Group Tools v{Version} - DEBUG MODE");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        
        Console.WriteLine("[DEBUG] Services configured");

        // Check for updates on startup (async, don't block)
        _ = CheckForUpdatesAsync();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<IVRChatApiService, VRChatApiService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<BadgeScannerViewModel>();
        
        Console.WriteLine("[DEBUG] All services registered");
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            Console.WriteLine("[DEBUG] Checking for updates...");
            var updateService = Services.GetRequiredService<IUpdateService>();
            var hasUpdate = await updateService.CheckForUpdateAsync();
            
            if (hasUpdate)
            {
                Console.WriteLine($"[DEBUG] Update available: v{updateService.LatestVersion}");
                var result = MessageBox.Show(
                    $"A new version is available!\n\nCurrent: v{Version}\nLatest: v{updateService.LatestVersion}\n\nWould you like to download the update?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await updateService.DownloadAndInstallUpdateAsync();
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] No updates available");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Update check failed: {ex.Message}");
        }
    }
}
