using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Noctra.Core.Services;

public static class StartupDiagnostics
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
            RotateLogIfNeeded();

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

    /// <summary>
    /// Log dosyası 1MB'ı aşarsa son 500KB'ı tutar, geri kalanını siler.
    /// </summary>
    private static void RotateLogIfNeeded()
    {
        try
        {
            const long maxSizeBytes = 1 * 1024 * 1024; // 1 MB
            const long keepBytes = 500 * 1024;          // 500 KB

            if (!File.Exists(_logFilePath)) return;

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length <= maxSizeBytes) return;

            // Read only the last keepBytes
            var allBytes = File.ReadAllBytes(_logFilePath);
            var tail = allBytes.AsSpan((int)(allBytes.Length - keepBytes));
            
            // Find the first newline in the tail to avoid partial lines
            var newlineIndex = tail.IndexOf((byte)'\n');
            if (newlineIndex >= 0 && newlineIndex < tail.Length - 1)
            {
                tail = tail[(newlineIndex + 1)..];
            }

            File.WriteAllBytes(_logFilePath, tail.ToArray());
        }
        catch
        {
            // Log rotation should never crash startup
        }
    }

    public static void Log(string message)
    {
        lock (Sync)
        {
            if (!_initialized)
            {
                 // Minimal initialize if called before explicit init
                 _logFilePath = ResolveLogPath();
                 _initialized = true; 
            }

            if (string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            
            // Also write to Debug Output for IDE/Console
            System.Diagnostics.Debug.Write(line);

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

    public static void LogRuntimeContext(
        string runtimeMode,
        string? packageFullName,
        string? packageFamilyName,
        string baseDirectory)
    {
        Log(
            $"RuntimeContext Mode={runtimeMode}; " +
            $"PackageFullName={packageFullName ?? "<none>"}; " +
            $"PackageFamilyName={packageFamilyName ?? "<none>"}; " +
            $"BaseDirectory={baseDirectory}");
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
