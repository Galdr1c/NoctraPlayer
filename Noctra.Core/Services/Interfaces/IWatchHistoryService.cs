using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IWatchHistoryService
{
    Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false, TimeSpan? duration = null, TimeSpan? incrementDelta = null);
    Task<List<WatchHistory>> GetHistoryAsync(int profileId);
    Task ClearHistoryAsync(int profileId);
    Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId);
    Task CleanupOlderThanDaysAsync(int profileId, int days);
}

