using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// Service for fetching content metadata from external sources (TMDB)
/// </summary>
public interface IMetadataService
{
    /// <summary>
    /// Fetches metadata for a search query from TMDB
    /// </summary>
    /// <param name="searchQuery">Movie or TV show name to search</param>
    /// <param name="type">Optional channel type to filter (Movie/Series)</param>
    /// <returns>Metadata if found, null otherwise</returns>
    Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null, string languageCode = "tr-TR", CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Enriches a channel with metadata from TMDB
    /// </summary>
    /// <param name="channel">Channel to enrich</param>
    Task EnrichChannelAsync(Channel channel, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Enriches multiple channels in batch
    /// </summary>
    /// <param name="channels">Channels to enrich</param>
    /// <param name="progress">Optional progress callback</param>
    Task EnrichChannelsAsync(IEnumerable<Channel> channels, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets genre names for given genre IDs
    /// </summary>
    /// <param name="genreIds">TMDB genre IDs</param>
    /// <returns>List of genre names</returns>
    Task<List<string>> GetGenresAsync(List<int> genreIds, string languageCode = "tr-TR", CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clears the genre cache
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Fetches deep details for a series (Credits, Cast, etc) by TmdbId
    /// </summary>
    Task<TmdbDetail?> FetchSeriesDetailsAsync(int tmdbId, string languageCode = "tr-TR", CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a specific season's details including all episode overviews and thumbnails
    /// </summary>
    Task<TmdbSeasonDetail?> FetchSeasonDetailsAsync(int tmdbId, int seasonNumber, string languageCode = "tr-TR", CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight search-only method for scroll enrichment.
    /// Returns basic metadata (poster, overview, year, rating, genres) WITHOUT fetching details (cast, contentRating, trailer).
    /// This uses 1 API call instead of 2.
    /// </summary>
    Task<ChannelMetadata?> SearchSeriesAsync(string searchQuery, string languageCode = "tr-TR", CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies heuristics to select the best content rating and network logo based on context.
    /// </summary>
    void ApplyHeuristics(TmdbDetail details, ChannelMetadata metadata, string languageCode, string? contextTitle);
}
