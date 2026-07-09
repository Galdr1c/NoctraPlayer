using Noctra.Models;

namespace Noctra.Services.Interfaces;

public sealed record ContentPageRequest(
    int PlaylistId,
    int Skip,
    int Take,
    string? SearchText = null,
    string? Group = null,
    ChannelType? Type = null,
    bool OnlyFavorites = false,
    ChannelSortOrder SortOrder = ChannelSortOrder.NewestFirst,
    bool ApplyHiddenGroups = true);

public interface IContentQueryService
{
    Task<(int TotalCount, List<string> AllGroups, List<string> LiveGroups, List<string> VodGroups, List<string> SeriesGroups)>
        GetChannelGroupMetadataAsync(int playlistId);

    Task<List<Channel>> GetChannelPageAsync(ContentPageRequest request);

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
