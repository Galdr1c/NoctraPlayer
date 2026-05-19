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
            StartupDiagnostics.Initialize();

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

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
