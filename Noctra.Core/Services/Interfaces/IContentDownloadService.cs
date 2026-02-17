using Noctra.Models;

namespace Noctra.Services.Interfaces;

public enum DownloadItemType
{
    Vod,
    SeriesEpisode
}

public sealed record DownloadTrackOption(int Id, string Name);

public sealed record DownloadContentRequest(
    int ProfileId,
    DownloadItemType ItemType,
    string DisplayName,
    string SourceUrl,
    string? PosterUrl = null,
    int PlaylistId = 0,
    int ChannelId = 0,
    int EpisodeId = 0,
    IReadOnlyList<DownloadTrackOption>? AudioTracks = null,
    IReadOnlyList<DownloadTrackOption>? SubtitleTracks = null);

public sealed record DownloadContentResult(
    bool Success,
    bool AlreadyExists,
    string Message,
    int? DownloadId = null);

public interface IContentDownloadService
{
    event EventHandler? DownloadsChanged;

    Task<DownloadContentResult> QueueDownloadAsync(
        DownloadContentRequest request,
        CancellationToken cancellationToken = default);

    Task<string> ResolvePlayableUrlAsync(
        string streamUrl,
        CancellationToken cancellationToken = default);

    Task CleanupPlaybackCacheAsync(
        CancellationToken cancellationToken = default);

    Task<List<DownloadItem>> GetDownloadsAsync(
        int profileId,
        CancellationToken cancellationToken = default);

    Task CancelDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default);

    Task PauseDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default);

    Task ResumeDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default);

    Task DeleteProfileDownloadsAsync(
        int profileId,
        CancellationToken cancellationToken = default);
}
