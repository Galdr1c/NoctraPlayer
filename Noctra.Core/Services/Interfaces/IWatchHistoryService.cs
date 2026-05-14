using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IWatchHistoryService
{
    Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position, bool completed = false, TimeSpan? duration = null, TimeSpan? incrementDelta = null, bool allowReset = false, CancellationToken ct = default);
    Task<List<WatchHistory>> GetHistoryAsync(int profileId, int skip = 0, int take = 50, CancellationToken ct = default);
    Task DeleteProfileHistoryAsync(int profileId, CancellationToken ct = default);
    Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default);
    Task RemoveFromHistoryAsync(int profileId, int? channelId, int? seriesId, CancellationToken ct = default);
    Task CleanupOlderThanDaysAsync(int profileId, int days, CancellationToken ct = default);
}

