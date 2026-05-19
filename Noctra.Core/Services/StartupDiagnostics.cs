using System;

namespace Noctra.Core.Services;

public static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

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
            if (!_initialized)
            {
                 _initialized = true; 
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            System.Diagnostics.Debug.Write(line);
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
}
