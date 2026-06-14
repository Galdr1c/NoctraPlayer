using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNoctraCoreServices(this IServiceCollection services)
    {
        services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
        {
            var appPaths = serviceProvider.GetRequiredService<IAppPathService>();
            appPaths.EnsureUserDataDirectory();
            options.UseSqlite($"Data Source={appPaths.DatabasePath}");
        });

        services.AddTransient<IM3UParser, M3UParser>();
        services.AddSingleton<IEpgService>(serviceProvider =>
            new EpgService(
                serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetRequiredService<LanguageDetectionService>(),
                serviceProvider.GetService<ILogger<EpgService>>()));
        services.AddTransient<IMetadataService, MetadataService>();
        services.AddSingleton<IXtreamCodesService>(serviceProvider =>
            new XtreamCodesService(
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILocalizationService>()));
        services.AddTransient<IStalkerPortalService>(serviceProvider =>
            new StalkerPortalService(
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILocalizationService>()));
        services.AddTransient<ICacheService>(serviceProvider =>
            new CacheService(serviceProvider.GetRequiredService<IAppPathService>()));

        services.AddSingleton<IPlaylistService>(serviceProvider =>
            new PlaylistService(
                serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                serviceProvider.GetRequiredService<IM3UParser>(),
                serviceProvider.GetRequiredService<IMediaService>(),
                serviceProvider.GetRequiredService<IPlaylistOrganizerService>(),
                serviceProvider.GetRequiredService<LanguageDetectionService>(),
                serviceProvider.GetRequiredService<EpgSourceResolver>(),
                serviceProvider.GetRequiredService<IEpgService>(),
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<IPlaylistOrganizerService, PlaylistOrganizerService>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<IChannelService, ChannelService>();
        services.AddSingleton<IWatchHistoryService, WatchHistoryService>();
        services.AddSingleton<IAvatarService, AvatarService>();
        services.AddSingleton<ISettingsService>(serviceProvider =>
            new SettingsService(serviceProvider.GetRequiredService<IAppPathService>()));
        services.AddSingleton<IContentDownloadService>(serviceProvider =>
            new ContentDownloadService(
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetService<ILogger<ContentDownloadService>>(),
                serviceProvider.GetRequiredService<IAppPathService>()));
        services.AddSingleton<LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<EpgSourceResolver>();
        services.AddSingleton<ITmdbSyncService, TmdbSyncService>();
        services.AddSingleton<IProfileService, ProfileService>();

        return services;
    }
}
