using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public sealed class PlayerResumeResolver
{
    private readonly IWatchHistoryService _watchHistoryService;

    public PlayerResumeResolver(IWatchHistoryService watchHistoryService)
    {
        _watchHistoryService = watchHistoryService;
    }

    public async Task<double?> ResolveAsync(
        int? profileId,
        Channel channel,
        Episode? episode,
        CancellationToken cancellationToken = default)
    {
        if (!profileId.HasValue || channel.Type == ChannelType.Live)
        {
            return null;
        }

        if (channel.Type == ChannelType.Series)
        {
            if (!IsSameStream(channel.StreamUrl, episode?.StreamUrl))
            {
                return null;
            }

            var episodeHistory = episode!.Id > 0
                ? await TryGetLatestAsync(
                    profileId.Value,
                    channelId: null,
                    episode.Id,
                    cancellationToken)
                : null;

            var episodePosition =
                episodeHistory?.StoppedAt ??
                episode.WatchedPosition ??
                TimeSpan.Zero;
            var episodeCompleted =
                episodeHistory?.Completed ??
                episode.IsCompleted;
            var hasAuthoritativeEpisodeState =
                episodeHistory is not null ||
                episode.LastWatched.HasValue ||
                episode.IsCompleted;
            var effectiveDuration =
                episode.Duration is { TotalSeconds: > 0 }
                    ? episode.Duration
                    : channel.Duration;

            if (hasAuthoritativeEpisodeState)
            {
                return ShouldOfferResume(
                    episodePosition,
                    effectiveDuration,
                    episodeCompleted,
                    hasRealWatchSignal: true)
                    ? episodePosition.TotalSeconds
                    : null;
            }

            return await ResolveChannelAsync(
                profileId.Value,
                channel,
                cancellationToken);
        }

        return channel.Type == ChannelType.VOD
            ? await ResolveChannelAsync(
                profileId.Value,
                channel,
                cancellationToken)
            : null;
    }

    private async Task<double?> ResolveChannelAsync(
        int profileId,
        Channel channel,
        CancellationToken cancellationToken)
    {
        var history = channel.Id > 0
            ? await TryGetLatestAsync(
                profileId,
                channel.Id,
                episodeId: null,
                cancellationToken)
            : null;

        var position = history?.StoppedAt ?? channel.WatchedPosition ?? TimeSpan.Zero;
        var completed = history?.Completed ?? channel.IsCompleted;
        var hasRealWatchSignal = history is not null || channel.LastWatched.HasValue;

        return ShouldOfferResume(
            position,
            channel.Duration,
            completed,
            hasRealWatchSignal)
            ? position.TotalSeconds
            : null;
    }

    private async Task<WatchHistory?> TryGetLatestAsync(
        int profileId,
        int? channelId,
        int? episodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _watchHistoryService.GetLatestForMediaAsync(
                profileId,
                channelId,
                episodeId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameStream(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(
               left,
               right,
               StringComparison.OrdinalIgnoreCase);

    private static bool ShouldOfferResume(
        TimeSpan position,
        TimeSpan? duration,
        bool completed,
        bool hasRealWatchSignal)
    {
        if (!hasRealWatchSignal || completed || position.TotalSeconds <= 120)
        {
            return false;
        }

        if (duration.HasValue && duration.Value.TotalSeconds > 0)
        {
            var durationSeconds = duration.Value.TotalSeconds;
            if (position.TotalSeconds >= durationSeconds - 30 ||
                position.TotalSeconds / durationSeconds >= 0.95)
            {
                return false;
            }
        }

        return true;
    }
}
