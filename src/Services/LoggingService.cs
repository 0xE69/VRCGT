using System.IO;
using System.Runtime.CompilerServices;

namespace VRCGroupTools.Services;

public static class LoggingService
{
    private static readonly string LogFolder;
    private static readonly string CrashFolder;
    private static readonly string CurrentLogFile;
    private static readonly object _lock = new();
    private static StreamWriter? _logWriter;

    static LoggingService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRCGroupTools");
        
        LogFolder = Path.Combine(appDataPath, "Logs");
        CrashFolder = Path.Combine(appDataPath, "CrashReports");
        
        Directory.CreateDirectory(LogFolder);
        Directory.CreateDirectory(CrashFolder);
        
        // Create log file with timestamp
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        CurrentLogFile = Path.Combine(LogFolder, $"log_{timestamp}.txt");
        
        // Clean up old logs (keep last 10)
        CleanupOldFiles(LogFolder, "log_*.txt", 10);
        CleanupOldFiles(CrashFolder, "crash_*.txt", 20);
    }

    public static void Initialize()
    {
        try
        {
            _logWriter = new StreamWriter(CurrentLogFile, append: true) { AutoFlush = true };
            Log("INFO", "LoggingService", "Logging initialized");
            Log("INFO", "LoggingService", $"Log file: {CurrentLogFile}");
            Log("INFO", "LoggingService", $"App Version: {App.Version}");
            Log("INFO", "LoggingService", $"OS: {Environment.OSVersion}");
            Log("INFO", "LoggingService", $".NET: {Environment.Version}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to initialize logging: {ex.Message}");
        }
    }

    public static void Log(string level, string source, string message,
        [CallerMemberName] string memberName = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] [{source}] {message}";
        
        // Write to console
        Console.WriteLine($"[{source}] {message}");
        
        // Write to file
        lock (_lock)
        {
            try
            {
                _logWriter?.WriteLine(logLine);
            }
            catch
            {
                // Ignore file write errors
            }
        }
    }

    public static void Info(string source, string message) => Log("INFO", source, message);
    public static void Debug(string source, string message) => Log("DEBUG", source, message);
    public static void Warn(string source, string message) => Log("WARN", source, message);
    public static void Error(string source, string message) => Log("ERROR", source, message);
    
    public static void Error(string source, Exception ex, string context = "")
    {
        var message = string.IsNullOrEmpty(context) 
            ? $"{ex.GetType().Name}: {ex.Message}" 
            : $"{context} - {ex.GetType().Name}: {ex.Message}";
        
        Log("ERROR", source, message);
        Log("ERROR", source, $"Stack: {ex.StackTrace}");
        
        if (ex.InnerException != null)
        {
            Log("ERROR", source, $"Inner: {ex.InnerException.Message}");
        }
    }

    public static string WriteCrashReport(Exception ex, string context = "")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var crashFile = Path.Combine(CrashFolder, $"crash_{timestamp}.txt");
        
        try
        {
            using var writer = new StreamWriter(crashFile);
            writer.WriteLine("========================================");
            writer.WriteLine("   VRC Group Tools - Crash Report");
            writer.WriteLine("========================================");
            writer.WriteLine();
            writer.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"App Version: {App.Version}");
            writer.WriteLine($"OS: {Environment.OSVersion}");
            writer.WriteLine($".NET Version: {Environment.Version}");
            writer.WriteLine();
            
            if (!string.IsNullOrEmpty(context))
            {
                writer.WriteLine($"Context: {context}");
                writer.WriteLine();
            }
            
            writer.WriteLine("========================================");
            writer.WriteLine("   Exception Details");
            writer.WriteLine("========================================");
            writer.WriteLine();
            
            WriteExceptionDetails(writer, ex, 0);
            
            writer.WriteLine();
            writer.WriteLine("========================================");
            writer.WriteLine("   Recent Log Entries");
            writer.WriteLine("========================================");
            writer.WriteLine();
            
            // Include last 50 lines from current log
            try
            {
                if (File.Exists(CurrentLogFile))
                {
                    var lines = File.ReadAllLines(CurrentLogFile);
                    var startIndex = Math.Max(0, lines.Length - 50);
                    for (int i = startIndex; i < lines.Length; i++)
                    {
                        writer.WriteLine(lines[i]);
                    }
                }
            }
            catch
            {
                writer.WriteLine("(Could not read recent logs)");
            }
            
            Log("ERROR", "CRASH", $"Crash report written to: {crashFile}");
            return crashFile;
        }
        catch (Exception writeEx)
        {
            Console.WriteLine($"[ERROR] Failed to write crash report: {writeEx.Message}");
            return string.Empty;
        }
    }

    private static void WriteExceptionDetails(StreamWriter writer, Exception ex, int depth)
    {
        var indent = new string(' ', depth * 2);
        
        writer.WriteLine($"{indent}Exception Type: {ex.GetType().FullName}");
        writer.WriteLine($"{indent}Message: {ex.Message}");
        writer.WriteLine($"{indent}Source: {ex.Source}");
        writer.WriteLine();
        writer.WriteLine($"{indent}Stack Trace:");
        
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            foreach (var line in ex.StackTrace.Split('\n'))
            {
                writer.WriteLine($"{indent}  {line.Trim()}");
            }
        }
        else
        {
            writer.WriteLine($"{indent}  (No stack trace available)");
        }
        
        if (ex.InnerException != null)
        {
            writer.WriteLine();
            writer.WriteLine($"{indent}--- Inner Exception ---");
            WriteExceptionDetails(writer, ex.InnerException, depth + 1);
        }
    }

    private static void CleanupOldFiles(string folder, string pattern, int keepCount)
    {
        try
        {
            var files = Directory.GetFiles(folder, pattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(keepCount)
                .ToList();
            
            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Ignore deletion errors
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public static void Shutdown()
    {
        Log("INFO", "LoggingService", "Application shutting down");
        lock (_lock)
        {
            _logWriter?.Flush();
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }

    public static string GetLogFolder() => LogFolder;
    public static string GetCrashFolder() => CrashFolder;
    public static string GetCurrentLogFile() => CurrentLogFile;
}
