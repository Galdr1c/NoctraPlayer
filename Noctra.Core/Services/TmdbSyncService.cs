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
    private readonly IDispatcherService _dispatcherService;
    private readonly ITmdbEnrichmentScheduler _enrichmentScheduler;
    private readonly ILogger<TmdbSyncService>? _logger;

    public TmdbSyncService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IMetadataService metadataService,
        IDispatcherService dispatcherService,
        ITmdbEnrichmentScheduler enrichmentScheduler,
        ILogger<TmdbSyncService>? logger = null)
    {
        _dbContextFactory = dbContextFactory;
        _metadataService = metadataService;
        _dispatcherService = dispatcherService;
        _enrichmentScheduler = enrichmentScheduler;
        _logger = logger;
    }

    public async Task EnrichSeriesBatchAsync(List<Series> series, CancellationToken cancellationToken = default)
    {
        // Process: series that need enrichment (missing TmdbId AND don't have enough metadata from provider)
        var pending = series
            .Where(s => ((s.TmdbId == null && s.LastTmdbSync == null) || s.MetadataFetchedAt == null) 
                        && !IsMetadataSufficient(s))
            .ToList();

        if (pending.Count == 0)
            return;

        // --- KOTA TASARRUFU: Sadece M3U çalma listeleri için TMDB kullan ---
        // Xtream ve Stalker portal içerikleri zaten meta veri ile yüklendiği için atla.
        using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var m3uPlaylistIds = await db.Playlists
            .Include(p => p.Profile)
            .Where(p => p.Profile != null && p.Profile.ProviderAccount.Type == ProfileType.M3U)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        pending = pending.Where(s => m3uPlaylistIds.Contains(s.PlaylistId)).ToList();

        if (pending.Count == 0)
            return;

        _logger?.LogDebug("Enriching {Count} series with TMDB data (M3U only filtering active)", pending.Count);

        var tasks = pending.Select(series => _enrichmentScheduler.ScheduleAsync(
            $"series-sync:{series.PlaylistId}:{series.Id}",
            token => EnrichSingleSeriesAsync(series, token),
            cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task EnrichSingleSeriesAsync(Series series, CancellationToken cancellationToken)
    {
        try
        {
            var languageCode = SeriesInfoParser.ExtractLanguageCode(series.GroupTitle ?? series.Genre ?? series.Name);

            // PHASE DECISION:
            // - TmdbId known (cache invalidated) → full detail via /tv/{id} (1 request, sets MetadataFetchedAt)
            // - TmdbId unknown → search-only via /search/tv (1 request, MetadataFetchedAt stays null → detail view will fetch cast/trailer)
            if (series.TmdbId.HasValue && series.TmdbId.Value > 0)
            {
                await EnrichWithFullDetailsAsync(series, languageCode, cancellationToken);
            }
            else
            {
                await EnrichWithSearchOnlyAsync(series, languageCode, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to enrich series: {Name}", series.Name);
            throw;
        }
    }

    /// <summary>
    /// Phase 1 — Search-only enrichment (1 API call).
    /// Provides list-view data: poster, overview, year, rating, genres, backdrop, TmdbId.
    /// MetadataFetchedAt is intentionally left NULL so detail view triggers Phase 2.
    /// </summary>
    private async Task EnrichWithSearchOnlyAsync(Series series, string languageCode, CancellationToken cancellationToken)
    {
        var cleanName = SeriesInfoParser.CleanSeriesName(series.Name);
        var meta = await _metadataService.SearchSeriesAsync(cleanName, languageCode, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dbSeries = await context.Series.FindAsync(new object[] { series.Id }, cancellationToken);
        if (dbSeries == null) return;
        cancellationToken.ThrowIfCancellationRequested();

        dbSeries.LastTmdbSync = DateTime.UtcNow;

        if (meta != null && meta.TmdbId.HasValue)
        {
            // List-view fields only — NO cast, contentRating, trailer
            dbSeries.TmdbId = meta.TmdbId;
            dbSeries.TmdbTitle = meta.Title;
            dbSeries.Plot = meta.Description;
            dbSeries.Rating = meta.Rating;
            dbSeries.ReleaseYear = meta.ReleaseYear;
            dbSeries.BackdropUrl = meta.BackdropUrl;
            // MetadataFetchedAt intentionally NULL → detail view will fetch full data

            if (ShouldFillCoverUrl(dbSeries.CoverUrl) && !string.IsNullOrEmpty(meta.PosterUrl))
                dbSeries.CoverUrl = meta.PosterUrl;

            if (meta.Genres != null && meta.Genres.Count > 0)
                dbSeries.Genre = string.Join(", ", meta.Genres);

            // Apply heuristics (Network, Rating etc.)
            dbSeries.NetworkName = meta.NetworkName;
            dbSeries.NetworkLogoUrl = meta.NetworkLogoUrl;
            dbSeries.ContentRating = meta.ContentRating;

        }

        cancellationToken.ThrowIfCancellationRequested();
        await context.SaveChangesAsync(cancellationToken);

        if (meta != null && meta.TmdbId.HasValue)
        {
            await _dispatcherService.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }

                series.TmdbId = meta.TmdbId;
                series.TmdbTitle = meta.Title;
                series.Plot = meta.Description;
                series.Rating = meta.Rating;
                series.ReleaseYear = meta.ReleaseYear;
                series.BackdropUrl = meta.BackdropUrl;
                series.LastTmdbSync = DateTime.UtcNow;
                if (ShouldFillCoverUrl(series.CoverUrl) && !string.IsNullOrEmpty(meta.PosterUrl))
                    series.CoverUrl = meta.PosterUrl;
                if (meta.Genres != null && meta.Genres.Count > 0)
                    series.Genre = string.Join(", ", meta.Genres);

                // Real-time update
                series.NetworkName = meta.NetworkName;
                series.NetworkLogoUrl = meta.NetworkLogoUrl;
                series.ContentRating = meta.ContentRating;
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>
    /// Phase 2 — Full detail enrichment (1 API call with append_to_response).
    /// Used when TmdbId is already known (cache invalidated series).
    /// Fetches cast, content rating, trailer, and sets MetadataFetchedAt.
    /// </summary>
    private async Task EnrichWithFullDetailsAsync(Series series, string languageCode, CancellationToken cancellationToken)
    {
        var details = await _metadataService.FetchSeriesDetailsAsync(series.TmdbId!.Value, languageCode, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dbSeries = await context.Series.FindAsync(new object[] { series.Id }, cancellationToken);
        if (dbSeries == null) return;
        cancellationToken.ThrowIfCancellationRequested();

        dbSeries.LastTmdbSync = DateTime.UtcNow;

        if (details != null)
        {
            dbSeries.TmdbTitle = details.Name;
            dbSeries.Plot = details.Overview;
            dbSeries.Rating = details.VoteAverage;
            dbSeries.ReleaseYear = details.ReleaseYear;
            dbSeries.MetadataFetchedAt = DateTime.UtcNow;

            if (ShouldFillCoverUrl(dbSeries.CoverUrl) && !string.IsNullOrEmpty(details.PosterPath))
                dbSeries.CoverUrl = $"https://image.tmdb.org/t/p/w500{details.PosterPath}";
            if (!string.IsNullOrEmpty(details.BackdropPath))
                dbSeries.BackdropUrl = $"https://image.tmdb.org/t/p/original{details.BackdropPath}";

            if (details.Genres != null && details.Genres.Count > 0)
                dbSeries.Genre = string.Join(", ", details.Genres.Select(g => g.Name));

            // Credits — cast + director
            if (details.Credits != null)
            {
                var director = details.DirectorName;
                if (!string.IsNullOrEmpty(director))
                    dbSeries.Director = director;

                var castList = details.Credits.Cast?.OrderBy(c => c.Order).Take(5).Select(c => c.Name).ToList();
                if (castList != null && castList.Any())
                    dbSeries.Cast = string.Join(", ", castList);
            }

            // Centralized Heuristics (Network, Rating, etc.)
            var tempMetadata = new ChannelMetadata();
            _metadataService.ApplyHeuristics(details, tempMetadata, languageCode, series.GroupTitle ?? series.Name);
            
            dbSeries.ContentRating = tempMetadata.ContentRating;
            dbSeries.NetworkName = tempMetadata.NetworkName;
            dbSeries.NetworkLogoUrl = tempMetadata.NetworkLogoUrl;

            // Trailer
            var trailer = details.Videos?.Results?
                .Where(v => v.Site == "YouTube" && (v.Type == "Trailer" || v.Type == "Teaser"))
                .OrderByDescending(v => v.Official)
                .ThenByDescending(v => v.Type == "Trailer")
                .FirstOrDefault();
            if (trailer != null && !string.IsNullOrEmpty(trailer.Key))
                dbSeries.TrailerUrl = $"https://www.youtube.com/watch?v={trailer.Key}";

        }

        cancellationToken.ThrowIfCancellationRequested();
        await context.SaveChangesAsync(cancellationToken);

        if (details != null)
        {
            await _dispatcherService.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }

                series.TmdbTitle = dbSeries.TmdbTitle;
                series.Plot = dbSeries.Plot;
                series.Rating = dbSeries.Rating;
                series.ReleaseYear = dbSeries.ReleaseYear;
                series.BackdropUrl = dbSeries.BackdropUrl;
                series.Director = dbSeries.Director;
                series.Cast = dbSeries.Cast;
                series.ContentRating = dbSeries.ContentRating;
                series.MetadataFetchedAt = dbSeries.MetadataFetchedAt;
                series.LastTmdbSync = dbSeries.LastTmdbSync;
                series.NetworkName = dbSeries.NetworkName;
                series.NetworkLogoUrl = dbSeries.NetworkLogoUrl;
                if (!string.IsNullOrEmpty(dbSeries.CoverUrl))
                    series.CoverUrl = dbSeries.CoverUrl;
                if (!string.IsNullOrEmpty(dbSeries.Genre))
                    series.Genre = dbSeries.Genre;
                if (!string.IsNullOrEmpty(dbSeries.TrailerUrl))
                    series.TrailerUrl = dbSeries.TrailerUrl;
                return Task.CompletedTask;
            });
        }
    }

    private bool IsMetadataSufficient(Series series)
    {
        // Provider (Xtream vb.) zaten yeterli veri gönderdiyse (Poster + Özet + Puan), 
        // TMDB'yi "son çare" olarak bırakmak için senkronizasyonu atla.
        if (string.IsNullOrEmpty(series.CoverUrl) || 
            series.CoverUrl.Contains("noposter") || 
            series.CoverUrl.Contains("placeholder") ||
            series.CoverUrl.Contains("default"))
        {
            return false;
        }

        // Özet (Plot) çok kısa veya boşsa yetersizdir.
        if (string.IsNullOrEmpty(series.Plot) || series.Plot.Length < 30)
        {
            return false;
        }

        // Puan bilgisi de varsa yeterli kabul et.
        return series.Rating > 0;
    }

    private static bool ShouldFillCoverUrl(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            return true;
        }

        return coverUrl.Contains("noposter", StringComparison.OrdinalIgnoreCase) ||
               coverUrl.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
               coverUrl.Contains("default", StringComparison.OrdinalIgnoreCase);
    }

}
