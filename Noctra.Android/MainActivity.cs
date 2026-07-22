using System;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Util;
using Android.Views;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Android.DependencyInjection;
using Noctra.Android.Services;
using Noctra.Diagnostics;
using Noctra.Mobile.Services;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.Models;

namespace Noctra.Android;

[Activity(
    Label = "Noctra",
    Theme = "@style/MyTheme.Splash",
    MainLauncher = true,
    SupportsPictureInPicture = true,
    ResizeableActivity = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // OnStop'ta bizim duraklattığımız oynatmayı OnStart'ta devam ettirmek için işaret.
    // Kullanıcının manuel duraklatmasını geri almamak adına yalnızca bu flag set ise resume edilir.
    private bool _pausedByLifecycle;
#if DEBUG
    private IDisposable? _performanceProbe;
#endif

    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        ConfigurePerformanceProbe();
#endif
        PerformanceTrace.Mark("android.activity.create");
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        var applicationContext = ApplicationContext
            ?? throw new InvalidOperationException("Android application context is unavailable.");
        Noctra.Mobile.App.ServiceProviderFactory ??=
            () => applicationContext.CreateNoctraAndroidServiceProvider();
        if (Avalonia.Application.Current is Noctra.Mobile.App existingApp)
        {
            existingApp.EnsureServices();
        }

        try
        {
            base.OnCreate(savedInstanceState);
        }
        catch (Exception ex)
        {
            Log.Error("Noctra", ex.ToString());
            throw;
        }

        IServiceProvider? services = null;
        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetRequiredService<AndroidActivityProvider>().SetCurrent(this);
            services = app.Services;
        }

#if DEBUG
        if (Intent?.GetBooleanExtra("noctra.performance.bootstrap", false) == true)
        {
            services ??= applicationContext.CreateNoctraAndroidServiceProvider();
            var bootstrapServices = services;
            _ = Task.Run(async () =>
            {
                try
                {
                    await BootstrapPerformanceProfileAsync(bootstrapServices).ConfigureAwait(false);
                    var root = FilesDir?.AbsolutePath
                        ?? throw new InvalidOperationException("Android files directory is unavailable.");
                    var readyPath = System.IO.Path.Combine(root, "performance", "bootstrap.ready");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(readyPath)!);
                    await System.IO.File.WriteAllTextAsync(readyPath, DateTime.UtcNow.ToString("O")).ConfigureAwait(false);
                    Log.Info("NoctraPerformance", "Benchmark profile bootstrap completed.");
                }
                catch (Exception ex)
                {
                    Log.Error("NoctraPerformance", ex.ToString());
                }
            });
        }
