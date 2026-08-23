using System;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Android.DependencyInjection;
using Noctra.Android.Services;
using Noctra.Diagnostics;
using Noctra.Mobile.Services;
using Noctra.Mobile.Controls;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.Models;
using Noctra.Core.Services;

namespace Noctra.Android;

[Activity(
    Label = "Noctra",
    Theme = "@style/MyTheme.Splash",
    MainLauncher = true,
    // Repeated launcher taps/resume flows must bring the existing task forward
    // instead of allocating another Avalonia window and native surface.
    LaunchMode = LaunchMode.SingleTask,
    SupportsPictureInPicture = true,
    ResizeableActivity = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation |
                           ConfigChanges.ScreenSize |
                           ConfigChanges.SmallestScreenSize |
                           ConfigChanges.ScreenLayout |
                           ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // OnStop'ta bizim duraklattığımız oynatmayı OnStart'ta devam ettirmek için işaret.
    // Kullanıcının manuel duraklatmasını geri almamak adına yalnızca bu flag set ise resume edilir.
    private bool _pausedByLifecycle;
    private bool _playerOverlaySurfaceActive;
#if DEBUG
    private IDisposable? _performanceProbe;
#endif

    protected override void OnCreate(Bundle? savedInstanceState)
    {
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
#if DEBUG
            // Some Android vendors do not expose launch extras through Intent
            // until the base Activity has completed creation.
            ConfigurePerformanceProbe();
            var previewRequested =
                Intent?.GetBooleanExtra("noctra.ads.preview", false) == true ||
                Intent?.GetBooleanExtra("noctra_ads_preview", false) == true;
            if (previewRequested)
            {
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(FilesDir!.AbsolutePath, "ads_preview_enabled"),
                        "1");
                }
                catch
                {
                }
            }

            var previewPersisted = System.IO.File.Exists(
                System.IO.Path.Combine(FilesDir!.AbsolutePath, "ads_preview_enabled"));
            PreviewMobileAdvertisingService.DebugOverrideEnabled =
                previewRequested || previewPersisted;
            global::Android.Util.Log.Info("NoctraAds",
                $"noctra_ads_preview extra={previewRequested} persisted={previewPersisted}");
#endif
            PerformanceTrace.Mark("android.activity.create");
            ConfigureAvaloniaOverlaySurface();
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

        if (services?.GetService<MobileAdvertisingBootstrapper>() is { } bootstrapper)
        {
            _ = RunAdvertisingBootstrapSafelyAsync(bootstrapper);
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

        Window?.DecorView?.Post(() =>
        {
            PerformanceTrace.Mark("android.first_ui_turn");
            // İlk kare çizildi: ertelenen startup işleri (EPG sync, history
            // rayları...) artık koordinatör üzerinden ilerleyebilir.
            if (Avalonia.Application.Current is Noctra.Mobile.App app &&
                app.Services?.GetService<StartupWorkCoordinator>() is { } coordinator)
            {
                coordinator.MarkFirstFrameRendered();
            }
        });
    }

    private void ConfigureAvaloniaOverlaySurface(
        int remainingAttempts = 2,
        bool recreateLegacySurface = false)
    {
        var decorView = Window?.DecorView;
        var content = decorView?
            .FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        var surfaceView = FindSurfaceView(content);
        if (surfaceView is null)
        {
            if (remainingAttempts > 0 && decorView is not null)
            {
                decorView.Post(() => ConfigureAvaloniaOverlaySurface(
                    remainingAttempts - 1,
                    recreateLegacySurface));
            }

            return;
        }

        // Shell mode keeps Avalonia behind normal Android window content so the
        // native AdView can be visible. Player mode moves Avalonia above the
        // native video TextureView so controls remain visible over playback.
        surfaceView.SetZOrderOnTop(_playerOverlaySurfaceActive);
        var surfaceHolder = surfaceView.Holder;
        if (surfaceHolder is null)
        {
            return;
        }

        surfaceHolder.SetFormat(Format.Translucent);
        surfaceView.SetBackgroundColor(Color.Transparent);

        if (recreateLegacySurface && Build.VERSION.SdkInt < BuildVersionCodes.R)
        {
            RecreateAvaloniaSurfaceForLegacyZOrder(surfaceView);
        }
    }

    internal void SetAvaloniaPlayerOverlayActive(bool active)
    {
        var changed = _playerOverlaySurfaceActive != active;
        _playerOverlaySurfaceActive = active;
        ConfigureAvaloniaOverlaySurface(
            remainingAttempts: 2,
            recreateLegacySurface: changed);
        Log.Info("NoctraSurface", $"playerOverlayActive={active}");
    }

    private void RecreateAvaloniaSurfaceForLegacyZOrder(SurfaceView surfaceView)
    {
        if (surfaceView.Visibility != ViewStates.Visible)
        {
            return;
        }

        var decorView = Window?.DecorView;
        if (decorView is null)
        {
            return;
        }

        surfaceView.Visibility = ViewStates.Gone;
        void RestoreSurface()
        {
            surfaceView.Visibility = ViewStates.Visible;
            surfaceView.RequestLayout();
            surfaceView.Invalidate();
        }

        if (!decorView.Post(RestoreSurface))
        {
            RestoreSurface();
        }
    }

    private static SurfaceView? FindSurfaceView(View? view)
    {
        if (view is SurfaceView surfaceView)
        {
            return surfaceView;
        }

        if (view is not ViewGroup group)
        {
            return null;
        }

        for (var index = 0; index < group.ChildCount; index++)
        {
            if (FindSurfaceView(group.GetChildAt(index)) is { } childSurface)
            {
                return childSurface;
            }
        }

        return null;
    }

    private void SetAvaloniaSurfaceVisibilityForPictureInPicture(
        bool isInPictureInPictureMode)
    {
        var content = Window?.DecorView?
            .FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        var surfaceView = FindSurfaceView(content);
        if (surfaceView is null)
        {
            return;
        }

        // Android PiP captures the activity window. Avalonia's translucent
        // SurfaceView is useful for full-screen controls, but in PiP it can
        // contribute a stale UI buffer above the native video TextureView.
        // PiP uses Android's own controls, so expose the video window directly.
        surfaceView.Visibility = isInPictureInPictureMode
            ? ViewStates.Gone
            : ViewStates.Visible;

        if (!isInPictureInPictureMode)
        {
            ConfigureAvaloniaOverlaySurface();
            surfaceView.RequestLayout();
            surfaceView.Invalidate();
        }
    }

    internal Task PrepareVideoSurfaceForPictureInPictureAsync()
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            SetAvaloniaSurfaceVisibilityForPictureInPicture(
                isInPictureInPictureMode: true);

            var decorView = Window?.DecorView;
            if (decorView is null)
            {
                completion.TrySetResult(true);
                return;
            }

            decorView.RequestLayout();
            decorView.Invalidate();

            // PiP takes its source frame while EnterPictureInPictureMode runs.
            // Let Android commit two UI turns after removing Avalonia's overlay
            // surface so the source frame contains the native video, not a stale
            // Avalonia buffer.
            void CompleteAfterSecondUiTurn() => completion.TrySetResult(true);
            void QueueSecondUiTurn()
            {
                if (!decorView.Post(CompleteAfterSecondUiTurn))
                {
                    completion.TrySetResult(true);
                }
            }

            if (!decorView.Post(QueueSecondUiTurn))
            {
                completion.TrySetResult(true);
            }
        });

        return completion.Task;
    }

    internal void RestoreAvaloniaSurfaceAfterFailedPictureInPictureEntry()
    {
        RunOnUiThread(() =>
            SetAvaloniaSurfaceVisibilityForPictureInPicture(
                isInPictureInPictureMode: false));
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

#pragma warning disable CA1422 // Required fallback for Android API < 33.
        base.OnBackPressed();
#pragma warning restore CA1422
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        // Focus kazanıldığında immersive mode durumunu yeniden uygula.
        // Huawei gibi cihazlarda izin pencereleri, ses paneli veya ekran dönme sonrası
        // sistem çubukları geri gelebilir.
        if (hasFocus &&
            Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<IPlayerWindowService>() is AndroidPlayerWindowService windowService)
        {
            Window?.DecorView?.Post(() => windowService.ReapplyImmersiveMode());
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        PerformanceTrace.Mark("android.activity.resume");
        var resumeGeneration = MobileAppLifecycle.BeginResume();

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetService<AndroidActivityProvider>()?.SetCurrent(this);

            if (app.Services?.GetService<GooglePlayUpdateService>() is { } updateService)
            {
                _ = ResumeUpdateFlowSafelyAsync(updateService);
            }

            // Resume/focus sonrası Premium süresi yeniden kontrol edilir;
            // süre uygulama kapalıyken dolduysa UI burada güncellenir.
            if (app.Services?.GetService<ILicenseService>() is { } licenseService)
            {
                QueueLicenseRefresh(licenseService, resumeGeneration);
            }

            // Resume sonrasında immersive mode durumunu yeniden uygula.
            if (app.Services?.GetService<IPlayerWindowService>() is AndroidPlayerWindowService windowService)
            {
                Window?.DecorView?.Post(() => windowService.ReapplyImmersiveMode());
            }
        }

        QueueVisualTreeRecovery(resumeGeneration);
    }

    protected override void OnPause()
    {
        MobileAppLifecycle.NotifyPaused();
        base.OnPause();
    }

    public override void OnTrimMemory([GeneratedEnum] TrimMemory level)
    {
        base.OnTrimMemory(level);

        // UiHidden/RunningModerate bilgilendirme seviyesidir: kullanıcı her an
        // dönebilir, temizlenmiş image cache dönüşü ekrandaki her posterin
        // yeniden decode'uyla jank'a dönüşür. Yalnızca gerçek baskı
        // seviyeleri bu bedeli öder.
        if (level is TrimMemory.RunningLow or
            TrimMemory.RunningCritical or
            TrimMemory.Moderate or
            TrimMemory.Background or
            TrimMemory.Complete)
        {
            RemoteImage.TrimImageCaches();
        }

        PerformanceTrace.Mark("android.memory.trim", (long)level);
    }

    private void QueueVisualTreeRecovery(long resumeGeneration)
    {
        void NotifyVisualTree()
        {
            if (!MobileAppLifecycle.TryNotifyResumed(resumeGeneration))
            {
                return;
            }

            PerformanceTrace.Mark("android.activity.resume.visual_tree_recovery");
        }

        // OnResume itself is on the Android UI thread. Posting through DecorView lets
        // the surface/window transition enqueue first; the grid then performs its own
        // bounded loaded-priority retries until a stable width is available.
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

    private void QueueLicenseRefresh(ILicenseService licenseService, long resumeGeneration)
    {
        async void RefreshWhenSurfaceIsReady()
        {
            if (!MobileAppLifecycle.IsGenerationCurrent(resumeGeneration))
            {
                return;
            }

            try
            {
                await licenseService.RefreshSubscriptionStatusAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("Noctra", $"License resume refresh failed: {ex}");
            }
        }

        if (Window?.DecorView is { } decorView)
        {
            decorView.PostDelayed(RefreshWhenSurfaceIsReady, 500);
            return;
        }

        _ = Task.Delay(500).ContinueWith(
            _ => RefreshWhenSurfaceIsReady(),
            TaskScheduler.Default);
    }

    private static async Task RunAdvertisingBootstrapSafelyAsync(MobileAdvertisingBootstrapper bootstrapper)
    {
        try
        {
            await bootstrapper.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn("NoctraAds", $"Advertising bootstrap failed: {ex}");
        }
    }

    protected override void OnStop()
    {
        // PiP gerçek background playback yoludur; PiP modunda oynatma
        // Android tarafından yönetilir, burada dokunmuyoruz.
        // PiP dışında oynatma arka plana taşınmaz — sıfır tolerans:
        // Opening/Buffering de duraklatılır, yoksa arka planda buffer
        // dolunca oynatma kendiliğinden başlayabilir.
        if (!IsInPictureInPictureMode &&
            Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<IVideoPlayerService>() is { } player &&
            (player.IsPlaying ||
             player.State == PlaybackState.Opening ||
             player.State == PlaybackState.Buffering))
        {
            // Yalnızca gerçekten oynayan player geri dönüşte otomatik resume
            // edilir; paused-seek anındaki kısa Buffering penceresi resume
            // hakkı kazanmaz (kullanıcının manuel pause'u korunur).
            _pausedByLifecycle = player.IsPlaying;
            player.Pause();
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

        // Arka plana geçerken otomatik PiP yalnızca premium kullanıcılarda
        // tetiklenir; ücretsiz kullanıcıda OnStop video'yu duraklatır
        // (CanEnterPictureInPicture, MainView tarafında premium ile gated).
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
        SetAvaloniaSurfaceVisibilityForPictureInPicture(isInPictureInPictureMode);

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            if (isInPictureInPictureMode)
            {
                app.Services?
                    .GetService<AndroidVideoSurfaceService>()?
                    .SetBounds(0, 0, -1, -1);
            }

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
