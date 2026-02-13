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
    Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null);
    
    /// <summary>
    /// Enriches a channel with metadata from TMDB
    /// </summary>
    /// <param name="channel">Channel to enrich</param>
    Task EnrichChannelAsync(Channel channel);
    
    /// <summary>
    /// Enriches multiple channels in batch
    /// </summary>
    /// <param name="channels">Channels to enrich</param>
    /// <param name="progress">Optional progress callback</param>
    Task EnrichChannelsAsync(IEnumerable<Channel> channels, IProgress<int>? progress = null);
    
    /// <summary>
    /// Gets genre names for given genre IDs
    /// </summary>
    /// <param name="genreIds">TMDB genre IDs</param>
    /// <returns>List of genre names</returns>
    Task<List<string>> GetGenresAsync(List<int> genreIds);
    
    /// <summary>
    /// Clears the genre cache
    /// </summary>
    void ClearCache();
}

