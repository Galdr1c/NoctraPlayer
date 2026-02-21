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

    public WatchHistoryService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false, TimeSpan? duration = null, TimeSpan? incrementDelta = null)
    {
        if (!channelId.HasValue && !episodeId.HasValue)
        {
            return;
        }

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
        history.StoppedAt = isCompletedNow && duration.HasValue ? duration.Value : position;
        history.WatchedAt = watchedAt;
        history.Completed = isCompletedNow;
        
        // Update total watched duration using the provided delta
        history.WatchedDuration += incrementDelta ?? TimeSpan.Zero;

        if (episodeId.HasValue)
        {
            var episode = await context.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s!.Series)
                .FirstOrDefaultAsync(e => e.Id == episodeId.Value);
            if (episode != null)
            {
                episode.LastWatched = history.WatchedAt;
                episode.WatchedPosition = history.StoppedAt;
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
                if (duration.HasValue && duration.Value.TotalSeconds > 0)
                {
                    channel.Duration = duration.Value;
                }
            }
        }

        await context.SaveChangesAsync();
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
        var seriesTitle = episode.Season?.Series?.Name;
        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            seriesTitle = episode.Name;
        }

        var seriesKey = SeriesProgressIdentity.NormalizeSeriesKey(seriesTitle);
        if (string.IsNullOrWhiteSpace(seriesKey))
        {
            return;
        }

        var (seasonNumber, episodeNumber) = SeriesProgressIdentity.ResolveSeasonEpisode(episode);
        var existing = await context.SeriesEpisodeProgresses
            .FirstOrDefaultAsync(p =>
                p.ProfileId == profileId &&
                p.SeriesKey == seriesKey &&
                p.SeasonNumber == seasonNumber &&
                p.EpisodeNumber == episodeNumber);

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

        if (duration.HasValue && duration.Value.TotalSeconds > 0)
        {
            existing.Duration = duration.Value;
        }
    }

    public async Task<List<WatchHistory>> GetHistoryAsync(int profileId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WatchHistories
            .Include(h => h.Channel)
            .Include(h => h.Episode)
            .Where(h => h.ProfileId == profileId)
            .OrderByDescending(h => h.WatchedAt)
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
            .Where(h => h.ProfileId == profileId && h.WatchedAt < cutoff)
            .ExecuteDeleteAsync();
    }
}

