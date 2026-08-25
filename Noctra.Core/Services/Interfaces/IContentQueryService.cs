using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Keyset pagination cursor. <paramref name="AdultPhase"/> marks that every
/// normal (non-adult) row has already been served and only adult-group rows
/// remain, so the next page must continue inside the adult segment only.
/// </summary>
public readonly record struct ContentPageCursor(int LastId, bool AdultPhase = false);

public sealed record ContentPageRequest(
    int PlaylistId,
    int Skip,
    int Take,
    string? SearchText = null,
    string? Group = null,
    ChannelType? Type = null,
    bool OnlyFavorites = false,
    ChannelSortOrder SortOrder = ChannelSortOrder.NewestFirst,
    bool ApplyHiddenGroups = true,
    ContentPageCursor? Cursor = null,
    IReadOnlyCollection<string>? AdultGroupsLast = null);

public interface IContentQueryService
{
    Task<(int TotalCount, List<string> AllGroups, List<string> LiveGroups, List<string> VodGroups, List<string> SeriesGroups)>
        GetChannelGroupMetadataAsync(
            int playlistId,
            CancellationToken cancellationToken = default);

    Task<List<Channel>> GetChannelPageAsync(
        ContentPageRequest request,
        CancellationToken cancellationToken = default);

    Task<List<Series>> GetSeriesListAsync(
        int playlistId,
        CancellationToken cancellationToken = default);

    Task<List<int>> GetProfilePlaylistIdsAsync(
        int profileId,
        CancellationToken cancellationToken = default);

    Task<List<Channel>> GetHistoryPageAsync(
        int profileId,
        IReadOnlyCollection<int> profilePlaylistIds,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
