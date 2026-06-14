using System;
using System.Net.Http;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Android.Services;
using Noctra.Core.DependencyInjection;
using Noctra.Core.Services;
using Noctra.Mobile.ViewModels;
using Noctra.Services;
using Noctra.Services.Interfaces;

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
        services.AddSingleton(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        });
        services.AddSingleton<IAppPathService, AndroidAppPathService>();
        services.AddSingleton<IDispatcherService, AndroidDispatcherService>();
        services.AddSingleton<INetworkService, AndroidNetworkService>();
        services.AddSingleton<ISecurityService, AndroidSecurityService>();
        services.AddSingleton<IAppEditionService, AppEditionService>();
        services.AddNoctraCoreServices();
        services.AddSingleton<ILicenseService>(serviceProvider =>
            new LicenseService(
                serviceProvider.GetRequiredService<IAppEditionService>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetRequiredService<ISecurityService>()));
        services.AddSingleton<MainViewModel>();

        return services;
    }

    public static IServiceProvider CreateNoctraAndroidServiceProvider(this Context context)
    {
        var services = new ServiceCollection();
        services.AddNoctraAndroidServices(context);
        return services.BuildServiceProvider();
    }
}
