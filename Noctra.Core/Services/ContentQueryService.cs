using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public sealed class ContentQueryService : IContentQueryService
{
    private readonly IPlaylistService _playlistService;
    private readonly IMediaService _mediaService;
    private readonly ISettingsService _settingsService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDatabaseWorkScheduler _databaseWorkScheduler;

    public ContentQueryService(
        IPlaylistService playlistService,
        IMediaService mediaService,
        ISettingsService settingsService,
        IDbContextFactory<AppDbContext> contextFactory,
        IDatabaseWorkScheduler databaseWorkScheduler)
    {
        _playlistService = playlistService;
        _mediaService = mediaService;
        _settingsService = settingsService;
        _contextFactory = contextFactory;
        _databaseWorkScheduler = databaseWorkScheduler;
    }

    public Task<(int TotalCount, List<string> AllGroups, List<string> LiveGroups, List<string> VodGroups, List<string> SeriesGroups)>
        GetChannelGroupMetadataAsync(
            int playlistId,
            CancellationToken cancellationToken = default)
        => RunQueryAsync(
            token => _playlistService.GetChannelGroupMetadataAsync(playlistId, token),
            cancellationToken);

    public Task<List<Channel>> GetChannelPageAsync(
        ContentPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var hiddenGroups = request.ApplyHiddenGroups
            ? GetHiddenGroups(request.Type)
            : null;

        return RunQueryAsync(
            token => _playlistService.GetChannelsFilteredPageAsync(
                request.PlaylistId,
                request.Skip,
                request.Take,
                request.SearchText,
                request.Group,
                request.Type,
                request.OnlyFavorites,
                request.SortOrder,
                hiddenGroups,
                token),
            cancellationToken);
    }

    public Task<List<Series>> GetSeriesListAsync(
        int playlistId,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(
            token => _mediaService.GetSeriesListAsync(playlistId, token),
            cancellationToken);

    public Task<List<int>> GetProfilePlaylistIdsAsync(
        int profileId,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(
            token => GetProfilePlaylistIdsCoreAsync(profileId, token),
            cancellationToken);

    private async Task<List<int>> GetProfilePlaylistIdsCoreAsync(
        int profileId,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var activeIds = await db.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.ProfileId == profileId && playlist.IsActive)
            .Select(playlist => playlist.Id)
            .ToListAsync(cancellationToken);

        if (activeIds.Count > 0)
        {
            return activeIds;
        }

        return await db.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.ProfileId == profileId)
            .Select(playlist => playlist.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Channel>> GetHistoryPageAsync(
        int profileId,
        IReadOnlyCollection<int> profilePlaylistIds,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
        => RunQueryAsync(
            token => GetHistoryPageCoreAsync(
                profileId,
                profilePlaylistIds,
                skip,
                take,
                token),
            cancellationToken);

    private async Task<List<Channel>> GetHistoryPageCoreAsync(
        int profileId,
        IReadOnlyCollection<int> profilePlaylistIds,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        if (profilePlaylistIds.Count == 0 || take <= 0)
        {
            return [];
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var histories = await db.WatchHistories
            .AsNoTracking()
            .Include(history => history.Channel)
            .Include(history => history.Episode)
                .ThenInclude(episode => episode!.Season)
                .ThenInclude(season => season!.Series)
            .Where(history => history.ProfileId == profileId &&
                ((history.ChannelId.HasValue &&
                  history.Channel != null &&
                  profilePlaylistIds.Contains(history.Channel.PlaylistId)) ||
                 (history.EpisodeId.HasValue &&
                  history.Episode != null &&
                  history.Episode.Season != null &&
                  history.Episode.Season.Series != null &&
                  profilePlaylistIds.Contains(history.Episode.Season.Series.PlaylistId))))
            .OrderByDescending(history => history.WatchedAt)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .ToListAsync(cancellationToken);

        var result = new List<Channel>(histories.Count);
        foreach (var history in histories)
        {
            var resolvedPosition = ResolveHistoryPosition(history.StoppedAt, history.WatchedDuration);
            if (history.Channel != null)
            {
                var channel = history.Channel;
                channel.LastWatched = history.WatchedAt;
                if (resolvedPosition.HasValue && resolvedPosition.Value > TimeSpan.Zero)
                {
                    channel.WatchedPosition = resolvedPosition.Value;
                }

                channel.Duration = ResolveHistoryDuration(channel.Duration, channel.WatchedPosition);
                result.Add(channel);
                continue;
            }

            if (history.Episode?.Season?.Series is not { } series)
            {
                continue;
            }

            var watchedPosition = resolvedPosition ?? history.Episode.WatchedPosition;
            result.Add(new Channel
            {
                Name = history.Episode.Name,
                StreamUrl = history.Episode.StreamUrl,
                LogoUrl = history.Episode.CoverUrl ?? series.CoverUrl,
                Type = ChannelType.Series,
                PlaylistId = series.PlaylistId,
                LastWatched = history.WatchedAt,
                WatchedPosition = watchedPosition,
                Duration = ResolveHistoryDuration(history.Episode.Duration, watchedPosition)
            });
        }

        return result;
    }

    private Task<T> RunQueryAsync<T>(
        Func<CancellationToken, Task<T>> query,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        // Microsoft.Data.Sqlite executes substantial portions of its async API
        // synchronously. The shared scheduler establishes a worker boundary and
        // bounds app-wide read pressure so navigation cannot create an unbounded
        // ThreadPool/SQLite backlog.
        return _databaseWorkScheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            query,
            cancellationToken);
    }

    private List<string> GetHiddenGroups(ChannelType? type)
    {
        var settings = _settingsService.Settings;
        return type switch
        {
            ChannelType.Live => settings.HiddenLiveGroups,
            ChannelType.VOD => settings.HiddenMovieGroups,
            ChannelType.Series => settings.HiddenSeriesGroups,
            _ => settings.HiddenLiveGroups
                .Concat(settings.HiddenMovieGroups)
                .Concat(settings.HiddenSeriesGroups)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static TimeSpan? ResolveHistoryDuration(TimeSpan? duration, TimeSpan? watchedPosition)
    {
        if (duration is { TotalSeconds: > 0 })
        {
            return duration;
        }

        return watchedPosition is { TotalSeconds: > 0 }
            ? watchedPosition.Value + TimeSpan.FromMinutes(30)
            : duration;
    }

    private static TimeSpan? ResolveHistoryPosition(TimeSpan stoppedAt, TimeSpan watchedDuration)
    {
        if (stoppedAt > TimeSpan.Zero)
        {
            return stoppedAt;
        }

        return watchedDuration > TimeSpan.Zero
            ? watchedDuration
            : null;
    }
}
