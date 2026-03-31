using Avalonia;
using Noctra.Core.Services;
using System;
using System.IO;

namespace Noctra.Avalonia;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            if (File.Exists(envPath))
            {
                DotNetEnv.Env.Load(envPath);
            }
            else
            {
                DotNetEnv.Env.TraversePath().Load();
            }
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
