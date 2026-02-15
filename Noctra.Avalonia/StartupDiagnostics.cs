using System.Text;

namespace Noctra.Avalonia;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static bool _initialized;
    private static string _logFilePath = string.Empty;

    public static string LogFilePath => _logFilePath;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _logFilePath = ResolveLogPath();

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogException("Unhandled AppDomain exception", ex);
                }
                else
                {
                    Log($"Unhandled AppDomain exception (non-exception object): {e.ExceptionObject}");
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogException("Unobserved task exception", e.Exception);
            };

            _initialized = true;
            Log("Startup diagnostics initialized.");
        }
    }

    public static void Log(string message)
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";

            try
            {
                File.AppendAllText(_logFilePath, line, Encoding.UTF8);
            }
            catch
            {
                try
                {
                    _logFilePath = Path.Combine(Path.GetTempPath(), "Noctra", "startup.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                    File.AppendAllText(_logFilePath, line, Encoding.UTF8);
                }
                catch
                {
                    // Diagnostics logging must never crash startup.
                }
            }
        }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"{context}: {ex}");
    }

    private static string ResolveLogPath()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "Noctra", "logs");
            Directory.CreateDirectory(logDir);
            return Path.Combine(logDir, "startup.log");
        }
        catch
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "Noctra");
            Directory.CreateDirectory(tempDir);
            return Path.Combine(tempDir, "startup.log");
        }
    }
}
