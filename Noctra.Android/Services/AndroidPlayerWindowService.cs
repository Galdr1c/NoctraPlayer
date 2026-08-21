using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Oynatıcı için Android pencere davranışları:
/// ekranı uyanık tutma, tam ekran (yatay yön + immersive sistem çubukları) ve parlaklık.
/// Tüm pencere işlemleri UI thread üzerinde yürütülür.
/// </summary>
/// Tam ekranda kullanıcı yönü korunur; portre ve yatay kullanım desteklenir.
public sealed class AndroidPlayerWindowService : IPlayerWindowService
{
    private readonly AndroidActivityProvider _activityProvider;

    // Parlaklık değeri cache'lenir; thread-güvenli senkron okuma için (swipe başlangıcı).
    private double _cachedBrightness = 0.5;

    // Immersive mode durumu; focus/resume sonrası yeniden uygulamak için.
    private bool _isImmersiveModeActive;

    public AndroidPlayerWindowService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider;
    }

    public void SetKeepScreenOn(bool keepOn)
    {
        RunOnUi(activity =>
        {
            var window = activity.Window;
            if (window is null)
            {
                return;
            }

            if (keepOn)
            {
                window.AddFlags(WindowManagerFlags.KeepScreenOn);
            }
            else
            {
                window.ClearFlags(WindowManagerFlags.KeepScreenOn);
            }
        });
    }

    public void SetFullScreenMode(bool fullScreen)
    {
        _isImmersiveModeActive = fullScreen;

        RunOnUi(activity =>
        {
            // 1) Ekran yönü
            activity.RequestedOrientation = fullScreen
                ? ScreenOrientation.FullUser
                : ScreenOrientation.Unspecified;

            // 2) Immersive sistem çubukları
            var window = activity.Window;
            if (window is null)
            {
                return;
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                ApplyImmersiveModern(window, fullScreen);
            }
            else
            {
                ApplyImmersiveLegacy(window, fullScreen);
            }
        });
    }

    public void SetPlayerOverlayActive(bool active)
    {
        RunOnUi(activity =>
        {
            if (activity is global::Noctra.Android.MainActivity mainActivity)
            {
                mainActivity.SetAvaloniaPlayerOverlayActive(active);
            }
        });
    }

    public void SetBrightness(double brightness)
    {
        // Negatif -> sistem varsayılanı
        var value = brightness < 0 ? -1f : (float)Math.Clamp(brightness, 0.0, 1.0);
        _cachedBrightness = value < 0 ? 0.5 : value;

        RunOnUi(activity =>
        {
            var window = activity.Window;
            if (window?.Attributes is { } attrs)
            {
                attrs.ScreenBrightness = value;
                window.Attributes = attrs;
            }
        });
    }

    public double GetBrightness() => _cachedBrightness;

    /// <summary>
    /// Focus/resume sonrasında immersive mode durumunu yeniden uygular.
    /// Huawei gibi cihazlarda ekran dönme veya izin pencereleri sonrası sistem çubuklarını tekrar gizlemek için.
    /// </summary>
    public void ReapplyImmersiveMode()
    {
        if (!_isImmersiveModeActive)
        {
            return;
        }

        RunOnUi(activity =>
        {
            var window = activity.Window;
            if (window is null)
            {
                return;
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                ApplyImmersiveModern(window, fullScreen: true);
            }
            else
            {
                ApplyImmersiveLegacy(window, fullScreen: true);
            }
        });
    }

    private void RunOnUi(Action<Activity> action)
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(() =>
        {
            try
            {
                action(activity);
            }
            catch (Exception ex)
            {
                // Pencere durumu geçiş anında geçersiz olabilir.
                global::Android.Util.Log.Debug("Noctra.PlayerWindow", $"Window operation failed: {ex.Message}");
            }
        });
    }

    private static void ApplyImmersiveModern(Window window, bool fullScreen)
    {
        window.SetDecorFitsSystemWindows(!fullScreen);
        var controller = window.InsetsController;
        if (controller is null)
        {
            return;
        }

        if (fullScreen)
        {
            controller.Hide(WindowInsets.Type.SystemBars());
            controller.SystemBarsBehavior =
                (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
            controller.Show(WindowInsets.Type.SystemBars());
        }
    }

    private static void ApplyImmersiveLegacy(Window window, bool fullScreen)
    {
        var decorView = window.DecorView;
#pragma warning disable CS0618 // Eski API; R altı cihazlar için gerekli
        if (fullScreen)
        {
            decorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.LayoutStable
                | SystemUiFlags.LayoutHideNavigation
                | SystemUiFlags.LayoutFullscreen
                | SystemUiFlags.HideNavigation
                | SystemUiFlags.Fullscreen
                | SystemUiFlags.ImmersiveSticky);
        }
        else
        {
            decorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.Visible;
        }
#pragma warning restore CS0618
    }
}
