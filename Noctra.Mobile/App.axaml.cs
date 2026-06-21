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
        Services = ServiceProviderFactory?.Invoke();
        if (Services?.GetService(typeof(ILocalizationService)) is ILocalizationService localization)
        {
            LocalizationSource.Instance.Initialize(localization);
        }

        // Inject mobile-specific promo code URL into settings
        if (Services?.GetService(typeof(ISettingsService)) is ISettingsService settingsService)
        {
            if (string.IsNullOrWhiteSpace(settingsService.Settings.PromoCodeConfigUrl))
            {
                settingsService.Settings.PromoCodeConfigUrl = Mobile.Services.MobileAppConfig.PromoCodesUrl;
            }
        }

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
        return Services?.GetService(typeof(MainViewModel)) as MainViewModel
            ?? new MainViewModel();
    }
}
