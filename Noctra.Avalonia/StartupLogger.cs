using System;
using System.Diagnostics;
using System.IO;
using Noctra.Core.Services;

namespace Noctra.Avalonia;

internal static class StartupLogger
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Noctra",
        "startup_debug.log"
    );

    // Crash log: asla başlangıçta silinmez. startup_debug.log her açılışta
    // temizlendiği için, terminating exception kaydı bu ayrı, append-only
    // dosyada bir sonraki açılışa kadar korunur. Yol, mobil ile aynı
    // konvansiyonda: IAppPathService.LogsDirectory (UserData\Logs).
    private static readonly string CrashLogFile = Path.Combine(
        new DesktopAppPathService().LogsDirectory,
        "crash.log"
    );

    static StartupLogger()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            // Clear old log on startup
            if (File.Exists(LogFile))
            {
                File.Delete(LogFile);
            }
        }
        catch
        {
            // Ignore file system errors
        }
    }

    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logLine = $"[{timestamp}] {message}";
        
        // Write to Debug output
        Debug.WriteLine(logLine);
        
        // Write to file
        try
        {
            File.AppendAllText(LogFile, logLine + Environment.NewLine);
        }
        catch
        {
            // Ignore file write errors
        }
    }

    public static void LogError(string context, Exception ex)
    {
        Log($"❌ ERROR in {context}: {ex.GetType().Name}: {ex.Message}");
        Log($"   Stack: {ex.StackTrace}");
        
        if (ex.InnerException != null)
        {
            Log($"   Inner: {ex.InnerException.Message}");
        }
    }

    public static void LogCrash(string context, Exception ex)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CRASH in {context}: {ex}";
        Debug.WriteLine(line);

        try
        {
            var dir = Path.GetDirectoryName(CrashLogFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(CrashLogFile, line + Environment.NewLine);
        }
        catch
        {
            // Dosya sistemi hatası: WER zaten crash'i yakalar, log best-effort.
        }
    }
}
