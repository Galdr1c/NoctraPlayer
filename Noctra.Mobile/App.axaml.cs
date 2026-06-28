using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

        if (Services.GetService(typeof(ILocalizationService)) is ILocalizationService localization)
        {
            LocalizationSource.Instance.Initialize(localization);
        }

        // Apply the saved theme before creating or resolving views so
        // DynamicResource bindings resolve against the correct theme dictionary.
        if (Services.GetService(typeof(ISettingsService)) is ISettingsService settingsService)
        {
            if (Services.GetService(typeof(IThemeService)) is IThemeService themeService)
            {
                themeService.SetTheme(settingsService.Settings.IsDarkTheme);
            }

            if (string.IsNullOrWhiteSpace(settingsService.Settings.PromoCodeConfigUrl))
            {
                settingsService.Settings.PromoCodeConfigUrl = Mobile.Services.MobileAppConfig.PromoCodesUrl;
            }
        }

        return Services;
    }
}
