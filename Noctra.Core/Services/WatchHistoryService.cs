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
    private readonly AppDbContext _context;

    public WatchHistoryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false, TimeSpan? duration = null)
    {
        if (!channelId.HasValue && !episodeId.HasValue)
        {
            return;
        }

        var watchedAt = DateTime.Now;

        var history = await _context.WatchHistories
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
            _context.WatchHistories.Add(history);
        }

        var isCompletedNow = history.Completed || completed;
        history.StoppedAt = isCompletedNow && duration.HasValue ? duration.Value : position;
        history.WatchedAt = watchedAt;
        history.Completed = isCompletedNow;
        
        // Update total watched duration (approximate increment)
        history.WatchedDuration += TimeSpan.FromSeconds(5);

        if (episodeId.HasValue)
        {
            var episode = await _context.Episodes
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
            var channel = await _context.Channels.FindAsync(channelId.Value);
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

        await _context.SaveChangesAsync();
    }

    private async Task UpsertSeriesProgressAsync(
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
        var existing = await _context.SeriesEpisodeProgresses
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
            _context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
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
        return await _context.WatchHistories
            .Include(h => h.Channel)
            .Include(h => h.Episode)
            .Where(h => h.ProfileId == profileId)
            .OrderByDescending(h => h.WatchedAt)
            .ToListAsync();
    }

    public async Task ClearHistoryAsync(int profileId)
    {
        var history = await _context.WatchHistories
            .Where(h => h.ProfileId == profileId)
            .ToListAsync();
            
        _context.WatchHistories.RemoveRange(history);
        await _context.SaveChangesAsync();
    }

    public async Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId)
    {
        return await _context.WatchHistories
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && 
                                     (channelId != null ? w.ChannelId == channelId : w.EpisodeId == episodeId));
    }

    public async Task CleanupOlderThanDaysAsync(int profileId, int days)
    {
        if (days <= 0)
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-days);
        var staleRows = await _context.WatchHistories
            .Where(h => h.ProfileId == profileId && h.WatchedAt < cutoff)
            .ToListAsync();

        if (staleRows.Count == 0)
        {
            return;
        }

        _context.WatchHistories.RemoveRange(staleRows);
        await _context.SaveChangesAsync();
    }
}

