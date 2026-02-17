using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Avalonia.Services;
using Noctra.Avalonia.Views;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Noctra.Avalonia;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        try
        {
            StartupDiagnostics.Log("App.Initialize started.");
            AvaloniaXamlLoader.Load(this);
            StartupDiagnostics.Log("AvaloniaXamlLoader.Load completed.");

            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.MaxServicePointIdleTime = 1000;
            ServicePointManager.DnsRefreshTimeout = 120000;

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
            StartupDiagnostics.Log("DI container built.");

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            ApplySchemaFixupsAsync(db).GetAwaiter().GetResult();
            StartupDiagnostics.Log("Database EnsureCreated completed.");

            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var themeService = Services.GetRequiredService<IThemeService>();
            ApplyApplicationLanguage(settings.Settings.Language);
            themeService.SetTheme(settings.Settings.IsDarkTheme);
            settings.SettingsChanged += () => ApplyApplicationLanguage(settings.Settings.Language);
            StartupDiagnostics.Log("Theme applied.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Fatal exception in App.Initialize", ex);
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            StartupDiagnostics.Log("OnFrameworkInitializationCompleted entered.");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var profilesWindow = Services.GetRequiredService<ProfilesWindow>();
                profilesWindow.DisableAutoSelect = true;
                desktop.MainWindow = profilesWindow;
                StartupDiagnostics.Log("ProfilesWindow resolved and assigned as startup window.");
                desktop.Exit += (_, _) =>
                {
                    try
                    {
                        var video = Services.GetService<IVideoPlayerService>();
                        video?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        StartupDiagnostics.LogException("Error while disposing video service on exit", ex);
                    }

                    if (Services is IDisposable disposableServices)
                    {
                        try
                        {
                            disposableServices.Dispose();
                        }
                        catch (Exception ex)
                        {
                            StartupDiagnostics.LogException("Error while disposing service provider on exit", ex);
                        }
                    }

                    StartupDiagnostics.Log("Desktop exit cleanup completed.");
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Fatal exception in OnFrameworkInitializationCompleted", ex);
            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var dbPath = ResolveDatabasePath();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Scoped);

        services.AddTransient(_ => CreateOptimizedHttpClient());

        services.AddTransient<IM3UParser, M3UParser>();
        services.AddTransient<IEpgService, EpgService>();
        services.AddTransient<IMetadataService, MetadataService>();
        services.AddTransient<IXtreamCodesService, XtreamCodesService>();
        services.AddTransient<IStalkerPortalService, StalkerPortalService>();

        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddScoped<IPlaylistOrganizerService, PlaylistOrganizerService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IWatchHistoryService, WatchHistoryService>();
        services.AddSingleton<IAvatarService, AvatarService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<LanguageDetectionService>();
        services.AddSingleton<EpgSourceResolver>();

        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<AvaloniaImageCacheService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>();
        services.AddTransient<WatermarkViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<AddProfileViewModel>();
        services.AddTransient<AvatarPickerViewModel>();
        services.AddTransient<GlobalSettingsViewModel>();
        services.AddTransient<EditChannelViewModel>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<ProfilesWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<GlobalSettingsWindow>();
        services.AddTransient<AddProfileWindow>();
        services.AddTransient<EditChannelWindow>();
        services.AddTransient<AvatarPickerWindow>();
        services.AddTransient<UpsellWindow>();
    }

    private static string ResolveDatabasePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noctra", "noctra_v1.db"),
            Path.Combine(Path.GetTempPath(), "Noctra", "noctra_v1.db")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var dir = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                Directory.CreateDirectory(dir);
                using (File.Open(candidate, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                }

                return candidate;
            }
            catch
            {
                // Try next candidate path.
            }
        }

        return candidates.Last();
    }

    private static HttpClient CreateOptimizedHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler);
    }

    private static async Task ApplySchemaFixupsAsync(AppDbContext context)
    {
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE ProviderAccounts ADD COLUMN ExpirationDate TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgUrl TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN DetectedCountry TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgLastUpdated TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgLastError TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN IntroStartSec REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN IntroEndSec REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN CreditsStartSec REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;"); } catch { }
    }

    private static void ApplyApplicationLanguage(string? languageCode)
    {
        var normalized = (languageCode ?? "tr").Trim().ToLowerInvariant();
        var cultureName = normalized switch
        {
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            _ => "tr-TR"
        };

        var culture = new CultureInfo(cultureName);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
