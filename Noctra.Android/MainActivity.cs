using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Noctra.Android.DependencyInjection;

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
    }
}
