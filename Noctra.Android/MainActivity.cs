using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Util;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Android.DependencyInjection;
using Noctra.Android.Services;
using Noctra.Mobile.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Android;

[Activity(
    Label = "Noctra",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    SupportsPictureInPicture = true,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int PostNotificationsRequestCode = 1001;

    // OnStop'ta bizim duraklattığımız oynatmayı OnStart'ta devam ettirmek için işaret.
    // Kullanıcının manuel duraklatmasını geri almamak adına yalnızca bu flag set ise resume edilir.
    private bool _pausedByLifecycle;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
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

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetRequiredService<AndroidActivityProvider>().SetCurrent(this);
        }

        RequestNotificationPermission();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<AndroidFilePickerService>() is { } filePicker &&
            filePicker.TryHandleActivityResult(requestCode, resultCode, data))
        {
            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
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

    protected override void OnStop()
    {
        // PiP modunda oynatma Android tarafından yönetilir; burada dokunmuyoruz.
        // PiP değilken ve oynatıcı aktifken: arka plana geçerken duraklat.
        if (!IsInPictureInPictureMode &&
            Avalonia.Application.Current is Noctra.Mobile.App app &&
            app.Services?.GetService<IVideoPlayerService>() is { IsPlaying: true })
        {
            _pausedByLifecycle = true;
            app.Services.GetService<IVideoPlayerService>()?.Pause();
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
        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetService<AndroidActivityProvider>()?.Clear(this);
        }

        base.OnDestroy();
    }

    private void RequestNotificationPermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
            {
                RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, PostNotificationsRequestCode);
            }
        }
    }
}
