using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Service for managing and aggregating media content (Series, Seasons, Episodes)
/// </summary>
public interface IMediaService
{
    /// <summary>
    /// Aggregates flat channel list into a structured series/season/episode hierarchy
    /// </summary>
    /// <param name="playlistId">ID of the playlist to aggregate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AggregateContentAsync(int playlistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all aggregated series for a specific playlist
    /// </summary>
    /// <param name="playlistId">ID of the playlist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of series with their seasons and episodes</returns>
    Task<List<Series>> GetSeriesAsync(int playlistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates series metadata or status (Favorite, InMyList)
    /// </summary>
    /// <param name="series">Series object to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateSeriesAsync(Series series, CancellationToken cancellationToken = default);
}

