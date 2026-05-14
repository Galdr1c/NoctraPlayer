using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class WatchHistoryService : IWatchHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ISettingsService _settingsService;
    private static readonly SemaphoreSlim _syncLock = new(1, 1);

    public WatchHistoryService(IDbContextFactory<AppDbContext> contextFactory, ISettingsService settingsService)
    {
        _contextFactory = contextFactory;
        _settingsService = settingsService;
    }

    public async Task TrackWatchAsync(
        int profileId,
        int? channelId,
        int? episodeId,
        TimeSpan position,
        bool completed = false,
        TimeSpan? duration = null,
        TimeSpan? incrementDelta = null,
        bool allowReset = false,
        CancellationToken ct = default)
    {
        if (_settingsService?.Settings != null && !_settingsService.Settings.SaveWatchHistory)
        {
            return;
        }

        // Guard: At least one ID must be provided, but not both simultaneously (data integrity)
        if ((!channelId.HasValue && !episodeId.HasValue) || (channelId.HasValue && episodeId.HasValue))
        {
            return;
        }

        var watchedAt = DateTime.UtcNow;

        // Ensure negative deltas (due to system clock shifts) don't corrupt duration
        var safeIncrementDelta = incrementDelta.HasValue && incrementDelta.Value > TimeSpan.Zero 
            ? incrementDelta.Value 
            : TimeSpan.Zero;

        // Use a lock to prevent concurrent upserts from creating duplicate records (Race Condition #1)
        await _syncLock.WaitAsync(ct);
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);

            var history = await context.WatchHistories
                .FirstOrDefaultAsync(w => w.ProfileId == profileId && 
                                         (channelId.HasValue ? w.ChannelId == channelId : w.EpisodeId == episodeId), ct);

            if (history == null)
            {
                history = new WatchHistory
                {
                    ProfileId = profileId,
                    ChannelId = channelId,
                    EpisodeId = episodeId,
                    WatchedAt = watchedAt
                };
                context.WatchHistories.Add(history);
            }

            // If we are starting over, allow resetting the completed status and position
            var isCompletedNow = allowReset ? completed : (history.Completed || completed);
            
            if (isCompletedNow)
            {
                if (duration.HasValue)
                {
                    history.StoppedAt = duration.Value;
                }
            }
            else
            {
                history.StoppedAt = position;
            }

            history.WatchedAt = watchedAt;
            history.Completed = isCompletedNow;
            history.WatchedDuration += safeIncrementDelta;

            if (episodeId.HasValue)
            {
                var episode = await context.Episodes
                    .Include(e => e.Season)
                    .ThenInclude(s => s!.Series)
                    .FirstOrDefaultAsync(e => e.Id == episodeId.Value, ct);
                if (episode != null)
                {
                    episode.LastWatched = history.WatchedAt;
                    episode.WatchedPosition = history.StoppedAt;
                    episode.IsCompleted = history.Completed;
                    if (duration.HasValue && duration.Value.TotalSeconds > 0)
                    {
                        episode.Duration = duration.Value;
                    }

                    await UpsertSeriesProgressAsync(
                        context,
                        profileId,
                        episode,
                        history.StoppedAt,
                        history.Completed,
                        duration,
                        watchedAt,
                        allowReset,
                        ct);
                }
            }
            else if (channelId.HasValue)
            {
                var channel = await context.Channels.FindAsync(new object[] { channelId.Value }, ct);
                if (channel != null)
                {
                    channel.LastWatched = history.WatchedAt;
                    channel.WatchedPosition = history.StoppedAt;
                    channel.IsCompleted = history.Completed;
                    if (duration.HasValue && duration.Value.TotalSeconds > 0)
                    {
                        channel.Duration = duration.Value;
                    }
                }
            }

            await context.SaveChangesAsync(ct);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task UpsertSeriesProgressAsync(
        AppDbContext context,
        int profileId,
        Episode episode,
        TimeSpan stoppedAt,
        bool completed,
        TimeSpan? duration,
        DateTime watchedAt,
        bool allowReset,
        CancellationToken ct)
    {
        var series = episode.Season?.Series;
        var seriesTitle = series?.Name ?? episode.BaseDisplayName; 
        
        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            return; 
        }

        var seriesKey = SeriesProgressIdentity.NormalizeSeriesKey(seriesTitle);
        if (string.IsNullOrWhiteSpace(seriesKey))
        {
            return;
        }

        var (seasonNumber, episodeNumber) = SeriesProgressIdentity.ResolveSeasonEpisode(episode);
        var tmdbId = series?.TmdbId;

        // Try to find existing record. Unique index protects DB, logic here prevents double-inserts in same transaction.
        var existing = await context.SeriesEpisodeProgresses
            .FirstOrDefaultAsync(p =>
                p.ProfileId == profileId &&
                p.SeasonNumber == seasonNumber &&
                p.EpisodeNumber == episodeNumber &&
                ((tmdbId.HasValue && p.TmdbId == tmdbId.Value) || p.SeriesKey == seriesKey), ct);

        // If we are starting over, allow resetting the completed status and position
        var isCompletedNow = allowReset ? completed : ((existing?.Completed ?? false) || completed);
        
        var finalStoppedAt = isCompletedNow && duration.HasValue
            ? duration.Value
            : stoppedAt;

        if (existing == null)
        {
            context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
            {
                ProfileId = profileId,
                SeriesKey = seriesKey,
                SeriesTitle = seriesTitle,
                TmdbId = tmdbId,
                SeasonNumber = seasonNumber,
                EpisodeNumber = episodeNumber,
                LastWatchedAt = watchedAt,
                StoppedAt = finalStoppedAt,
                Duration = duration,
                Completed = isCompletedNow
            });
            return;
        }

        existing.SeriesTitle = string.IsNullOrWhiteSpace(existing.SeriesTitle) ? seriesTitle : existing.SeriesTitle;
        existing.LastWatchedAt = watchedAt;
        existing.StoppedAt = finalStoppedAt;
        existing.Completed = isCompletedNow;

        if (!existing.TmdbId.HasValue && tmdbId.HasValue)
        {
            existing.TmdbId = tmdbId;
        }

        if (duration.HasValue && duration.Value.TotalSeconds > 0)
        {
            existing.Duration = duration.Value;
        }
    }

    public async Task<List<WatchHistory>> GetHistoryAsync(int profileId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.WatchHistories
            .AsNoTracking() // Performance improvement (#10)
            .Include(h => h.Channel)
            .Include(h => h.Episode)
                .ThenInclude(e => e!.Season)
                .ThenInclude(s => s!.Series) // Support deep displays in HistoryView (#12)
            .Where(h => h.ProfileId == profileId)
            .OrderByDescending(h => h.WatchedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task DeleteProfileHistoryAsync(int profileId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        
        // Delete watch history
        await context.WatchHistories
            .Where(h => h.ProfileId == profileId)
            .ExecuteDeleteAsync(ct);

        // Delete series progress
        await context.SeriesEpisodeProgresses
            .Where(p => p.ProfileId == profileId)
            .ExecuteDeleteAsync(ct);

        // Reset channel progress
        await context.Channels
            .Where(c => c.Playlist != null && c.Playlist.ProfileId == profileId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.LastWatched, (DateTime?)null)
                                      .SetProperty(x => x.WatchedPosition, TimeSpan.Zero)
                                      .SetProperty(x => x.IsCompleted, false), ct);

        // Reset episode progress
        await context.Episodes
            .Where(e => e.Season != null &&
                        e.Season.Series != null &&
                        e.Season.Series.Playlist != null &&
                        e.Season.Series.Playlist.ProfileId == profileId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.LastWatched, (DateTime?)null)
                                      .SetProperty(x => x.WatchedPosition, TimeSpan.Zero)
                                      .SetProperty(x => x.IsCompleted, false), ct);
    }

    public async Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default)
    {
        if ((!channelId.HasValue && !episodeId.HasValue) || (channelId.HasValue && episodeId.HasValue))
        {
            return null;
        }

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.WatchHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && 
                                     (channelId.HasValue ? w.ChannelId == channelId : w.EpisodeId == episodeId), ct);
    }

    public async Task CleanupOlderThanDaysAsync(int profileId, int days, CancellationToken ct = default)
    {
        if (days <= 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        
        // Fix: Do not delete completed content (Resume point preservation) (#3)
        await context.WatchHistories
            .Where(h => h.ProfileId == profileId && h.WatchedAt < cutoff && !h.Completed)
            .ExecuteDeleteAsync(ct);

        // Fix: Also cleanup old series progress to prevent table bloat (#4)
        await context.SeriesEpisodeProgresses
            .Where(p => p.ProfileId == profileId && p.LastWatchedAt < cutoff && !p.Completed)
            .ExecuteDeleteAsync(ct);
    }

    public async Task RemoveFromHistoryAsync(int profileId, int? channelId, int? seriesId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);

        if (channelId.HasValue)
        {
            await context.WatchHistories
                .Where(h => h.ProfileId == profileId && h.ChannelId == channelId.Value)
                .ExecuteDeleteAsync(ct);

            await context.Channels
                .Where(c => c.Id == channelId.Value)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.LastWatched, (DateTime?)null)
                                          .SetProperty(x => x.WatchedPosition, TimeSpan.Zero)
                                          .SetProperty(x => x.IsCompleted, false), ct);
        }
        else if (seriesId.HasValue)
        {
            var episodeIds = await context.Episodes
                .Where(e => e.Season != null && e.Season.SeriesId == seriesId.Value)
                .Select(e => e.Id)
                .ToListAsync(ct);

            if (episodeIds.Count > 0)
            {
                await context.WatchHistories
                    .Where(h => h.ProfileId == profileId && h.EpisodeId.HasValue && episodeIds.Contains(h.EpisodeId.Value))
                    .ExecuteDeleteAsync(ct);

                await context.Episodes
                    .Where(e => episodeIds.Contains(e.Id))
                    .ExecuteUpdateAsync(e => e.SetProperty(x => x.LastWatched, (DateTime?)null)
                                              .SetProperty(x => x.WatchedPosition, TimeSpan.Zero)
                                              .SetProperty(x => x.IsCompleted, false), ct);

                var series = await context.Series.FindAsync(new object[] { seriesId.Value }, ct);
                if (series != null)
                {
                    var title = series.Name;
                    var tmdbId = series.TmdbId;
                    
                    var query = context.SeriesEpisodeProgresses.Where(p => p.ProfileId == profileId);
                    
                    var seriesKey = SeriesProgressIdentity.NormalizeSeriesKey(title);
                    if (!string.IsNullOrEmpty(seriesKey))
                    {
                         await query.Where(p => (tmdbId.HasValue && p.TmdbId == tmdbId) || p.SeriesKey == seriesKey)
                                   .ExecuteDeleteAsync(ct);
                    }
                }
            }
        }
    }
}

