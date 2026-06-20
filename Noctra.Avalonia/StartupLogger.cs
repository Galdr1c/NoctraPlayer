using System;
using System.Diagnostics;
using System.IO;

namespace Noctra.Avalonia;

internal static class StartupLogger
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Noctra",
        "startup_debug.log"
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
}
