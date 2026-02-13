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

    public async Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false)
    {
        var history = await _context.WatchHistories
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && 
                                     (channelId != null ? w.ChannelId == channelId : w.EpisodeId == episodeId));

        if (history == null)
        {
            history = new WatchHistory
            {
                ProfileId = profileId,
                ChannelId = channelId,
                EpisodeId = episodeId,
                WatchedAt = DateTime.Now
            };
            _context.WatchHistories.Add(history);
        }

        history.StoppedAt = position;
        history.WatchedAt = DateTime.Now;
        history.Completed = completed;
        
        // Update total watched duration (approximate increment)
        history.WatchedDuration += TimeSpan.FromSeconds(5);

        await _context.SaveChangesAsync();
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
}

