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
                if (Services.GetService(typeof(IDatabaseSchemaFixupService)) is IDatabaseSchemaFixupService schemaFixups)
                {
                    schemaFixups.ApplyAsync(db, DatabaseSchemaFixupProfile.Mobile).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mobile.App] Database initialization/schema fixup failed: {ex}");
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mobile.App] Failed to apply settings change: {ex.Message}");
                    }
                });
            };
        }

        // ── Purge expired profiles (fire-and-forget) ─────────────────────────
        if (Services.GetService(typeof(IProfileService)) is IProfileService profileService)
        {
            _ = Task.Run(async () =>
            {
                try { await profileService.PurgeExpiredProfilesAsync(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mobile.App] Failed to purge expired profiles: {ex.Message}");
                }
            });
        }

        return Services;
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
