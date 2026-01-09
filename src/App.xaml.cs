using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using MaterialDesignThemes.Wpf;
using VRCGroupTools.Data;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools;

public partial class App : Application
{
    private static int _handlingException;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static IServiceProvider Services { get; private set; } = null!;
    public static string Version => "1.0.5";
    public static string GitHubRepo => "0xE69/VRCGT";
    public static string BindingLogPath { get; private set; } = string.Empty;

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
            
            // Allocate console if debug mode is enabled
            if (settingsService.Settings.ShowConsoleWindow)
            {
                AllocConsole();
                LoggingService.Info("APP", "Console window enabled via settings");
            }
            
            // Initialize database
            LoggingService.Debug("APP", "Initializing SQLite database...");
            var cacheService = Services.GetRequiredService<ICacheService>();
            await cacheService.InitializeAsync();
            LoggingService.Debug("APP", "Database initialized");

            // Check for updates BEFORE showing login window (to block startup if update needed)
            await CheckForUpdatesAsync();

            // Create and show the login window
            LoggingService.Debug("APP", "Creating LoginWindow...");
            var loginWindow = new Views.LoginWindow();
            LoggingService.Debug("APP", "Showing LoginWindow...");
            loginWindow.Show();
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
        // Guard against re-entrancy / recursive handling
        if (Interlocked.Exchange(ref _handlingException, 1) == 1)
        {
            SafeWriteExceptionLog(e.Exception, "UI Thread Exception (re-entrant)");
            e.Handled = true;
            return;
        }

        var crashFile = SafeWriteExceptionLog(e.Exception, "UI Thread Exception");

        // Avoid MessageBox in the handler to prevent further UI exceptions
        // Keep app running if possible; if the dispatcher is shutting down, exit cleanly
        if (Current?.Dispatcher?.HasShutdownStarted == true)
        {
            e.Handled = true;
            Shutdown();
        }
        else
        {
            e.Handled = true; // try to continue
        }

        Interlocked.Exchange(ref _handlingException, 0);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception("Unknown exception");
        SafeWriteExceptionLog(ex, "Background Thread Exception", isTerminating: e.IsTerminating);

        if (e.IsTerminating)
        {
            Shutdown();
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        SafeWriteExceptionLog(e.Exception, "Unobserved Task Exception");
        e.SetObserved(); // Prevent app crash
    }

    private static string SafeWriteExceptionLog(Exception ex, string context, bool isTerminating = false)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VRCGroupTools",
                "logs");
            Directory.CreateDirectory(logDir);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss_fff");
            var logPath = Path.Combine(logDir, $"crash_{timestamp}.log");

            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}");
            sb.AppendLine($"Context: {context}");
            sb.AppendLine($"IsTerminating: {isTerminating}");
            sb.AppendLine($"App Version: {Version}");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Process Arch: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine();
            AppendException(sb, ex);

            File.WriteAllText(logPath, sb.ToString());
            return logPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void AppendException(StringBuilder sb, Exception ex, string? prefix = null)
    {
        var label = prefix ?? "Exception";
        sb.AppendLine($"{label}: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine("Stack:");
        sb.AppendLine(ex.StackTrace);
        sb.AppendLine();

        if (ex.InnerException != null)
        {
            AppendException(sb, ex.InnerException, "Inner");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services.GetService<IDiscordPresenceService>()?.Dispose();
        }
        catch { }
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
        services.AddSingleton<IDiscordPresenceService, DiscordPresenceService>();
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
        services.AddTransient<GroupPostsViewModel>();
        services.AddTransient<InviteToGroupViewModel>();
        services.AddTransient<KillSwitchViewModel>();
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
                
                // Show custom update prompt on UI thread (modal and blocking)
                bool shouldUpdate = false;
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    var updatePrompt = new Views.UpdatePromptWindow
                    {
                        CurrentVersion = $"v{Version}",
                        LatestVersion = $"v{updateService.LatestVersion}",
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Topmost = true
                    };

                    // ShowDialog() blocks until user responds
                    shouldUpdate = updatePrompt.ShowDialog() == true && updatePrompt.ShouldUpdate;
                });

                if (shouldUpdate)
                {
                    LoggingService.Info("APP", "User accepted update, starting download...");
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

// Writes WPF binding trace output through LoggingService so it is visible in the terminal/logs.
file sealed class BindingTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            LoggingService.Debug("BINDING", message);
        }
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            LoggingService.Debug("BINDING", message);
        }
    }
}
