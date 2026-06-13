using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noctra.Avalonia.Localization;
using Noctra.Avalonia.Services;
using Noctra.Avalonia.Views;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Noctra.Core.Services;
using Noctra.Models;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Noctra.Avalonia;

public partial class App : Application
{
    private static readonly TimeSpan SharedHttpClientTimeout = TimeSpan.FromMinutes(3);

    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        try
        {
            RegisterCrashHandlers();
            AvaloniaXamlLoader.Load(this);

            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.MaxServicePointIdleTime = 1000;
            ServicePointManager.DnsRefreshTimeout = 120000;

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            
            // Phase 29: Move blocking schema fixups to an async flow to avoid deadlock
            // ApplySchemaFixupsAsync(db).GetAwaiter().GetResult(); 
            // We will call this inside OnFrameworkInitializationCompleted's background task
            

            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var themeService = Services.GetRequiredService<IThemeService>();
            var localizationService = Services.GetRequiredService<ILocalizationService>();
            ApplyApplicationLanguage(settings.Settings.Language);
            themeService.SetTheme(settings.Settings.IsDarkTheme);
            localizationService.SetLanguage(settings.Settings.Language ?? "en");
            LocalizationSource.Instance.Initialize(localizationService);

            settings.SettingsChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ApplyApplicationLanguage(settings.Settings.Language);
                        themeService.SetTheme(settings.Settings.IsDarkTheme);
                        localizationService.SetLanguage(settings.Settings.Language ?? "en");
                    }
                    catch
                    {
                    }
                });
            };
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {

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
                        
                        // 1. Warmup Settings (Lazy load trigger)
                        var settingsService = Services.GetRequiredService<ISettingsService>();
                        _ = settingsService.Settings; 

                        // 2. Warmup EF Core (Triggers first-time model compilation)
                        using (var scope = Services.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            
                            // Phase 29: Apply schema fixups here (async) to avoid UI hang
                            await ApplySchemaFixupsAsync(db);
                            await db.Profiles.AnyAsync();
                        }

                        // 2.1 Purge profiles with expired deletion countdown
                        try
                        {
                            var profileService = Services.GetRequiredService<IProfileService>();
                            await profileService.PurgeExpiredProfilesAsync();
                        }
                        catch (Exception ex)
                        {
                        }

                        // 2.5 TMDB Sync Service is now on-demand (no background processing)

                        // 3. Resolve MainWindow/ProfilesWindow early
                        var profilesWindow = await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            var win = Services.GetRequiredService<ProfilesWindow>();
                            win.DisableAutoSelect = true;
                            return win;
                        });
                        
                        var canContinue = await Dispatcher.UIThread.InvokeAsync(async () =>
                            await ShowLegalConsentAsync(settingsService, splashWindow));
                        if (!canContinue)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                desktop.Shutdown();
                            });
                            return;
                        }

                        // 4. Update Check (Silent)
                        var packageIdentity = Services.GetRequiredService<IPackageIdentityService>();
                        if (!packageIdentity.IsPackaged && settingsService.Settings.AutoUpdate)
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
                        });
                    }
                    catch (Exception ex)
                    {
                        
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
                                            await watchHistoryService.DeleteProfileHistoryAsync(profile.Id);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                }
                            }).Wait();
                        }

                        var video = Services.GetService<IVideoPlayerService>();
                        video?.Dispose();
                    }
                    catch (Exception ex)
                    {
                    }

                    if (Services is IDisposable disposableServices)
                    {
                        try
                        {
                            disposableServices.Dispose();
                        }
                        catch (Exception ex)
                        {
                        }
                    }

                };
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
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
        // The shared timeout prevents playlist/EPG/API requests from hanging forever;
        // streaming-style operations still pass their own CancellationToken where needed.
        services.AddSingleton(_ => CreateOptimizedHttpClient());

        services.AddTransient<IM3UParser, M3UParser>();
        services.AddSingleton<IEpgService, EpgService>(sp => 
            new EpgService(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetRequiredService<LanguageDetectionService>(),
                sp.GetService<ILogger<EpgService>>()
            ));
        services.AddTransient<IMetadataService, MetadataService>();
        services.AddSingleton<IXtreamCodesService, XtreamCodesService>(sp => 
            new XtreamCodesService(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ILocalizationService>()
            ));
        services.AddTransient<IStalkerPortalService, StalkerPortalService>(sp => 
            new StalkerPortalService(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ILocalizationService>()
            ));
        services.AddSingleton<IAppPathService, DesktopAppPathService>();
        services.AddTransient<ICacheService>(sp =>
            new CacheService(sp.GetRequiredService<IAppPathService>()));

        // Domain services changed to Singleton/Transient because they manually manage DB Context lifetimes
        services.AddSingleton<IPlaylistService, PlaylistService>(sp => 
            new PlaylistService(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                sp.GetRequiredService<IM3UParser>(),
                sp.GetRequiredService<IMediaService>(),
                sp.GetRequiredService<IPlaylistOrganizerService>(),
                sp.GetRequiredService<LanguageDetectionService>(),
                sp.GetRequiredService<EpgSourceResolver>(),
                sp.GetRequiredService<IEpgService>(),
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILocalizationService>()
            ));
        services.AddSingleton<IPlaylistOrganizerService, PlaylistOrganizerService>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<IChannelService, ChannelService>();
        services.AddSingleton<IWatchHistoryService, WatchHistoryService>();
        
        services.AddSingleton<IAvatarService, AvatarService>();
        services.AddSingleton<ISettingsService>(sp =>
            new SettingsService(sp.GetRequiredService<IAppPathService>()));
        services.AddSingleton<IAppEditionService, AppEditionService>();
        services.AddSingleton<IContentDownloadService, ContentDownloadService>(sp => 
            new ContentDownloadService(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetService<ILogger<ContentDownloadService>>()
            ));
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IPackageIdentityService, PackageIdentityService>();
        services.AddSingleton<LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<EpgSourceResolver>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        services.AddSingleton<ITmdbSyncService, TmdbSyncService>();
        services.AddSingleton<IDiagnosticReportService, DiagnosticReportService>();

        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IReviewPromptService, ReviewPromptService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>(sp => 
            new VideoPlayerService(
                sp.GetRequiredService<IDispatcherService>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILocalizationService>()
            ));
        services.AddSingleton<ISecurityService, DesktopSecurityService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddTransient<WatermarkViewModel>();
        
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PlayerViewModel>(sp => 
            new PlayerViewModel(
                sp.GetRequiredService<IVideoPlayerService>(),
                sp.GetRequiredService<IEpgService>(),
                sp.GetRequiredService<IMetadataService>(),
                sp.GetRequiredService<IMediaService>(),
                sp.GetRequiredService<IContentDownloadService>(),
                sp.GetRequiredService<INetworkService>(),
                sp.GetRequiredService<IDispatcherService>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILicenseService>(),
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetRequiredService<MainViewModel>(),
                sp.GetService<IWatchHistoryService>(),
                sp.GetRequiredService<Noctra.Services.Interfaces.IStalkerPortalService>()
            ));
        
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
        services.AddTransient<ReviewPromptWindow>();
        services.AddTransient<LegalConsentWindow>();
        services.AddTransient<ProfileLoadingWindow>();
    }

    private async Task<bool> ShowLegalConsentAsync(ISettingsService settingsService, Window owner)
    {
        if (!RequiresLegalConsent(settingsService.Settings))
        {
            return true;
        }

        var settings = settingsService.Settings;
        var dialog = Services.GetRequiredService<LegalConsentWindow>();
        var result = await dialog.ShowDialog<LegalConsentResult?>(owner);
        if (result?.IsAccepted != true)
        {
            return false;
        }

        settings.LegalConsentAccepted = true;
        settings.LegalConsentVersion = AppSettings.CurrentLegalConsentVersion;
        settings.PrivacyNoticeVersion = AppSettings.CurrentPrivacyNoticeVersion;
        settings.LegalConsentAcceptedAtUtc = DateTime.UtcNow;
        settings.DiagnosticDataConsent = result.DiagnosticDataConsent;
        await settingsService.SaveAsync();

        return true;
    }

    private static bool RequiresLegalConsent(AppSettings settings)
    {
        return !settings.LegalConsentAccepted ||
               !string.Equals(settings.LegalConsentVersion, AppSettings.CurrentLegalConsentVersion, StringComparison.Ordinal) ||
               !string.Equals(settings.PrivacyNoticeVersion, AppSettings.CurrentPrivacyNoticeVersion, StringComparison.Ordinal);
    }

    private static string ResolveDatabasePath()
    {
        var candidates = new[]
        {
            AppPaths.DatabasePath,
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
        var client = new HttpClient(handler) { Timeout = SharedHttpClientTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    private static async Task ApplySchemaFixupsAsync(AppDbContext context)
    {
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE ProviderAccounts ADD COLUMN ExpirationDate TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN PinHash TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN PendingDeletionAt TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgUrl TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN DetectedCountry TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgLastUpdated TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN EpgLastError TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN SourceEtag TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN SourceLastModified TEXT;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Playlists ADD COLUMN SourceContentLength INTEGER;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0;"); } catch { }
        
        // Phase 29: Defensive fix for phantom CurrentProgramId column seen in logs
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Channels ADD COLUMN CurrentProgramId INTEGER;"); } catch { }

        // Composite indexes for hot menu/filter paths. Single-column indexes are not enough for
        // PlaylistId + Type + GroupTitle + newest-first paging used by the card grids.
        try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Type_Group_Id ON Channels(PlaylistId, Type, GroupTitle, Id DESC);"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Type_Id ON Channels(PlaylistId, Type, Id DESC);"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Group_Id ON Channels(PlaylistId, GroupTitle, Id DESC);"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Favorite_Id ON Channels(PlaylistId, IsFavorite, Id DESC);"); } catch { }
        
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
        } catch { }

        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Seasons ADD COLUMN TmdbSeasonId INTEGER;"); } catch { }
        
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
        }
        catch (Exception ex)
        {
        }
    }

    private static void ApplyApplicationLanguage(string? languageCode)
    {
        var normalized = (languageCode ?? "en").Trim().ToLowerInvariant();
        var cultureName = normalized switch
        {
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            _ => "en-US"
        };

        var culture = new CultureInfo(cultureName);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {

                
                // Mailto penceresini açmayı dene
                var reportService = Services?.GetService<IDiagnosticReportService>();
                if (reportService != null)
                {
                    if (e.IsTerminating)
                    {
                        // Uygulama kapanmak üzere, doğrudan açmayı dene
                        reportService.OpenCrashReport(ex, "Global (Terminating)");
                    }
                    else
                    {
                        // UI thread'ine post ederek aç
                        Dispatcher.UIThread.Post(() => reportService.OpenCrashReport(ex, "Global"));
                    }
                }
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {

            e.SetObserved(); // Sürecin ölmesini engelle
            
            // Task hataları çok sık olabilir (özellikle ağ kopmalarında), 
            // kullanıcıyı rahatsız etmemek için otomatik mail açma kapalı bırakılabilir.
            // Ama yine de kritikse burası aktif edilebilir.
        };
    }
}
