using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class WatchHistoryService : IWatchHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public WatchHistoryService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false, TimeSpan? duration = null, TimeSpan? incrementDelta = null)
    {
        if ((!channelId.HasValue && !episodeId.HasValue) || (channelId.HasValue && episodeId.HasValue))
        {
            return;
        }

        var lockKey = $"{profileId}_{channelId}_{episodeId}";
        var semaphore = _semaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var watchedAt = DateTime.UtcNow;

            using var context = await _contextFactory.CreateDbContextAsync();

            var history = await context.WatchHistories
                .FirstOrDefaultAsync(w => w.ProfileId == profileId &&
                                         (channelId.HasValue ? w.ChannelId == channelId : w.EpisodeId == episodeId));

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

            var isCompletedNow = history.Completed || completed;
            // Eğer zaten tamamlanmışsa ve bu oturumda henüz süre tespit edilememişse (fail load),
            // eski duruş noktasını (tüm süreyi) koru. 0'a çekme.
            if (isCompletedNow)
            {
                if (duration.HasValue)
                {
                    history.StoppedAt = duration.Value;
                }
                // else: history.StoppedAt'i olduğu gibi bırak (genelde eski duration'dır)
            }
            else
            {
                history.StoppedAt = position;
            }

            history.WatchedAt = watchedAt;
            history.Completed = isCompletedNow;

            // Update total watched duration using the provided delta
            var delta = incrementDelta ?? TimeSpan.Zero;
            if (delta < TimeSpan.Zero)
            {
                delta = TimeSpan.Zero;
            }
            history.WatchedDuration += delta;

            if (episodeId.HasValue)
            {
                var episode = await context.Episodes
                    .Include(e => e.Season)
                    .ThenInclude(s => s.Series)
                    .FirstOrDefaultAsync(e => e.Id == episodeId.Value);
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
                        watchedAt);
                }
            }
            else if (channelId.HasValue)
            {
                var channel = await context.Channels.FindAsync(channelId.Value);
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

            await context.SaveChangesAsync();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task UpsertSeriesProgressAsync(
        AppDbContext context,
        int profileId,
        Episode episode,
        TimeSpan stoppedAt,
        bool completed,
        TimeSpan? duration,
        DateTime watchedAt)
    {
        var series = episode.Season?.Series;
        var seriesTitle = series?.Name;

        var seriesKey = SeriesProgressIdentity.NormalizeSeriesKey(seriesTitle);
        if (string.IsNullOrWhiteSpace(seriesKey))
        {
            return;
        }

        var (seasonNumber, episodeNumber) = SeriesProgressIdentity.ResolveSeasonEpisode(episode);
        var tmdbId = series?.TmdbId;

        // Try to find by TmdbId first (Absolute match), fallback to SeriesKey if TmdbId is null or no record found
        var existing = await context.SeriesEpisodeProgresses
            .FirstOrDefaultAsync(p =>
                p.ProfileId == profileId &&
                p.SeasonNumber == seasonNumber &&
                p.EpisodeNumber == episodeNumber &&
                ((tmdbId.HasValue && p.TmdbId == tmdbId.Value) || p.SeriesKey == seriesKey));

        var isCompletedNow = existing?.Completed == true || completed;
        var finalStoppedAt = isCompletedNow && duration.HasValue
            ? duration.Value
            : stoppedAt;

        if (existing == null)
        {
            context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
            {
                ProfileId = profileId,
                SeriesKey = seriesKey,
                SeriesTitle = seriesTitle ?? string.Empty,
                TmdbId = tmdbId, // Save the TMDB ID!
                SeasonNumber = seasonNumber,
                EpisodeNumber = episodeNumber,
                LastWatchedAt = watchedAt,
                StoppedAt = finalStoppedAt,
                Duration = duration,
                Completed = isCompletedNow
            });
            return;
        }

        existing.SeriesTitle = string.IsNullOrWhiteSpace(existing.SeriesTitle)
            ? seriesTitle ?? string.Empty
            : existing.SeriesTitle;
        existing.LastWatchedAt = watchedAt;
        existing.StoppedAt = finalStoppedAt;
        existing.Completed = isCompletedNow;

        // Upgrade legacy SeriesKey progress to absolute TmdbId progress if available
        if (!existing.TmdbId.HasValue && tmdbId.HasValue)
        {
            existing.TmdbId = tmdbId;
        }

        if (duration.HasValue && duration.Value.TotalSeconds > 0)
        {
            existing.Duration = duration.Value;
        }
    }

    public async Task<List<WatchHistory>> GetHistoryAsync(int profileId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WatchHistories
            .AsNoTracking()
            .Include(h => h.Channel)
            .Include(h => h.Episode)
                .ThenInclude(e => e.Season)
                .ThenInclude(s => s.Series)
            .Where(h => h.ProfileId == profileId)
            .OrderByDescending(h => h.WatchedAt)
            .Take(100)
            .ToListAsync();
    }

    public async Task ClearHistoryAsync(int profileId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.WatchHistories
            .Where(h => h.ProfileId == profileId)
            .ExecuteDeleteAsync();
    }

    public async Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId)
    {
        if (!channelId.HasValue && !episodeId.HasValue)
        {
            return null;
        }

        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WatchHistories
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && 
                                     (channelId != null ? w.ChannelId == channelId : w.EpisodeId == episodeId));
    }

    public async Task CleanupOlderThanDaysAsync(int profileId, int days)
    {
        if (days <= 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);

        using var context = await _contextFactory.CreateDbContextAsync();
        await context.WatchHistories
            .Where(h => h.ProfileId == profileId && h.WatchedAt < cutoff && !h.Completed)
            .ExecuteDeleteAsync();

        await context.SeriesEpisodeProgresses
            .Where(p => p.ProfileId == profileId && p.LastWatchedAt < cutoff && !p.Completed)
            .ExecuteDeleteAsync();
    }
}

