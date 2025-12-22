using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MaterialDesignThemes.Wpf;
using VRCGroupTools.Data;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string Version => "1.0.0";
    public static string GitHubRepo => "YourUsername/VRCGroupTools"; // Change this to your repo

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Set up global exception handlers FIRST
        SetupExceptionHandlers();
        
        // Initialize logging
        LoggingService.Initialize();
        
        LoggingService.Info("APP", "==================================================");
        LoggingService.Info("APP", $"  VRC Group Tools v{Version} - Starting");
        LoggingService.Info("APP", "==================================================");

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
            
            LoggingService.Debug("APP", "Services configured");

            // Apply saved theme early
            var settingsService = Services.GetRequiredService<ISettingsService>();
            ApplyTheme(settingsService.Settings.Theme);
            
            // Initialize database
            LoggingService.Debug("APP", "Initializing SQLite database...");
            var cacheService = Services.GetRequiredService<ICacheService>();
            await cacheService.InitializeAsync();
            LoggingService.Debug("APP", "Database initialized");

            // Create and show the login window
            LoggingService.Debug("APP", "Creating LoginWindow...");
            var loginWindow = new Views.LoginWindow();
            LoggingService.Debug("APP", "Showing LoginWindow...");
            loginWindow.Show();

            // Check for updates on startup (async, don't block)
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Error("APP", ex, "Fatal error during startup");
            var crashFile = LoggingService.WriteCrashReport(ex, "Application Startup");
            
            MessageBox.Show(
                $"Fatal error during startup:\n\n{ex.Message}\n\nCrash report saved to:\n{crashFile}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            
            Shutdown();
        }
    }

    private void SetupExceptionHandlers()
    {
        // Handle exceptions on the UI thread
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        
        // Handle exceptions from background threads
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        
        // Handle Task exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LoggingService.Error("APP", e.Exception, "Unhandled UI exception");
        var crashFile = LoggingService.WriteCrashReport(e.Exception, "UI Thread Exception");
        
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nCrash report saved to:\n{crashFile}\n\nThe application will try to continue.",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        
        e.Handled = true; // Prevent app crash, try to continue
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception("Unknown exception");
        LoggingService.Error("APP", ex, "Unhandled background exception");
        var crashFile = LoggingService.WriteCrashReport(ex, "Background Thread Exception");
        
        if (e.IsTerminating)
        {
            MessageBox.Show(
                $"A fatal error occurred:\n\n{ex.Message}\n\nCrash report saved to:\n{crashFile}",
                "Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LoggingService.Error("APP", e.Exception, "Unobserved task exception");
        LoggingService.WriteCrashReport(e.Exception, "Unobserved Task Exception");
        e.SetObserved(); // Prevent app crash
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggingService.Info("APP", "Application exiting");
        LoggingService.Shutdown();
        base.OnExit(e);
    }

    private static void ApplyTheme(string themeName)
    {
        var theme = Current.Resources.MergedDictionaries
            .OfType<BundledTheme>()
            .FirstOrDefault();

        if (theme == null) return;

        theme.BaseTheme = themeName.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? BaseTheme.Light
            : BaseTheme.Dark;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Database & Cache
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<ICacheService, CacheService>();
        
        // Services
        services.AddSingleton<IVRChatApiService, VRChatApiService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddSingleton<IDiscordWebhookService, DiscordWebhookService>();
        services.AddSingleton<ICalendarEventService, CalendarEventService>();

        // ViewModels - use Singleton for ViewModels that need event subscriptions
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<BadgeScannerViewModel>();
        services.AddTransient<UserSearchViewModel>(sp => 
            new UserSearchViewModel(
                sp.GetRequiredService<IVRChatApiService>(),
                sp.GetRequiredService<MainViewModel>()));
        services.AddTransient<AuditLogViewModel>();
        services.AddTransient<CalendarEventViewModel>();
        services.AddTransient<DiscordSettingsViewModel>();
        services.AddTransient<InstanceCreatorViewModel>();
        services.AddTransient<MembersListViewModel>();
        services.AddTransient<BansListViewModel>();
        services.AddTransient<GroupInfoViewModel>();
        services.AddTransient<CreatePostViewModel>();
        services.AddTransient<InviteToGroupViewModel>();
        services.AddTransient<AppSettingsViewModel>();
        
        LoggingService.Debug("APP", "All services registered");
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            LoggingService.Debug("APP", "Checking for updates...");
            var updateService = Services.GetRequiredService<IUpdateService>();
            var hasUpdate = await updateService.CheckForUpdateAsync();
            
            if (hasUpdate)
            {
                LoggingService.Info("APP", $"Update available: v{updateService.LatestVersion}");
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
                LoggingService.Debug("APP", "No updates available");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn("APP", $"Update check failed: {ex.Message}");
        }
    }
}
