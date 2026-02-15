using Avalonia;

namespace Noctra.Avalonia;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var tracePath = Path.Combine(AppContext.BaseDirectory, "startup_trace.txt");
            File.AppendAllText(tracePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Main entered.{Environment.NewLine}");
        }
        catch
        {
            // Keep startup resilient even if trace write fails.
        }

        StartupDiagnostics.Initialize();
        StartupDiagnostics.Log("Program.Main entered.");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            StartupDiagnostics.Log("Application lifetime ended normally.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Fatal exception in Program.Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
