using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Background worker that scans the database for VOD and Series without a TmdbId
/// and enriches them via TMDB API. It respects rate limits and runs silently.
/// </summary>
public class TmdbSyncService : ITmdbSyncService, IDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IMetadataService _metadataService;
    private readonly ILogger<TmdbSyncService>? _logger;

    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private readonly SemaphoreSlim _triggerSignal = new(0, 1);

    // How many items to process per batch before pausing
    private const int BATCH_SIZE = 50;
    // Delay between individual TMDB requests to avoid 429 Too Many Requests (TMDB limit: ~40 req / 10s)
    private const int REQUEST_DELAY_MS = 300; 
    // Sleep duration when database is fully synced and there is no work left
    private readonly TimeSpan _idleSleepDuration = TimeSpan.FromHours(1);

    public TmdbSyncService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IMetadataService metadataService,
        ILogger<TmdbSyncService>? logger = null)
    {
        _dbContextFactory = dbContextFactory;
        _metadataService = metadataService;
        _logger = logger;
    }

    public void StartSync()
    {
        if (_workerTask != null && !_workerTask.IsCompleted)
            return;

        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => SyncLoopAsync(_cts.Token), _cts.Token);
        _logger?.LogInformation("TmdbSyncService started.");
    }

    public void TriggerSync()
    {
        if (_triggerSignal.CurrentCount == 0)
        {
            _triggerSignal.Release();
        }
    }

    public void StopSync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _logger?.LogInformation("TmdbSyncService stopping...");
        }
    }

    private async Task SyncLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var workDone = await ProcessBatchAsync(cancellationToken);

                if (workDone)
                {
                    // If we processed items, take a short breath before the next batch
                    await Task.Delay(5000, cancellationToken);
                }
                else
                {
                    // No items left to sync. Sleep for a long time OR until manually triggered
                    _logger?.LogInformation("TmdbSyncService is idle. All content is synced.");
                    await WaitUntilTriggeredOrTimeoutAsync(_idleSleepDuration, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fatal error in TmdbSyncService loop. Retrying in 1 minute.");
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        bool processedAny = false;

        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // 1. Process VODs (Channels where Type == VOD and TmdbId is null)
        var pendingVods = await context.Channels
            .Where(c => c.Type == ChannelType.VOD && c.TmdbId == null && c.LastTmdbSync == null)
            .OrderByDescending(c => c.Id)
            .Take(BATCH_SIZE)
            .ToListAsync(cancellationToken);

        foreach (var vod in pendingVods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var languageCode = SeriesInfoParser.ExtractLanguageCode(vod.GroupTitle ?? vod.Name);
            _logger?.LogDebug("Syncing VOD: {Name} with Language: {Lang}", vod.Name, languageCode);
            
            var meta = await _metadataService.FetchMetadataAsync(vod.Name, ChannelType.VOD, languageCode, cancellationToken);
            
            vod.LastTmdbSync = DateTime.UtcNow;

            if (meta != null && meta.TmdbId.HasValue)
            {
                vod.TmdbId = meta.TmdbId;
                vod.Plot = meta.Description;
                vod.Rating = meta.Rating;
                vod.ReleaseYear = meta.ReleaseYear;
                vod.BackdropUrl = meta.BackdropUrl;
                vod.Director = meta.Director;
                vod.Cast = meta.Cast;
                vod.ContentRating = meta.ContentRating;
                
                if (string.IsNullOrEmpty(vod.LogoUrl) && !string.IsNullOrEmpty(meta.PosterUrl))
                    vod.LogoUrl = meta.PosterUrl;
            }

            processedAny = true;
            await context.SaveChangesAsync(cancellationToken);
            await Task.Delay(REQUEST_DELAY_MS, cancellationToken); // Rate limit
        }

        // 2. Process Series (Where TmdbId is null)
        var pendingSeries = await context.Series
            .Where(s => s.TmdbId == null && s.LastTmdbSync == null)
            .OrderByDescending(s => s.Id)
            .Take(BATCH_SIZE)
            .ToListAsync(cancellationToken);

        foreach (var series in pendingSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var languageCode = SeriesInfoParser.ExtractLanguageCode(series.Name);
            // Clean the name using our robust parser before sending to TMDB
            var cleanName = SeriesInfoParser.CleanSeriesName(series.Name);
            _logger?.LogDebug("Syncing Series: {Name} -> {CleanName} ({Lang})", series.Name, cleanName, languageCode);
            
            var meta = await _metadataService.FetchMetadataAsync(cleanName, ChannelType.Series, languageCode, cancellationToken);
            
            series.LastTmdbSync = DateTime.UtcNow;

            if (meta != null && meta.TmdbId.HasValue)
            {
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
            }

            processedAny = true;
            await context.SaveChangesAsync(cancellationToken);
            await Task.Delay(REQUEST_DELAY_MS, cancellationToken); // Rate limit
        }

        return processedAny;
    }

    private async Task WaitUntilTriggeredOrTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            // Wait until someone calls TriggerSync() OR the timeout expires
            await _triggerSignal.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // If it was cancelled by the timeout, we just swallow it and loop continues
            if (cancellationToken.IsCancellationRequested)
                throw; 
        }
    }

    public void Dispose()
    {
        StopSync();
        _cts?.Dispose();
        _triggerSignal.Dispose();
        GC.SuppressFinalize(this);
    }
}
