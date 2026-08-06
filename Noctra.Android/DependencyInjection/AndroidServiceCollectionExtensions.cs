using System;
using System.Net;
using System.Net.Http;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Android.Services;
using Noctra.Core.DependencyInjection;
using Noctra.Core.Services;
using Noctra.Mobile.Services;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using AddProfileViewModel = Noctra.ViewModels.AddProfileViewModel;
using CoreMainViewModel = Noctra.ViewModels.MainViewModel;
using MobileMainViewModel = Noctra.Mobile.ViewModels.MainViewModel;

namespace Noctra.Android.DependencyInjection;

public static class AndroidServiceCollectionExtensions
{
    public static IServiceCollection AddNoctraAndroidServices(
        this IServiceCollection services,
        Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applicationContext = context.ApplicationContext ?? context;
        services.AddSingleton(applicationContext);
        services.AddSingleton(_ => CreateOptimizedHttpClient());
        services.AddSingleton<IAppPathService, AndroidAppPathService>();
        services.AddSingleton<IDispatcherService, AndroidDispatcherService>();
        services.AddSingleton<INetworkService, AndroidNetworkService>();
        services.AddSingleton<ISecurityService, AndroidSecurityService>();
        services.AddSingleton<AndroidActivityProvider>();
        services.AddSingleton<AndroidNotificationPermissionService>();
        services.AddSingleton<AndroidFilePickerService>();
        services.AddSingleton<IPlaylistFilePickerService>(serviceProvider =>
            serviceProvider.GetRequiredService<AndroidFilePickerService>());
        services.AddSingleton<IDialogService, AndroidDialogService>();
        services.AddSingleton<IAppEditionService, AppEditionService>();
        services.AddNoctraCoreServices();
        services.AddSingleton<IAppVersionService, AndroidAppVersionService>();
        services.AddSingleton<GooglePlayUpdateService>();
        services.AddSingleton<IAppUpdateService>(sp => sp.GetRequiredService<GooglePlayUpdateService>());
        services.AddSingleton<IPlatformActionService, AndroidPlatformActionService>();
        services.AddSingleton<IStorePurchaseService, AndroidStorePurchaseService>();
        services.AddSingleton<IStorageInfoService>(serviceProvider =>
            new AndroidStorageInfoService(serviceProvider.GetRequiredService<Context>()));
        services.AddSingleton<IThemeService, AndroidThemeService>();
        services.AddSingleton<IDiagnosticReportService, AndroidDiagnosticReportService>();
        services.AddSingleton<AndroidVideoSurfaceService>();
        services.AddSingleton<IVideoSurfaceService>(serviceProvider =>
            serviceProvider.GetRequiredService<AndroidVideoSurfaceService>());
        services.AddSingleton<AndroidPictureInPictureService>();
        services.AddSingleton<IPictureInPictureService>(serviceProvider =>
            serviceProvider.GetRequiredService<AndroidPictureInPictureService>());
        services.AddSingleton<AndroidPlayerWindowService>();
        services.AddSingleton<IPlayerWindowService>(serviceProvider =>
            serviceProvider.GetRequiredService<AndroidPlayerWindowService>());
        services.AddSingleton<MobileBackNavigationService>();
        services.AddSingleton<AndroidVideoPlayerService>();
        services.AddSingleton<IVideoPlayerService>(serviceProvider =>
            serviceProvider.GetRequiredService<AndroidVideoPlayerService>());
        services.AddSingleton<ILicenseService>(serviceProvider =>
            new LicenseService(
                serviceProvider.GetRequiredService<IAppEditionService>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetRequiredService<ISecurityService>(),
                serviceProvider.GetRequiredService<IPlatformActionService>(),
                serviceProvider.GetRequiredService<IStorePurchaseService>())); 
        services.AddTransient<WatermarkViewModel>();
        services.AddSingleton<CoreMainViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MobileViewModelResolver>();
        services.AddSingleton<MobilePlatformServiceResolver>();
        services.AddSingleton<MobileMainViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddTransient<AddProfileViewModel>();
        services.AddTransient<AvatarPickerViewModel>();
        services.AddTransient<ProfileLoadingViewModel>();
        services.AddSingleton<IReviewPromptService, AndroidReviewPromptService>();

        return services;
    }

    private static HttpClient CreateOptimizedHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        var client = new HttpClient(handler)
        {
            // Large M3U/Xtream/Stalker responses can legitimately take longer than 30s on Android tablets.
            // Desktop already uses a 3-minute shared timeout, so keep mobile aligned with desktop.
            Timeout = TimeSpan.FromMinutes(3)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 12) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36 Noctra/Android");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    public static IServiceProvider CreateNoctraAndroidServiceProvider(this Context context)
    {
        var services = new ServiceCollection();
        services.AddNoctraAndroidServices(context);
        return services.BuildServiceProvider();
    }
}
