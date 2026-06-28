using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Mobile.Localization;
using Noctra.Mobile.ViewModels;
using Noctra.Mobile.Views;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Mobile;

public partial class App : Application
{
    public static Func<IServiceProvider>? ServiceProviderFactory { get; set; }

    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        RegisterCrashHandlers();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        EnsureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = CreateMainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => new MainView
            {
                DataContext = CreateMainViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = CreateMainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainViewModel CreateMainViewModel()
    {
        return EnsureServices()?.GetService(typeof(MainViewModel)) as MainViewModel
            ?? new MainViewModel();
    }

    public IServiceProvider? EnsureServices()
    {
        if (Services is not null)
        {
            return Services;
        }

        Services = ServiceProviderFactory?.Invoke();
        if (Services is null)
        {
            return null;
        }

        // ── DB Initialization ────────────────────────────────────────────────
        if (Services.GetService(typeof(IDbContextFactory<AppDbContext>)) is IDbContextFactory<AppDbContext> dbContextFactory)
        {
            try
            {
                using var db = dbContextFactory.CreateDbContext();
                db.Database.EnsureCreated();
                ApplySchemaFixups(db);
            }
            catch (Exception)
            {
                // Soft fail — app still works without schema fixups in worst case
            }
        }

        // ── Localization ─────────────────────────────────────────────────────
        if (Services.GetService(typeof(ILocalizationService)) is ILocalizationService localizationService)
        {
            localizationService.SetLanguage((Services.GetService(typeof(ISettingsService)) as ISettingsService)?.Settings?.Language ?? "en");
            LocalizationSource.Instance.Initialize(localizationService);
        }

        // ── Theme + Settings ─────────────────────────────────────────────────
        if (Services.GetService(typeof(ISettingsService)) is ISettingsService settingsService)
        {
            if (Services.GetService(typeof(IThemeService)) is IThemeService themeService)
            {
                themeService.SetTheme(settingsService.Settings.IsDarkTheme);
            }

            ApplyApplicationLanguage(settingsService.Settings.Language);

            if (string.IsNullOrWhiteSpace(settingsService.Settings.PromoCodeConfigUrl))
            {
                settingsService.Settings.PromoCodeConfigUrl = Mobile.Services.MobileAppConfig.PromoCodesUrl;
            }

            // React to settings changes (language / theme hot-reload)
            settingsService.SettingsChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ApplyApplicationLanguage(settingsService.Settings.Language);
                        if (Services.GetService(typeof(IThemeService)) is IThemeService ts)
                            ts.SetTheme(settingsService.Settings.IsDarkTheme);
                        if (Services.GetService(typeof(ILocalizationService)) is ILocalizationService ls)
                            ls.SetLanguage(settingsService.Settings.Language ?? "en");
                    }
                    catch { }
                });
            };
        }

        // ── Purge expired profiles (fire-and-forget) ─────────────────────────
        if (Services.GetService(typeof(IProfileService)) is IProfileService profileService)
        {
            _ = Task.Run(async () =>
            {
                try { await profileService.PurgeExpiredProfilesAsync(); }
                catch { }
            });
        }

        return Services;
    }

    // ── Schema Fixups ────────────────────────────────────────────────────────
    private static void ApplySchemaFixups(AppDbContext context)
    {
        // ── ProviderAccounts ─────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE ProviderAccounts ADD COLUMN ExpirationDate TEXT;"); } catch { }

        // ── Profiles ─────────────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN PinHash TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN PendingDeletionAt TEXT;"); } catch { }

        // ── Playlists ─────────────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN EpgUrl TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN DetectedCountry TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN EpgLastUpdated TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN EpgLastError TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN SourceEtag TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN SourceLastModified TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN SourceContentLength INTEGER;"); } catch { }

        // ── Channels ─────────────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN CurrentProgramId INTEGER;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN Genre TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN ReleaseYear INTEGER;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN Rating REAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN ContentRating TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN TmdbId INTEGER;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN LastTmdbSync TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN IsInMyList INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN WatchedPosition TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Channels ADD COLUMN Country TEXT;"); } catch { }

        // Composite indexes for hot menu/filter paths
        try { context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Type_Group_Id ON Channels(PlaylistId, Type, GroupTitle, Id DESC);"); } catch { }
        try { context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Type_Id ON Channels(PlaylistId, Type, Id DESC);"); } catch { }
        try { context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Group_Id ON Channels(PlaylistId, GroupTitle, Id DESC);"); } catch { }
        try { context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Favorite_Id ON Channels(PlaylistId, IsFavorite, Id DESC);"); } catch { }

        // ── Episodes ──────────────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Episodes ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Episodes ADD COLUMN IntroStartSec REAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Episodes ADD COLUMN IntroEndSec REAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Episodes ADD COLUMN CreditsStartSec REAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Episodes ADD COLUMN AirDate TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Episodes ADD COLUMN TmdbEpisodeName TEXT;"); } catch { }

        // ── Series ────────────────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN IsInMyList INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN Genre TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN Plot TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN ReleaseYear INTEGER;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN Rating REAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN ContentRating TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN PlaylistId INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN TmdbId INTEGER;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN TmdbTitle TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN LastTmdbSync TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN Cast TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN Director TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN BackdropUrl TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN TrailerUrl TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN MetadataFetchedAt TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN GroupTitle TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN NetworkName TEXT;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Series ADD COLUMN NetworkLogoUrl TEXT;"); } catch { }

        // Invalidate TMDB cache for EU series (force English metadata re-fetch)
        try
        {
            context.Database.ExecuteSqlRaw(@"
                UPDATE Series
                SET Plot = NULL, Cast = NULL, BackdropUrl = NULL, TrailerUrl = NULL, ContentRating = NULL, MetadataFetchedAt = NULL
                WHERE GroupTitle LIKE 'EU %' OR GroupTitle LIKE 'EU|%' OR GroupTitle = 'EU'");
        }
        catch { }

        // ── Seasons ───────────────────────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Seasons ADD COLUMN TmdbSeasonId INTEGER;"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Seasons ADD COLUMN Plot TEXT;"); } catch { }

        // ── SeriesEpisodeProgresses ───────────────────────────────────────────
        try { context.Database.ExecuteSqlRaw("ALTER TABLE SeriesEpisodeProgresses ADD COLUMN TmdbId INTEGER;"); } catch { }

        try
        {
            context.Database.ExecuteSqlRaw(@"
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
            context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SeriesEpisodeProgresses_ProfileId ON SeriesEpisodeProgresses(ProfileId);");
            context.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_SeriesEpisodeProgresses_UniqueEpisode ON SeriesEpisodeProgresses(ProfileId, SeriesKey, SeasonNumber, EpisodeNumber);");
        }
        catch { }

        // ── DownloadItems ─────────────────────────────────────────────────────
        try
        {
            context.Database.ExecuteSqlRaw(@"
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
            context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DownloadItems_ProfileId ON DownloadItems(ProfileId);");
            context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DownloadItems_Status ON DownloadItems(Status);");
            context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DownloadItems_ProfileStatusCreated ON DownloadItems(ProfileId, Status, CreatedAt);");

            // Fix: Rename LocalEncryptedPath → LocalFilePath if upgrading from old schema
            try { context.Database.ExecuteSqlRaw("ALTER TABLE DownloadItems RENAME COLUMN LocalEncryptedPath TO LocalFilePath;"); } catch { }

            // Cleanup: Mark old encrypted files as failed/obsolete
            try
            {
                context.Database.ExecuteSqlRaw(
                    "UPDATE DownloadItems SET Status = 4, ErrorMessage = 'Eski format. Lütfen tekrar indirin.' " +
                    "WHERE LocalFilePath LIKE '%.nctra' AND Status = 3;");
            }
            catch { }
        }
        catch { }

        // ── SQLite Performance PRAGMAs ────────────────────────────────────────
        // foreign_keys: ensures Cascade Deletes work
        // WAL: concurrent reads during writes — UI stays responsive while importing
        try { context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;"); } catch { }
        try { context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;"); } catch { }
        try { context.Database.ExecuteSqlRaw("PRAGMA cache_size=-32000;"); } catch { } // 32MB cache (mobile-friendly)
        try { context.Database.ExecuteSqlRaw("PRAGMA temp_store=MEMORY;"); } catch { }
        try { context.Database.ExecuteSqlRaw("PRAGMA mmap_size=134217728;"); } catch { } // 128MB mmap (mobile-friendly)
    }

    // ── Language / Culture ───────────────────────────────────────────────────
    private static void ApplyApplicationLanguage(string? languageCode)
    {
        var normalized = (languageCode ?? "en").Trim().ToLowerInvariant();
        var cultureName = normalized switch
        {
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "tr" => "tr-TR",
            _ => "en-US"
        };

        var culture = new CultureInfo(cultureName);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    // ── Crash Handlers ───────────────────────────────────────────────────────
    private void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;

            var reportService = Services?.GetService(typeof(IDiagnosticReportService)) as IDiagnosticReportService;
            if (reportService is null) return;

            if (e.IsTerminating)
            {
                reportService.OpenCrashReport(ex, "Global (Terminating)");
            }
            else
            {
                Dispatcher.UIThread.Post(() => reportService.OpenCrashReport(ex, "Global"));
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            e.SetObserved(); // Prevent process kill on unobserved Task exceptions
        };
    }
}
