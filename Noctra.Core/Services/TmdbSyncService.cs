using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// On-demand TMDB enrichment service. Fetches metadata only when requested
/// (e.g., when series become visible on screen), not in the background.
/// </summary>
public class TmdbSyncService : ITmdbSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IMetadataService _metadataService;
    private readonly ILogger<TmdbSyncService>? _logger;

    // Rate limit: TMDB allows ~40 req / 10s
    private const int REQUEST_DELAY_MS = 300;
    private const int MAX_CONCURRENT = 3;

    public TmdbSyncService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IMetadataService metadataService,
        ILogger<TmdbSyncService>? logger = null)
    {
        _dbContextFactory = dbContextFactory;
        _metadataService = metadataService;
        _logger = logger;
    }

    public async Task EnrichSeriesBatchAsync(List<Series> series, CancellationToken cancellationToken = default)
    {
        // Only process series that haven't been synced yet
        var pending = series
            .Where(s => s.TmdbId == null && s.LastTmdbSync == null)
            .ToList();

        if (pending.Count == 0)
            return;

        _logger?.LogDebug("Enriching {Count} series with TMDB data", pending.Count);

        // Process with limited concurrency (MAX_CONCURRENT parallel requests)
        using var semaphore = new SemaphoreSlim(MAX_CONCURRENT);
        var tasks = pending.Select(async s =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await EnrichSingleSeriesAsync(s, cancellationToken);
                await Task.Delay(REQUEST_DELAY_MS, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnrichSingleSeriesAsync(Series series, CancellationToken cancellationToken)
    {
        try
        {
            var languageCode = SeriesInfoParser.ExtractLanguageCode(series.Name);
            var cleanName = SeriesInfoParser.CleanSeriesName(series.Name);

            var meta = await _metadataService.FetchMetadataAsync(cleanName, ChannelType.Series, languageCode, cancellationToken);

            // Persist to database
            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var dbSeries = await context.Series.FindAsync(new object[] { series.Id }, cancellationToken);
            if (dbSeries == null) return;

            dbSeries.LastTmdbSync = DateTime.UtcNow;

            if (meta != null && meta.TmdbId.HasValue)
            {
                dbSeries.TmdbId = meta.TmdbId;
                dbSeries.TmdbTitle = meta.Title;
                dbSeries.Plot = meta.Description;
                dbSeries.Rating = meta.Rating;
                dbSeries.ReleaseYear = meta.ReleaseYear;
                dbSeries.BackdropUrl = meta.BackdropUrl;
                dbSeries.Director = meta.Director;
                dbSeries.Cast = meta.Cast;
                dbSeries.ContentRating = meta.ContentRating;
                dbSeries.MetadataFetchedAt = DateTime.UtcNow;

                if (string.IsNullOrEmpty(dbSeries.CoverUrl) && !string.IsNullOrEmpty(meta.PosterUrl))
                    dbSeries.CoverUrl = meta.PosterUrl;

                if (meta.Genres != null && meta.Genres.Count > 0)
                    dbSeries.Genre = string.Join(", ", meta.Genres);

                // Update the in-memory object so UI reflects changes immediately
                series.TmdbId = meta.TmdbId;
                series.TmdbTitle = meta.Title;
                series.Plot = meta.Description;
                series.Rating = meta.Rating;
                series.ReleaseYear = meta.ReleaseYear;
                series.BackdropUrl = meta.BackdropUrl;
                series.Director = meta.Director;
                series.Cast = meta.Cast;
                series.ContentRating = meta.ContentRating;
                series.MetadataFetchedAt = DateTime.UtcNow;
                if (string.IsNullOrEmpty(series.CoverUrl) && !string.IsNullOrEmpty(meta.PosterUrl))
                    series.CoverUrl = meta.PosterUrl;
                if (meta.Genres != null && meta.Genres.Count > 0)
                    series.Genre = string.Join(", ", meta.Genres);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to enrich series: {Name}", series.Name);
        }
    }
}
