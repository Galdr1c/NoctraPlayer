using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IWatchHistoryService
{
    Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false, TimeSpan? duration = null, TimeSpan? incrementDelta = null, CancellationToken ct = default);
    Task<List<WatchHistory>> GetHistoryAsync(int profileId, CancellationToken ct = default);
    Task ClearHistoryAsync(int profileId, CancellationToken ct = default);
    Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default);
    Task RemoveFromHistoryAsync(int profileId, int? channelId, int? seriesId, CancellationToken ct = default);
    Task CleanupOlderThanDaysAsync(int profileId, int days, CancellationToken ct = default);
}

