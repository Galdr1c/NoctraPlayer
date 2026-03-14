using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noctra.Avalonia.Services;
using Noctra.Avalonia.Views;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Noctra.Core.Services;
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
                // Show lightweight splash screen immediately
                var splashWindow = new Views.SplashWindow();
                desktop.MainWindow = splashWindow;

                // Fire and forget warmup
                _ = Task.Run(async () =>
                {
                    var startupStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        StartupDiagnostics.Log("Background warmup started.");
                        
                        // 1. Warmup Settings (Lazy load trigger)
                        var settingsService = Services.GetRequiredService<ISettingsService>();
                        _ = settingsService.Settings; 
                        StartupDiagnostics.Log("Settings warmed up.");

                        // 2. Warmup EF Core (Triggers first-time model compilation)
                        using (var scope = Services.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            await db.Profiles.AnyAsync();
                            StartupDiagnostics.Log("EF Core warmed up.");
                        }

                        // 2.5 TMDB Sync Service is now on-demand (no background processing)
                        StartupDiagnostics.Log("TMDB Sync Service ready (on-demand mode).");

                        // 3. Resolve MainWindow/ProfilesWindow early
                        var profilesWindow = await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            var win = Services.GetRequiredService<ProfilesWindow>();
                            win.DisableAutoSelect = true;
                            return win;
                        });
                        
                        StartupDiagnostics.Log("ProfilesWindow resolved.");

                        // 4. Update Check (Silent)
                        if (settingsService.Settings.AutoUpdate)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var updateService = Services.GetRequiredService<IUpdateService>();
                                    var update = await updateService.CheckForUpdatesAsync();
                                    if (update != null)
                                    {
                                        var dialogService = Services.GetRequiredService<IDialogService>();
                                        await Dispatcher.UIThread.InvokeAsync(async () =>
                                        {
                                            var confirmed = await dialogService.ShowConfirmationAsync(
                                                "Yeni Güncelleme Mevcut",
                                                $"v{update.Version} sürümü yayınlandı. Şimdi indirmek ister misiniz?"
                                            );
                                            if (confirmed)
                                            {
                                                await updateService.StartUpdateAsync(update);
                                            }
                                        });
                                    }
                                }
                                catch { /* Ignore background update check failures */ }
                            });
                        }

                        // Ensure a minimum splash duration (e.g., 1.5 seconds) for premium feel
                        var elapsed = startupStopwatch.ElapsedMilliseconds;
                        if (elapsed < 1500)
                        {
                            await Task.Delay(1500 - (int)elapsed);
                        }

                        // Transition to Main Window
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            desktop.MainWindow = profilesWindow;
                            profilesWindow.Show();
                            splashWindow.Close();
                            StartupDiagnostics.Log("Transitioned from Splash to ProfilesWindow.");
                        });
                    }
                    catch (Exception ex)
                    {
                        StartupDiagnostics.LogException("Startup warmup failed", ex);
                        
                        // Fallback: Just try to open the app anyway if warmup fails
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var win = Services.GetRequiredService<ProfilesWindow>();
                            win.DisableAutoSelect = true;
                            desktop.MainWindow = win;
                            win.Show();
                            splashWindow.Close();
                        });
                    }
                });

                desktop.Exit += (_, _) =>
                {
                    try
                    {
                        var settingsService = Services.GetService<ISettingsService>();
                        var profileService = Services.GetService<IProfileService>();
                        var watchHistoryService = Services.GetService<IWatchHistoryService>();
                        
                        if (settingsService != null && profileService != null && watchHistoryService != null)
                        {
                            // 1. Tüm profilleri al
                            // 2. Her birinin ayarlarını "dikizle" (peek)
                            // 3. ClearHistoryOnExit aktifse temizle
                            Task.Run(async () => 
                            {
                                try 
                                {
                                    var profiles = await profileService.GetProfilesAsync();
                                    foreach (var profile in profiles)
                                    {
                                        var profileSettings = await settingsService.PeekProfileSettingsAsync(profile.Id);
                                        if (profileSettings?.ClearHistoryOnExit == true)
                                        {
                                            StartupDiagnostics.Log($"Clearing history for profile {profile.Id} on exit...");
                                            await watchHistoryService.DeleteProfileHistoryAsync(profile.Id);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    StartupDiagnostics.LogException("Error during multi-profile history cleanup on exit", ex);
                                }
                            }).Wait();
                        }

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
        
        // Register IDbContextFactory instead of a scoped DbContext
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // HttpClient as Singleton: SocketsHttpHandler already manages connection pooling.
        // Transient would create new handler per resolution, defeating pooling and causing socket exhaustion.
        // PooledConnectionLifetime (5min) handles DNS rotation for long-lived instances.
        services.AddSingleton(_ => CreateOptimizedHttpClient());

        services.AddTransient<IM3UParser, M3UParser>();
        services.AddSingleton<IEpgService, EpgService>(sp => 
            new EpgService(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetService<ILogger<EpgService>>()
            ));
        services.AddTransient<IMetadataService, MetadataService>();
        services.AddSingleton<IXtreamCodesService, XtreamCodesService>();
        services.AddTransient<IStalkerPortalService, StalkerPortalService>();
        services.AddTransient<ICacheService, CacheService>();

        // Domain services changed to Singleton/Transient because they manually manage DB Context lifetimes
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<IPlaylistOrganizerService, PlaylistOrganizerService>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<IChannelService, ChannelService>();
        services.AddSingleton<IWatchHistoryService, WatchHistoryService>();
        
        services.AddSingleton<IAvatarService, AvatarService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IContentDownloadService, ContentDownloadService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<LanguageDetectionService>();
        services.AddSingleton<EpgSourceResolver>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        services.AddSingleton<ITmdbSyncService, TmdbSyncService>();

        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<AvaloniaImageCacheService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>();
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddTransient<WatermarkViewModel>();
        
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PlayerViewModel>();
        
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<AddProfileViewModel>();
        services.AddTransient<AvatarPickerViewModel>();
        services.AddTransient<GlobalSettingsViewModel>();
        services.AddTransient<EditChannelViewModel>();
        services.AddTransient<ProfileLoadingViewModel>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<ProfilesWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<GlobalSettingsWindow>();
        services.AddTransient<AddProfileWindow>();
        services.AddTransient<EditChannelWindow>();
        services.AddTransient<AvatarPickerWindow>();
        services.AddTransient<UpsellWindow>();
        services.AddTransient<ProfileLoadingWindow>();
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
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    private static async Task ApplySchemaFixupsAsync(AppDbContext context)
    {
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE ProviderAccounts ADD COLUMN ExpirationDate TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgUrl TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN DetectedCountry TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgLastUpdated TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgLastError TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN SourceEtag TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN SourceLastModified TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN SourceContentLength INTEGER;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN IntroStartSec REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN IntroEndSec REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN CreditsStartSec REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN IsInMyList INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN Genre TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN Plot TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN ReleaseYear INTEGER;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN Rating REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN ContentRating TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN PlaylistId INTEGER NOT NULL DEFAULT 0;"); } catch { }
        
        // TMDB Extensions
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN TmdbId INTEGER;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN TmdbTitle TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN LastTmdbSync TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN Cast TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN Director TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN BackdropUrl TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN TrailerUrl TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN MetadataFetchedAt TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN GroupTitle TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN NetworkName TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Series ADD COLUMN NetworkLogoUrl TEXT;"); } catch { }
        
        try 
        { 
            // Invalidate TMDB cache for EU series so they fetch English metadata instead of the cached Turkish metadata
            await context.Database.ExecuteSqlRawAsync(@"
                UPDATE Series 
                SET Plot = NULL, Cast = NULL, BackdropUrl = NULL, TrailerUrl = NULL, ContentRating = NULL, MetadataFetchedAt = NULL 
                WHERE GroupTitle LIKE 'EU %' OR GroupTitle LIKE 'EU|%' OR GroupTitle = 'EU'");
        } catch (Exception ex) { StartupDiagnostics.Log($"Failed to migrate GroupTitle: {ex.Message}"); }

        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Seasons ADD COLUMN TmdbSeasonId INTEGER;"); } catch (Exception ex) { StartupDiagnostics.Log($"Failed to add TmdbSeasonId: {ex.Message}"); }
        
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN Genre TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN ReleaseYear INTEGER;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN Rating REAL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN ContentRating TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN TmdbId INTEGER;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN LastTmdbSync TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN IsInMyList INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN WatchedPosition TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN Country TEXT;"); } catch { }
        
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Seasons ADD COLUMN Plot TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN AirDate TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Episodes ADD COLUMN TmdbEpisodeName TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SeriesEpisodeProgresses ADD COLUMN TmdbId INTEGER;"); } catch { }
        
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS DownloadItems (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadItems PRIMARY KEY AUTOINCREMENT,
    ProfileId INTEGER NOT NULL,
    PlaylistId INTEGER NOT NULL DEFAULT 0,
    ChannelId INTEGER NULL,
    EpisodeId INTEGER NULL,
    ChannelType INTEGER NOT NULL DEFAULT 1,
    DisplayName TEXT NOT NULL,
    PosterUrl TEXT NULL,
    SourceUrl TEXT NOT NULL,
    LocalFilePath TEXT NULL,
    TempFilePath TEXT NULL,
    AudioTracksJson TEXT NULL,
    SubtitleTracksJson TEXT NULL,
    Status INTEGER NOT NULL DEFAULT 0,
    BytesDownloaded INTEGER NOT NULL DEFAULT 0,
    BytesTotal INTEGER NULL,
    SpeedBytesPerSecond REAL NOT NULL DEFAULT 0,
    EstimatedSecondsRemaining INTEGER NULL,
    ErrorMessage TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CompletedAt TEXT NULL
);");
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_DownloadItems_ProfileId ON DownloadItems(ProfileId);");
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_DownloadItems_Status ON DownloadItems(Status);");
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_DownloadItems_ProfileStatusCreated ON DownloadItems(ProfileId, Status, CreatedAt);");
            
            // Fix: Rename LocalEncryptedPath to LocalFilePath if it's an old database
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE DownloadItems RENAME COLUMN LocalEncryptedPath TO LocalFilePath;");
            }
            catch { }

            // Cleanup: Mark old encrypted files as failed/obsolete
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE DownloadItems SET Status = 4, ErrorMessage = 'Eski format. Lütfen tekrar indirin.' " +
                    "WHERE LocalFilePath LIKE '%.nctra' AND Status = 3;");
            }
            catch { }
        }
        catch { }

        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS SeriesEpisodeProgresses (
    Id INTEGER NOT NULL CONSTRAINT PK_SeriesEpisodeProgresses PRIMARY KEY AUTOINCREMENT,
    ProfileId INTEGER NOT NULL,
    SeriesKey TEXT NOT NULL,
    SeriesTitle TEXT NOT NULL,
    SeasonNumber INTEGER NOT NULL,
    EpisodeNumber INTEGER NOT NULL,
    LastWatchedAt TEXT NOT NULL,
    StoppedAt TEXT NOT NULL,
    Duration TEXT NULL,
    Completed INTEGER NOT NULL DEFAULT 0
);");
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_SeriesEpisodeProgresses_ProfileId ON SeriesEpisodeProgresses(ProfileId);");
            await context.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_SeriesEpisodeProgresses_UniqueEpisode ON SeriesEpisodeProgresses(ProfileId, SeriesKey, SeasonNumber, EpisodeNumber);");
        }
        catch { }

        // Enable Foreign Keys for SQLite to ensure Cascade Deletes work properly
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failed to enable SQLite foreign keys", ex);
        }

        // SQLite WAL mode + performance PRAGMAs
        // WAL enables concurrent reads during writes — UI stays responsive while importing
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA cache_size=-64000;"); // 64MB cache
            await context.Database.ExecuteSqlRawAsync("PRAGMA temp_store=MEMORY;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA mmap_size=268435456;"); // 256MB mmap
            StartupDiagnostics.Log("SQLite WAL mode and performance PRAGMAs enabled.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failed to enable SQLite WAL mode", ex);
        }
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
