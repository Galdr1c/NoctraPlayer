using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface ITmdbSyncService
{
    /// <summary>
    /// Enriches a batch of series with TMDB metadata (poster, plot, cast, genre, etc.).
    /// Only fetches data for series that don't already have a TmdbId.
    /// Results are saved directly to the database.
    /// </summary>
    Task EnrichSeriesBatchAsync(List<Series> series, CancellationToken cancellationToken = default);
}
