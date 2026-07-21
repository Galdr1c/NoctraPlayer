using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Avalonia.Localization;
using Noctra.Avalonia.Services;
using Noctra.Core.DependencyInjection;
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
            StartupLogger.Log("========= NOCTRA DESKTOP INITIALIZE =========");
            RegisterCrashHandlers();
            StartupLogger.Log("Crash handlers registered");
            
            AvaloniaXamlLoader.Load(this);
            StartupLogger.Log("XAML loaded");

            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.MaxServicePointIdleTime = 1000;
            ServicePointManager.DnsRefreshTimeout = 120000;

            var services = new ServiceCollection();
            ConfigureServices(services);
            StartupLogger.Log("Services configured");
            
            Services = services.BuildServiceProvider();
            StartupLogger.Log("Service provider built");

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            StartupLogger.Log("Database ensured");
            
            // Schema fixups run later in the async warmup flow to avoid blocking startup.
            

            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var themeService = Services.GetRequiredService<IThemeService>();
            var localizationService = Services.GetRequiredService<ILocalizationService>();
            StartupLogger.Log("Core services retrieved");
            
            ApplyApplicationLanguage(settings.Settings.Language);
            themeService.SetTheme(settings.Settings.IsDarkTheme);
            localizationService.SetLanguage(settings.Settings.Language ?? "en");
            LocalizationSource.Instance.Initialize(localizationService);
            StartupLogger.Log("Theme and localization applied");

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
                    catch (Exception ex)
                    {
                        StartupLogger.LogError("SettingsChanged", ex);
                    }
                });
            };
            
            StartupLogger.Log("✅ Initialize completed successfully");
        }
        catch (Exception ex)
        {
            StartupLogger.LogError("Initialize", ex);
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
                        StartupLogger.Log("========= WARMUP SEQUENCE START =========");
                        
                        // 1. Warmup Settings (Lazy load trigger)
                        StartupLogger.Log("Step 1: Loading settings...");
                        var settingsService = Services.GetRequiredService<ISettingsService>();
                        _ = settingsService.Settings;
                        StartupLogger.Log("Step 1: ✅ Settings loaded");

                        // 2. Warmup EF Core (Triggers first-time model compilation)
                        StartupLogger.Log("Step 2: Warming up EF Core...");
                        using (var scope = Services.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            
                            StartupLogger.Log("Step 2a: Applying schema fixups...");
                            var schemaFixups = Services.GetRequiredService<IDatabaseSchemaFixupService>();
                            await schemaFixups.ApplyAsync(db, DatabaseSchemaFixupProfile.Desktop);
                            StartupLogger.Log("Step 2b: Checking profiles table...");
                            await db.Profiles.AnyAsync();
                            StartupLogger.Log("Step 2: ✅ EF Core ready");
                        }

                        // 2.1 Purge profiles with expired deletion countdown
                        StartupLogger.Log("Step 3: Purging expired profiles...");
                        try
                        {
                            var profileService = Services.GetRequiredService<IProfileService>();
                            await profileService.PurgeExpiredProfilesAsync();
                            StartupLogger.Log("Step 3: ✅ Profiles purged");
                        }
                        catch (Exception ex)
                        {
                            StartupLogger.LogError("Step 3 (profile purge)", ex);
                        }

                        // 2.5 TMDB Sync Service is now on-demand (no background processing)

                        // 3. Resolve MainWindow/ProfilesWindow early
                        StartupLogger.Log("Step 4: Creating ProfilesWindow...");
                        var profilesWindow = await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            var win = Services.GetRequiredService<ProfilesWindow>();
                            win.DisableAutoSelect = true;
                            StartupLogger.Log("Step 4: ✅ ProfilesWindow created on UI thread");
                            return win;
                        });
                        
                        StartupLogger.Log("Step 5: Checking legal consent...");
                        var canContinue = await Dispatcher.UIThread.InvokeAsync(async () =>
                            await ShowLegalConsentAsync(settingsService, splashWindow));
                        StartupLogger.Log($"Step 5: Legal consent = {canContinue}");
                        if (!canContinue)
                        {
                            StartupLogger.Log("User declined consent, shutting down...");
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                desktop.Shutdown();
                            });
                            return;
                        }

                        // Ensure a minimum splash duration (e.g., 1.5 seconds) for premium feel
                        var elapsed = startupStopwatch.ElapsedMilliseconds;
                        StartupLogger.Log($"Warmup completed in {elapsed}ms");
                        if (elapsed < 1500)
                        {
                            var waitTime = 1500 - (int)elapsed;
                            StartupLogger.Log($"Waiting {waitTime}ms for minimum splash...");
                            await Task.Delay(waitTime);
                        }

                        // Transition to Main Window
                        StartupLogger.Log("Step 7: Transitioning to main window...");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            desktop.MainWindow = profilesWindow;
                            profilesWindow.Show();
                            splashWindow.Close();
                            StartupLogger.Log("========= ✅ STARTUP COMPLETE =========");
                        });

                        // 8. Update check removed — platform-specific store updates only.
                        // Windows Store handles updates automatically.
                        // Debug/unpackaged builds: NoOpUpdateService (no popups).
                        StartupLogger.Log("Step 8: Update check skipped (store-managed)");
                    }
                    catch (Exception ex)
                    {
                        StartupLogger.LogError("WARMUP SEQUENCE", ex);
                        
                        // Fallback: Just try to open the app anyway if warmup fails
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                StartupLogger.Log("⚠️ Attempting fallback window creation...");
                                var win = Services.GetRequiredService<ProfilesWindow>();
                                win.DisableAutoSelect = true;
                                desktop.MainWindow = win;
                                win.Show();
                                splashWindow.Close();
                                StartupLogger.Log("⚠️ Fallback startup complete");
                            }
                            catch (Exception fallbackEx)
                            {
                                StartupLogger.LogError("FALLBACK", fallbackEx);
                            }
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
                                    StartupLogger.LogError("Shutdown clear history on exit", ex);
                                }
                            }).Wait();
                        }

                        var video = Services.GetService<IVideoPlayerService>();
                        video?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        StartupLogger.LogError("Shutdown cleanup", ex);
                    }

                    if (Services is IDisposable disposableServices)
                    {
                        try
                        {
                            disposableServices.Dispose();
                        }
                        catch (Exception ex)
                        {
                            StartupLogger.LogError("Service provider dispose", ex);
                        }
                    }

                };
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            StartupLogger.LogError("OnFrameworkInitializationCompleted", ex);
            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // HttpClient as Singleton: SocketsHttpHandler already manages connection pooling.
        // Transient would create new handler per resolution, defeating pooling and causing socket exhaustion.
        // PooledConnectionLifetime (5min) handles DNS rotation for long-lived instances.
        // The shared timeout prevents playlist/EPG/API requests from hanging forever;
        // streaming-style operations still pass their own CancellationToken where needed.
        services.AddSingleton(_ => CreateOptimizedHttpClient());
        services.AddSingleton<IAppPathService, DesktopAppPathService>();
        services.AddNoctraCoreServices();

        services.AddSingleton<IAppEditionService, AppEditionService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IPackageIdentityService, PackageIdentityService>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IAppVersionService>(sp => new DesktopAppVersionService(sp.GetService<IPackageIdentityService>()));
        services.AddSingleton<IAppUpdateService>(sp =>
        {
            var pkg = sp.GetService<IPackageIdentityService>();
            return pkg?.IsPackaged == true
                ? new MicrosoftStoreUpdateService(pkg)
                : new NoOpUpdateService();
        });

        services.AddSingleton<IDiagnosticReportService, DiagnosticReportService>();

        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IPlaylistFilePickerService>(sp =>
            new AvaloniaFilePickerService(sp.GetRequiredService<IAppPathService>()));
        services.AddSingleton<IReviewPromptService, ReviewPromptService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>(sp => 
            new VideoPlayerService(
                sp.GetRequiredService<IDispatcherService>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILocalizationService>()
            ));
        services.AddSingleton<ISecurityService, DesktopSecurityService>();
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