#endif

        Window?.DecorView?.Post(() => PerformanceTrace.Mark("android.first_ui_turn"));
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            if (app.Services?.GetService<GooglePlayUpdateService>() is { } updateService &&
                updateService.TryHandleActivityResult(requestCode, resultCode))
            {
                return;
            }

            if (app.Services?.GetService<AndroidFilePickerService>() is { } filePicker &&
                filePicker.TryHandleActivityResult(requestCode, resultCode, data))
            {
                return;
            }
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        if (Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<AndroidNotificationPermissionService>() is { } permissionService &&
            permissionService.TryHandleRequestPermissionsResult(requestCode, grantResults))
        {
            return;
        }

        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    /// <summary>
    /// Donanım/jest geri tuşu. Önce MainView'e (oynatıcı/EPG/alt sayfa) devredilir;
    /// olay uygulama içinde tüketilmezse varsayılan davranış (uygulamadan çıkış) uygulanır.
    /// </summary>
    public override void OnBackPressed()
    {
        if (Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<MobileBackNavigationService>() is { } backService &&
            backService.HandleBack())
        {
            return;
        }

        base.OnBackPressed();
    }

    protected override void OnResume()
    {
        base.OnResume();
        PerformanceTrace.Mark("android.activity.resume");

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetService<AndroidActivityProvider>()?.SetCurrent(this);

            if (app.Services?.GetService<GooglePlayUpdateService>() is { } updateService)
            {
                _ = ResumeUpdateFlowSafelyAsync(updateService);
            }
        }

        QueueVisualTreeRecovery();
    }

    private void QueueVisualTreeRecovery()
    {
        void NotifyVisualTree()
        {
            PerformanceTrace.Mark("android.activity.resume.visual_tree_recovery");
            MobileAppLifecycle.NotifyResumed();
        }

        // OnResume itself is on the Android UI thread. Posting through DecorView lets
        // the surface/window transition enqueue first; the grid then performs its own
        // bounded render-priority retries until a stable width is available.
        if (Window?.DecorView is { } decorView)
        {
            decorView.Post(NotifyVisualTree);
            return;
        }

        NotifyVisualTree();
    }

    private static async Task ResumeUpdateFlowSafelyAsync(GooglePlayUpdateService updateService)
    {
        try
        {
            await updateService.ResumeUpdateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn("Noctra", $"Update resume check failed: {ex}");
        }
    }

    protected override void OnStop()
    {
        // PiP modunda oynatma Android tarafından yönetilir; burada dokunmuyoruz.
        // AllowBackgroundPlayback aktifse ve PiP değilse bile oynatmayı devam ettir.
        // AllowBackgroundPlayback kapalıysa: arka plana geçerken duraklat.
        if (!IsInPictureInPictureMode &&
            Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<IVideoPlayerService>() is { IsPlaying: true })
        {
            var settings = app.Services?.GetService<ISettingsService>()?.Settings;
            if (settings is not { AllowBackgroundPlayback: true })
            {
                _pausedByLifecycle = true;
                app.Services.GetService<IVideoPlayerService>()?.Pause();
            }
        }

        base.OnStop();
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Yalnızca bizim OnStop'ta duraklattığımız oynatmayı devam ettir.
        // Kullanıcının manuel duraklatmasını geri almamak için _pausedByLifecycle kontrolü.
        if (_pausedByLifecycle &&
            !IsInPictureInPictureMode &&
            Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<IVideoPlayerService>() is { State: Noctra.Models.PlaybackState.Paused, HasLoadedMedia: true })
        {
            app.Services.GetService<IVideoPlayerService>()?.Resume();
        }

        _pausedByLifecycle = false;
    }

    protected override void OnUserLeaveHint()
    {
        base.OnUserLeaveHint();

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            _ = app.Services?
                .GetService<AndroidPictureInPictureService>()?
                .TryEnterAutoPictureInPictureAsync();
        }
    }

    public override void OnPictureInPictureModeChanged(
        bool isInPictureInPictureMode,
        Configuration? newConfig)
    {
        base.OnPictureInPictureModeChanged(isInPictureInPictureMode, newConfig);

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?
                .GetService<AndroidPictureInPictureService>()?
                .NotifyPictureInPictureModeChanged(isInPictureInPictureMode);
        }
    }

    protected override void OnDestroy()
    {
        PerformanceTrace.Mark("android.activity.destroy");
        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetService<AndroidActivityProvider>()?.Clear(this);
        }

        base.OnDestroy();

#if DEBUG
        if (_performanceProbe is not null)
        {
            PerformanceTrace.Probe = NullPerformanceProbe.Instance;
            _performanceProbe.Dispose();
            _performanceProbe = null;
        }
#endif
    }

#if DEBUG
    private void ConfigurePerformanceProbe()
    {
        if (Intent?.GetBooleanExtra("noctra.performance", false) != true)
        {
            return;
        }

        var runId = Intent.GetStringExtra("noctra.performance.run_id") ?? "manual";
        var safeRunId = new string(runId
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray());
        if (safeRunId.Length == 0)
        {
            safeRunId = "manual";
        }

        var root = FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android files directory is unavailable.");
        var path = System.IO.Path.Combine(root, "performance", $"{safeRunId}.jsonl");
        var probe = new JsonLinesPerformanceProbe(path);
        _performanceProbe = probe;
        PerformanceTrace.Probe = probe;
        PerformanceTrace.Mark("performance.run.start", scope: safeRunId);
    }

    private async Task BootstrapPerformanceProfileAsync(IServiceProvider? services)
    {
        if (services is null)
        {
            throw new InvalidOperationException("Application services are unavailable.");
        }

        var profileService = services.GetRequiredService<IProfileService>();
        var existing = await profileService.GetProfilesAsync();
        if (existing.Any(profile => string.Equals(
                profile.Name,
                "Noctra Performance",
                StringComparison.Ordinal)))
        {
            return;
        }

        var url = Intent?.GetStringExtra("noctra.performance.url")
            ?? "http://127.0.0.1:18765";
        var securityService = services.GetRequiredService<ISecurityService>();
        var saved = await profileService.SaveProfileAsync(new ProfileSaveRequest
        {
            ProfileName = "Noctra Performance",
            Avatar = "default",
            IsChild = false,
            Url = url,
            Username = "benchmark",
            EncryptedPassword = securityService.Encrypt("benchmark") ?? string.Empty,
            AccountType = ProfileType.XtreamCodes,
            CredentialsChanged = false,
            PinHash = null
        });

        if (saved is null)
        {
            throw new InvalidOperationException("Performance profile could not be created.");
        }
    }
#endif
}
