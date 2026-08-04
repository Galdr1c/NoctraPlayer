using System;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
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

    private void ConfigureAvaloniaOverlaySurface(int remainingAttempts = 2)
    {
        var decorView = Window?.DecorView;
        var content = decorView?
            .FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        var surfaceView = FindSurfaceView(content);
        if (surfaceView is null)
        {
            if (remainingAttempts > 0 && decorView is not null)
            {
                decorView.Post(() => ConfigureAvaloniaOverlaySurface(remainingAttempts - 1));
            }

            return;
        }

        // The native video TextureView is drawn into the activity window. Avalonia's
        // surface needs an alpha channel and must be composed above that window so
        // decoded video and player controls remain visible together.
        surfaceView.SetZOrderOnTop(true);
        surfaceView.Holder.SetFormat(Format.Translucent);
        surfaceView.SetBackgroundColor(Color.Transparent);
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

        base.OnBackPressed();
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

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetService<AndroidActivityProvider>()?.SetCurrent(this);

            if (app.Services?.GetService<GooglePlayUpdateService>() is { } updateService)
            {
                _ = ResumeUpdateFlowSafelyAsync(updateService);
            }

            // Resume sonrasında immersive mode durumunu yeniden uygula.
            if (app.Services?.GetService<IPlayerWindowService>() is AndroidPlayerWindowService windowService)
            {
                Window?.DecorView?.Post(() => windowService.ReapplyImmersiveMode());
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
