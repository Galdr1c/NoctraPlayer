using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Android.DependencyInjection;
using Noctra.Android.Services;

namespace Noctra.Android;

[Activity(
    Label = "Noctra",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var applicationContext = ApplicationContext
            ?? throw new InvalidOperationException("Android application context is unavailable.");
        Noctra.Mobile.App.ServiceProviderFactory ??=
            () => applicationContext.CreateNoctraAndroidServiceProvider();

        base.OnCreate(savedInstanceState);

        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetRequiredService<AndroidActivityProvider>().SetCurrent(this);
        }
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

    protected override void OnDestroy()
    {
        if (Avalonia.Application.Current is Noctra.Mobile.App app)
        {
            app.Services?.GetService<AndroidActivityProvider>()?.Clear(this);
        }

        base.OnDestroy();
    }
}
