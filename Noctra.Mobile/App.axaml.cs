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
using Noctra.Diagnostics;
using Noctra.Mobile.Localization;
using Noctra.Mobile.ViewModels;
using Noctra.Mobile.Views;
using Noctra.Services;
using Noctra.Services.Interfaces;

using Noctra.Mobile.Services;
namespace Noctra.Mobile;

public partial class App : Application
{
    public static Func<IServiceProvider>? ServiceProviderFactory { get; set; }

    public IServiceProvider? Services { get; private set; }

    private Task? _databaseInitializationTask;

    /// <summary>
    /// Completes when the one-time database creation/schema maintenance has
    /// finished.  The task is intentionally started in the background so the
    /// Android activity can attach its first visual tree without waiting for
    /// SQLite maintenance.
    /// </summary>
    public Task DatabaseInitializationTask =>
        Volatile.Read(ref _databaseInitializationTask) ?? Task.CompletedTask;

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

        // Device sınıfını başlat (responsive layout kararları için).
        // MainView.SizeChanged ile runtime'da güncellenir.
        DeviceMetricsService.Instance.InitializeFromTopLevel();

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
            var schemaFixups = Services.GetService(typeof(IDatabaseSchemaFixupService)) as IDatabaseSchemaFixupService;
            _databaseInitializationTask = InitializeDatabaseAsync(
                dbContextFactory,
                schemaFixups,
                Services.GetService(typeof(ISettingsService)) as ISettingsService,
                Services.GetService(typeof(IProfileService)) as IProfileService);
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mobile.App] Failed to apply settings change: {ex.Message}");
                    }
                });
            };
        }

        return Services;
    }

    private static Task InitializeDatabaseAsync(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IDatabaseSchemaFixupService? schemaFixups,
        ISettingsService? settingsService,
        IProfileService? profileService = null)
    {
        return Task.Run(async () =>
        {
            try
            {
                PerformanceTrace.Mark("app.db.init.start");
                await using var db = await dbContextFactory
                    .CreateDbContextAsync()
                    .ConfigureAwait(false);
                await db.Database.EnsureCreatedAsync().ConfigureAwait(false);

                if (schemaFixups is not null)
                {
                    var resetPinCount = await schemaFixups
                        .ApplyAsync(db, DatabaseSchemaFixupProfile.Mobile)
                        .ConfigureAwait(false);

                    // Geçersiz PIN kayıtları (eski PBKDF2/legacy veya bozuk PIN2)
                    // sıfırlandı — profil listesi açılırken bir defalık bilgi
                    // gösterilir (Seçenek A).
                    if (resetPinCount > 0 && settingsService is not null)
                    {
                        settingsService.Settings.PinSystemResetNoticePending = true;
                        await settingsService.SaveAsync().ConfigureAwait(false);
                    }

                }

                // Bakım işlemleri burada TEK zincirde sıralı çalışır — ayrı bir
                // fire-and-forget purge yoktur; eşzamanlı iki silme işleminin
                // aynı profile dokunması (SQLite locked / yarış) engellenir.
                if (profileService is not null)
                {
                    // 1) Üç günlük silme süresi dolmuş bekleyen profilleri temizle.
                    // Kendi try/catch'i: geçici bir hata çocuk profili migration'ını
                    // engellememeli (bir sonraki açılışta purge tekrar denenir).
                    try
                    {
                        await profileService
                            .PurgeExpiredProfilesAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception purgeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mobile.App] Failed to purge expired profiles: {purgeEx.Message}");
                    }

                    // 2) Çocuk profili özelliği kaldırıldı — eski çocuk profilleri
                    // (verileriyle birlikte) silinir; sahibine bir defalık bilgi
                    // gösterilir.
                    var deletedChildProfiles = await profileService
                        .DeleteChildProfilesAsync()
                        .ConfigureAwait(false);

                    if (deletedChildProfiles > 0 && settingsService is not null)
                    {
                        settingsService.Settings.ChildModeRemovedNoticePending = true;
                        await settingsService.SaveAsync().ConfigureAwait(false);
                    }
                }

                PerformanceTrace.Mark("app.db.init.end");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mobile.App] Database initialization/schema fixup failed: {ex}");
            }
        });
    }

    // ── Schema Fixups ────────────────────────────────────────────────────────
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
