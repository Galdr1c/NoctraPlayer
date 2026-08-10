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
        // ILogger<T> bağımlılıkları Android/desktop release build'lerde de çözülebilsin.
        // Ekstra Console/Debug logging paketi çekmeden Warning+ seviyesini stderr'e yazar;
        // Android'de bu çıktı adb logcat tarafında mono/MonoStdio tag'iyle görünür.
        services.AddLogging(builder =>
        {
            builder.AddProvider(new ConsoleErrorLoggerProvider());
        });

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
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetService<ILogger<XtreamCodesService>>()));
        services.AddTransient<IStalkerPortalService>(serviceProvider =>
            new StalkerPortalService(
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetService<ILogger<StalkerPortalService>>()));
        services.AddTransient<ICacheService>(serviceProvider =>
            new CacheService(serviceProvider.GetRequiredService<IAppPathService>()));
        services.AddSingleton<IImportJobService, ImportJobService>();

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
                serviceProvider.GetRequiredService<ILocalizationService>(),
                serviceProvider.GetRequiredService<IImportJobService>(),
                serviceProvider.GetService<ILogger<PlaylistService>>()));
        services.AddSingleton<IPlaylistOrganizerService, PlaylistOrganizerService>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<IContentQueryService, ContentQueryService>();
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
                serviceProvider.GetRequiredService<IAppPathService>(),
                serviceProvider.GetService<INetworkService>()));
        services.AddSingleton<LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IPlatformActionService, DesktopPlatformActionService>();
        services.AddSingleton<IStorageInfoService, DesktopStorageInfoService>();
        services.AddSingleton<EpgSourceResolver>();
        services.AddSingleton<ITmdbSyncService, TmdbSyncService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IProfileAccessService, ProfileAccessService>();
        services.AddSingleton<IDatabaseSchemaFixupService, DatabaseSchemaFixupService>();
        services.AddSingleton<IProfilePinService, ProfilePinService>();
        services.AddSingleton<ReviewPromptFallbackHandler>();
        services.AddSingleton<ReviewPromptTracker>();

        return services;
    }
}

/// <summary>
/// Minimal ILoggerProvider: Warning/Error/Critical loglarını Console.Error'a yazar.
/// Android'de Console.Error adb logcat'te mono/MonoStdio tag'iyle görülebilir;
/// desktop'ta stderr'e düşer. Logging asla exception fırlatmamalıdır.
/// </summary>
internal sealed class ConsoleErrorLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ConsoleErrorLogger(categoryName);

    public void Dispose() { }

    private sealed class ConsoleErrorLogger : ILogger
    {
        private readonly string _categoryName;

        public ConsoleErrorLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            try
            {
                var level = logLevel switch
                {
                    LogLevel.Warning => "WARN",
                    LogLevel.Error => "ERROR",
                    LogLevel.Critical => "FATAL",
                    _ => logLevel.ToString().ToUpperInvariant()
                };

                var message = formatter(state, exception);
                var line = $"[Noctra:{level}] [{_categoryName}] {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                Console.Error.WriteLine(line);
            }
            catch
            {
                // Logging never throws.
            }
        }
    }
}
