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
    private const int LightStatusBarsAppearance = 8;
    private const int LightNavigationBarsAppearance = 16;
    private const int LightSystemBarsAppearanceMask =
        LightStatusBarsAppearance | LightNavigationBarsAppearance;

    private readonly AndroidActivityProvider _activityProvider;

    // Parlaklık değeri cache'lenir; thread-güvenli senkron okuma için (swipe başlangıcı).
    private double _cachedBrightness = 0.5;

    // Immersive mode durumu; focus/resume sonrası yeniden uygulamak için.
    private bool _isImmersiveModeActive;
    private bool _isPlayerOverlayActive;
    private bool _isDarkTheme = true;

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

            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                ApplyImmersiveModern(window, fullScreen);
            }
            else
            {
                ApplyImmersiveLegacy(window, fullScreen);
            }

            ApplySystemChrome(activity);
        });
    }

    public void SetPlayerOverlayActive(bool active)
    {
        _isPlayerOverlayActive = active;
        RunOnUi(activity =>
        {
            if (activity is global::Noctra.Android.MainActivity mainActivity)
            {
                mainActivity.SetAvaloniaPlayerOverlayActive(active);
            }

            ApplySystemChrome(activity);
        });
    }

    public void SetSystemBarsTheme(bool isDarkTheme)
    {
        _isDarkTheme = isDarkTheme;
        RunOnUi(ApplySystemChrome);
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
        RunOnUi(activity =>
        {
            var window = activity.Window;
            if (window is null)
            {
                return;
            }

            if (_isImmersiveModeActive && OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                ApplyImmersiveModern(window, fullScreen: true);
            }
            else if (_isImmersiveModeActive)
            {
                ApplyImmersiveLegacy(window, fullScreen: true);
            }

            ApplySystemChrome(activity);
        });
    }

    private void ApplySystemChrome(Activity activity)
    {
        var window = activity.Window;
        if (window is null)
        {
            return;
        }

        var playerSurfaceActive = _isPlayerOverlayActive || _isImmersiveModeActive;
        var useLightShell = !playerSurfaceActive && !_isDarkTheme;
        var backdropColor = useLightShell
            ? global::Android.Graphics.Color.Rgb(250, 250, 250)
            : global::Android.Graphics.Color.Rgb(10, 10, 10);

        var content = window.DecorView?
            .FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        content?.SetBackgroundColor(backdropColor);

        // API 35+ forces transparent edge-to-edge system bars. On API 30-34,
        // explicitly match the selected Noctra shell backdrop.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            window.SetStatusBarColor(backdropColor);
            window.SetNavigationBarColor(backdropColor);
        }

        var useDarkIcons = useLightShell;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            window.InsetsController?.SetSystemBarsAppearance(
                useDarkIcons ? LightSystemBarsAppearanceMask : 0,
                LightSystemBarsAppearanceMask);
            return;
        }

#pragma warning disable CS0618 // API 29 and earlier compatibility path.
        var decorView = window.DecorView;
        if (decorView is null)
        {
            return;
        }

        var flags = (SystemUiFlags)decorView.SystemUiVisibility;
        flags = useDarkIcons
            ? flags | SystemUiFlags.LightStatusBar | SystemUiFlags.LightNavigationBar
            : flags & ~SystemUiFlags.LightStatusBar & ~SystemUiFlags.LightNavigationBar;
        decorView.SystemUiVisibility = (StatusBarVisibility)flags;
#pragma warning restore CS0618
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

    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private static void ApplyImmersiveModern(Window window, bool fullScreen)
    {
        // Android 15 (API 35) enforces edge-to-edge and deprecates this setter;
        // on older API 30-34 devices it is still required for immersive mode.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            window.SetDecorFitsSystemWindows(!fullScreen);
        }
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
