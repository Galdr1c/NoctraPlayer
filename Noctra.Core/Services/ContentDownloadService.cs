using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Classification of a download target's stream format. Used to reject HLS/DASH
/// streams early instead of saving a manifest text file and marking it Completed.
/// </summary>
internal enum DownloadContentKind
{
    DirectFile,
    Hls,
    Dash,
    Unsupported
}

public class ContentDownloadService : IContentDownloadService
{
    internal const string CredentialChangeFailureMessage =
        "Hesap bilgileri degistirildi. Indirmeyi yeni bilgilerle bastan baslatmaniz gerekiyor.";
    private const int ProgressPersistIntervalMs = 1800;
    private const long ProgressPersistMinDeltaBytes = 1024 * 1024; // 1 MB
    private const int MaxAutoResumeAttempts = 3;
    private const int MaxPosterBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ISettingsService _settingsService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _httpClient;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<ContentDownloadService>? _logger;
    private readonly IAppPathService _appPaths;
    private readonly INetworkService? _networkService;
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly SemaphoreSlim _downloadStateGate = new(1, 1);
    private readonly ConcurrentQueue<int> _pendingIds = new();
    private readonly ConcurrentDictionary<int, byte> _queuedIds = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeDownloadCts = new();
    private readonly ConcurrentDictionary<int, Task> _activeDownloadTasks = new();
    private readonly ConcurrentDictionary<int, string> _activeTempFiles = new();
    private readonly ConcurrentDictionary<int, byte> _credentialFailureRequestedIds = new();
    private readonly ConcurrentDictionary<int, byte> _pauseRequestedIds = new();
    private readonly ConcurrentDictionary<int, byte> _cancelRequestedIds = new();
    private readonly ConcurrentDictionary<int, int> _autoResumeAttempts = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastCleanupUtcByProfile = new();
    private int _isQueueWorkerStarted;

    public event EventHandler<DownloadsChangedEventArgs>? DownloadsChanged;
    public event EventHandler<DownloadItem>? DownloadCompleted;

    public ContentDownloadService(
        ISettingsService settingsService,
        IDbContextFactory<AppDbContext> contextFactory,
        HttpClient httpClient,
        ILocalizationService localizationService,
        ILogger<ContentDownloadService>? logger = null,
        IAppPathService? appPaths = null,
        INetworkService? networkService = null)
    {
        _settingsService = settingsService;
        _contextFactory = contextFactory;
        _httpClient = httpClient;
        _localizationService = localizationService;
        _logger = logger;
        _appPaths = appPaths ?? new DesktopAppPathService();
        _networkService = networkService;

        // Ensure worker starts on app launch to process pending/interrupted downloads
        EnsureQueueWorkerStarted();
    }

    public async Task<DownloadContentResult> QueueDownloadAsync(
        DownloadContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProfileId <= 0)
        {
            return new DownloadContentResult(false, false, _localizationService.GetString("Download.Error.ProfileNotFound"));
        }

        if (string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            return new DownloadContentResult(false, false, _localizationService.GetString("Download.Error.InvalidUrl"));
        }

        var normalizedSource = request.SourceUrl.Trim().Trim('"', '\'');
        if (IsSegmentedManifestUrl(normalizedSource))
        {
            return new DownloadContentResult(
                false,
                false,
                _localizationService.GetString("Download.Error.UnsupportedStreaming"));
        }

        if (IsLocalFilePath(normalizedSource))
        {
            return new DownloadContentResult(true, true, _localizationService.GetString("Download.Status.AlreadyDownloaded"));
        }

        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var contentKey = BuildContentKey(request);

        var duplicate = await FindDuplicateAsync(db, request, contentKey, normalizedSource, cancellationToken);
        if (duplicate != null)
        {
            return ToDuplicateResult(duplicate);
        }

        var item = new DownloadItem
        {
            ProfileId = request.ProfileId,
            PlaylistId = request.PlaylistId,
            ChannelId = request.ChannelId > 0 ? request.ChannelId : null,
            EpisodeId = request.EpisodeId > 0 ? request.EpisodeId : null,
            SeriesId = request.SeriesId > 0 ? request.SeriesId : null,
            SeriesTitle = string.IsNullOrWhiteSpace(request.SeriesTitle) ? null : request.SeriesTitle.Trim(),
            SeasonNumber = request.SeasonNumber,
            EpisodeNumber = request.EpisodeNumber,
            EpisodeTitle = string.IsNullOrWhiteSpace(request.EpisodeTitle) ? null : request.EpisodeTitle.Trim(),
            ContentKey = contentKey,
            ChannelType = request.ItemType == DownloadItemType.SeriesEpisode ? ChannelType.Series : ChannelType.VOD,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? _localizationService.GetString("Download.DefaultName") : request.DisplayName.Trim(),
            PosterUrl = request.PosterUrl,
            SourcePosterUrl = string.IsNullOrWhiteSpace(request.PosterUrl) ? null : request.PosterUrl.Trim(),
            SourceUrl = normalizedSource,
            AudioTracksJson = SerializeTrackList(request.AudioTracks),
            SubtitleTracksJson = SerializeTrackList(request.SubtitleTracks),
            Status = DownloadStatus.Queued,
            BytesDownloaded = 0,
            BytesTotal = null,
            SpeedBytesPerSecond = 0,
            EstimatedSecondsRemaining = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            db.DownloadItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // A concurrent request inserted the same content between our query
            // and insert. The unique index on ContentKey closes that race.
            var racedDuplicate = contentKey != null
                ? await db.DownloadItems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        d => d.Status != DownloadStatus.Failed &&
                             d.Status != DownloadStatus.Canceled &&
                             d.ContentKey == contentKey,
                        cancellationToken)
                : null;

            // The conflicting row is an active/completed download in the
            // overwhelming majority of cases. If it already failed between
            // our insert and this lookup, reporting it as already-present
            // lets the user simply retry — the pre-check then succeeds.
            return racedDuplicate != null
                ? ToDuplicateResult(racedDuplicate)
                : new DownloadContentResult(true, true, _localizationService.GetString("Download.Status.AlreadyInQueue"));
        }

        if (_queuedIds.TryAdd(item.Id, 1))
        {
            _pendingIds.Enqueue(item.Id);
            _queueSignal.Release();
        }

        EnsureQueueWorkerStarted();
        DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        return new DownloadContentResult(true, false, _localizationService.GetString("Download.Status.AddedToQueue"), item.Id);
    }

    /// <summary>
    /// Normalized dedup key used to detect the same content even when the
    /// provider URL/token changed.
    ///   series: playlistId:episodeId
    ///   series: playlistId:normalizedSeriesTitle:season:episode  (fallback)
    ///   movie:  playlistId:channelId
    /// Returns null when the request carries no reliable identity, in which
    /// case the legacy SourceUrl match applies.
    /// </summary>
    internal static string? BuildContentKey(DownloadContentRequest request)
    {
        if (request.PlaylistId <= 0)
        {
            return null;
        }

        if (request.ItemType == DownloadItemType.SeriesEpisode)
        {
            if (request.EpisodeId > 0)
            {
                return $"series:{request.PlaylistId}:{request.EpisodeId}";
            }

            if (!string.IsNullOrWhiteSpace(request.SeriesTitle) &&
                request.SeasonNumber > 0 &&
                request.EpisodeNumber > 0)
            {
                var normalizedSeries = SeriesInfoParser.NormalizeKey(request.SeriesTitle);
                if (!string.IsNullOrWhiteSpace(normalizedSeries))
                {
                    return $"series:{request.PlaylistId}:{normalizedSeries}:{request.SeasonNumber}:{request.EpisodeNumber}";
                }
            }

            return null;
        }

        return request.ChannelId > 0
            ? $"movie:{request.PlaylistId}:{request.ChannelId}"
            : null;
    }

    private static Task<DownloadItem?> FindDuplicateAsync(
        AppDbContext db,
        DownloadContentRequest request,
        string? contentKey,
        string normalizedSource,
        CancellationToken cancellationToken)
    {
        // Content-key match first. Rows that predate the content-key feature
        // have a NULL key, so until the migration backfills them they are also
        // deduped by URL, EpisodeId or ChannelId.
        if (contentKey != null)
        {
            return db.DownloadItems
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    d => d.Status != DownloadStatus.Failed &&
                         d.Status != DownloadStatus.Canceled &&
                         (d.ContentKey == contentKey ||
                          (d.ContentKey == null &&
                           d.ProfileId == request.ProfileId &&
                           d.PlaylistId == request.PlaylistId &&
                           (d.SourceUrl == normalizedSource ||
                            (request.EpisodeId > 0 && d.EpisodeId == request.EpisodeId) ||
                            (request.ChannelId > 0 && d.ChannelId == request.ChannelId)))),
                    cancellationToken);
        }

        // No reliable identity: dedupe legacy items by URL only.
        return db.DownloadItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.ProfileId == request.ProfileId &&
                     d.SourceUrl == normalizedSource &&
                     d.Status != DownloadStatus.Failed &&
                     d.Status != DownloadStatus.Canceled,
                cancellationToken);
    }

    private DownloadContentResult ToDuplicateResult(DownloadItem? duplicate)
    {
        if (duplicate == null)
        {
            return new DownloadContentResult(false, false, _localizationService.GetString("Download.Status.AddedToQueue"));
        }

        if (duplicate.IsCompleted)
        {
            return new DownloadContentResult(true, true, _localizationService.GetString("Download.Status.AlreadyDownloaded"), duplicate.Id);
        }

        return new DownloadContentResult(true, true, _localizationService.GetString("Download.Status.AlreadyInQueue"), duplicate.Id);
    }

    internal static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException sqliteEx)
            {
                // Only treat as duplicate if it's specifically a UNIQUE or PRIMARY KEY violation
                // SqliteErrorCode 19 = SQLITE_CONSTRAINT (generic)
                // SqliteExtendedErrorCode distinguishes:
                //   1555 = SQLITE_CONSTRAINT_PRIMARYKEY
                //   2067 = SQLITE_CONSTRAINT_UNIQUE
                // Other constraint types (NOT NULL=1299, FOREIGN KEY=787, CHECK=275) should NOT be treated as duplicates
                var extendedCode = sqliteEx.SqliteExtendedErrorCode;
                if (extendedCode == 1555 || extendedCode == 2067)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public Task<string> ResolvePlayableUrlAsync(
        string streamUrl,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(streamUrl);
    }


    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var value = path.Trim().Trim('"', '\'');
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.IsFile)
        {
            value = uri.LocalPath;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return value;
        }
    }

    private async Task<string?> TryRestoreMissingLocalPathAsync(
        string missingPath,
        CancellationToken cancellationToken)
    {
        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.DownloadItems
            .FirstOrDefaultAsync(d => d.LocalFilePath == missingPath, cancellationToken);
        if (item == null)
        {
            return null;
        }

        await RestoreMappedEntitiesToSourceUrlAsync(db, item);
        db.DownloadItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        return item.SourceUrl;
    }

    public Task CleanupPlaybackCacheAsync(CancellationToken cancellationToken = default)
    {
        // No-op as TempPlayback is no longer used
        return Task.CompletedTask;
    }

    public async Task<List<DownloadItem>> GetDownloadsAsync(
        int _, // Parameter kept for interface compatibility but ignored
        CancellationToken cancellationToken = default)
    {
        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        // Use a single global key (0) for cleanup throttling
        if (!_lastCleanupUtcByProfile.TryGetValue(0, out var lastCleanupUtc) ||
            (DateTime.UtcNow - lastCleanupUtc).TotalSeconds >= 20)
        {
            await CleanupMissingCompletedDownloadsAsync(db, cancellationToken);
            _lastCleanupUtcByProfile[0] = DateTime.UtcNow;
        }

        // Phase 25: Return all downloads globally, not filtered by profile
        return await db.DownloadItems
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task FailActiveDownloadsForProfileAsync(
        int profileId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var activeIds = Array.Empty<int>();
        var ctsToCancel = new List<CancellationTokenSource>();

        // Serialize the claim/marker transition with the worker's initial
        // status claim and every worker-owned DB state write. Never await a
        // worker while holding this gate; the worker may need it to unwind.
        await _downloadStateGate.WaitAsync(cancellationToken);
        try
        {
            activeIds = await db.DownloadItems
                .AsNoTracking()
                .Where(d => d.ProfileId == profileId &&
                            (d.Status == DownloadStatus.Queued ||
                             d.Status == DownloadStatus.Downloading ||
                             d.Status == DownloadStatus.Paused))
                .Select(d => d.Id)
                .ToArrayAsync(cancellationToken);

            if (activeIds.Length == 0)
            {
                return;
            }

            foreach (var downloadId in activeIds)
            {
                // Mark before cancellation so a queued worker or a cancellation
                // continuation cannot start/write this item while the final Failed
                // state is being persisted.
                _credentialFailureRequestedIds[downloadId] = 1;
                if (_activeDownloadCts.TryGetValue(downloadId, out var cts))
                {
                    ctsToCancel.Add(cts);
                }
            }
        }
        finally
        {
            _downloadStateGate.Release();
        }

        // Cancellation can run continuations synchronously. Never invoke it
        // while _downloadStateGate is held: the worker's finally block may need
        // that gate to unwind. The worker may also have disposed a captured CTS
        // after leaving the gate, so a disposed token source is a benign race.
        foreach (var cts in ctsToCancel)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                _logger?.LogDebug("Download cancellation raced worker disposal.");
            }
            catch (Exception ex)
            {
                // Cancellation callbacks are allowed to fail independently;
                // do not let one worker abort the credential invalidation
                // pass before the remaining rows are persisted as Failed.
                _logger?.LogWarning(ex, "Download cancellation callback failed.");
            }
        }

        var workerTasks = new List<(int DownloadId, Task Worker)>();
        var persisted = false;
        try
        {
            foreach (var downloadId in activeIds)
            {
                await WaitForActiveWorkerAsync(downloadId, cancellationToken);
                if (_activeDownloadTasks.TryGetValue(downloadId, out var worker))
                {
                    workerTasks.Add((downloadId, worker));
                }
            }

            db.ChangeTracker.Clear();
            await _downloadStateGate.WaitAsync(cancellationToken);
            try
            {
                var activeItems = await db.DownloadItems
                    .Where(d => activeIds.Contains(d.Id) &&
                                (d.Status == DownloadStatus.Queued ||
                                 d.Status == DownloadStatus.Downloading ||
                                 d.Status == DownloadStatus.Paused))
                    .ToListAsync(cancellationToken);

                foreach (var item in activeItems)
                {
                    item.Status = DownloadStatus.Failed;
                    item.ErrorMessage = errorMessage;
                    item.UpdatedAt = DateTime.UtcNow;

                    _queuedIds.TryRemove(item.Id, out _);
                    _pauseRequestedIds.TryRemove(item.Id, out _);
                    _cancelRequestedIds.TryRemove(item.Id, out _);
                    _autoResumeAttempts.TryRemove(item.Id, out _);
                }

                const int maxAttempts = 5;
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        persisted = true;
                        break;
                    }
                    catch (Exception ex) when (
                        IsDatabaseBusyException(ex) && attempt < maxAttempts)
                    {
                        await Task.Delay(50 * attempt, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _downloadStateGate.Release();
            }
        }
        finally
        {
            // Cleanup is guaranteed even when cancellation or an unexpected
            // DB error happens before the final Failed write.
            foreach (var downloadId in activeIds)
            {
                var observed = workerTasks.FirstOrDefault(
                    entry => entry.DownloadId == downloadId);
                if (observed.Worker is not null && !observed.Worker.IsCompleted)
                {
                    _ = ObserveCredentialFailureWorkerAsync(downloadId, observed.Worker);
                }
                else if (_activeDownloadTasks.ContainsKey(downloadId) ||
                         _activeDownloadCts.ContainsKey(downloadId))
                {
                    _ = ObserveCredentialFailureWorkerAsync(downloadId, worker: null);
                }
                else
                {
                    _credentialFailureRequestedIds.TryRemove(downloadId, out _);
                }
            }
        }

        if (persisted)
        {
            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        }
    }

    public Task DeleteProfileDownloadsAsync(
        int profileId,
        CancellationToken cancellationToken = default)
        => DeleteAllDownloadsAsync(profileId, cancellationToken);

    public async Task DeleteAllDownloadsAsync(
        int profileId,
        CancellationToken cancellationToken = default)
    {
        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var downloadIds = await db.DownloadItems
            .Where(d => profileId <= 0 || d.ProfileId == profileId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        // Rows being removed by this same call must not be counted as poster
        // sharers; otherwise the last episode of a series would keep its poster
        // file (and file:// mapping) alive after every download is gone.
        var siblingDeletionIds = new HashSet<int>(downloadIds);

        foreach (var downloadId in downloadIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_activeDownloadCts.TryGetValue(downloadId, out var cts))
            {
                // The item is being downloaded right now. Cancel the worker but
                // DO NOT set the cancel flag: the worker's finally block must
                // not run its own cleanup here, because DeleteAllDownloadsAsync
                // performs it below — after the worker has fully unwound — so
                // the shared-series-poster logic sees the complete set of rows
                // being deleted. Awaiting the worker removes the previous race
                // where both sides deleted the same file/row concurrently.
                cts.Cancel();
                if (_activeDownloadTasks.TryGetValue(downloadId, out var workerTask))
                {
                    try
                    {
                        await workerTask;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "DeleteAllDownloadsAsync: awaited worker for {DownloadId}.", downloadId);
                    }
                }
            }
            else
            {
                // No worker is running; mark the row so a worker that starts in
                // the brief pre-registration window still cleans up after itself.
                _cancelRequestedIds[downloadId] = 1;
            }

            await RemoveDownloadArtifactsAndRecordWithRetryAsync(downloadId, cancellationToken, siblingDeletionIds);
        }

        CleanupEmptyDownloadDirectories();
        DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
    }

    public async Task DeleteDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default)
    {
        if (downloadId <= 0)
        {
            return;
        }

        _pauseRequestedIds.TryRemove(downloadId, out _);
        _cancelRequestedIds[downloadId] = 1;

        // If the item is actively downloading, cancel the worker first. The
        // worker's finally block detects the pending cancel flag, performs the
        // cleanup and raises DownloadsChanged exactly once.
        if (_activeDownloadCts.TryGetValue(downloadId, out var cts))
        {
            cts.Cancel();
            await WaitForActiveWorkerAsync(downloadId, cancellationToken);
            // The worker normally performs this cleanup in its finally block.
            // Repeat it after the worker has unwound so a cancellation that
            // raced a database write cannot leave a visible row behind.
            await RemoveDownloadArtifactsAndRecordWithRetryAsync(downloadId, cancellationToken);
            return;
        }

        await RemoveDownloadArtifactsAndRecordWithRetryAsync(downloadId, cancellationToken);
        DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
    }

    public Task CancelDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default)
        => DeleteDownloadAsync(downloadId, cancellationToken);

    public async Task PauseDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default)
    {
        if (downloadId <= 0)
        {
            return;
        }

        _pauseRequestedIds[downloadId] = 1;
        if (_activeDownloadCts.TryGetValue(downloadId, out var cts))
        {
            cts.Cancel();
            await WaitForActiveWorkerAsync(downloadId, cancellationToken);
            await MarkPausedWithRetryAsync(downloadId, cancellationToken);
            return;
        }

        await _downloadStateGate.WaitAsync(cancellationToken);
        try
        {
            if (_credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }

            using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
            if (item == null)
            {
                return;
            }

            if (item.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
            {
                item.Status = DownloadStatus.Paused;
                item.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
            }
        }
        finally
        {
            _downloadStateGate.Release();
        }
    }

    public async Task ResumeDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default)
    {
        if (downloadId <= 0)
        {
            return;
        }

        await _downloadStateGate.WaitAsync(cancellationToken);
        try
        {
            if (_credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }

            _pauseRequestedIds.TryRemove(downloadId, out _);

            using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
            if (item == null)
            {
                return;
            }

        // Phase 28: Enforce profile ownership. 
        // We do not have the current active profile ID here easily without changing the interface,
        // but the UI (MainViewModel) passes the profileId to GetDownloadsAsync.
        // For Resume, we will assume the caller ensures the profile is correct,
        // but we'll add a guard in MainViewModel before calling this.

        if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Failed)
        {
            return;
        }

        // A live duplicate (active, paused or completed) already occupies this
        // ContentKey. The unique index excludes Failed/Canceled rows, so waking
        // this stale Failed record would violate the constraint. Drop the stale
        // record and keep the live one.
        if (item.Status == DownloadStatus.Failed && !string.IsNullOrWhiteSpace(item.ContentKey))
        {
            var duplicateExists = await db.DownloadItems.AnyAsync(
                d => d.Id != downloadId &&
                     d.ContentKey == item.ContentKey &&
                     d.Status != DownloadStatus.Failed &&
                     d.Status != DownloadStatus.Canceled,
                cancellationToken);

            if (duplicateExists)
            {
                db.DownloadItems.Remove(item);
                await db.SaveChangesAsync(cancellationToken);
                DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
                return;
            }
        }

        // Manuel resume — otomatik yeniden deneme sayacını sıfırla
        _autoResumeAttempts.TryRemove(downloadId, out _);

        var isMissingFiles = item.BytesDownloaded > 0 && 
                             (!string.IsNullOrWhiteSpace(item.TempFilePath) && !File.Exists(item.TempFilePath)) && 
                             (!string.IsNullOrWhiteSpace(item.LocalFilePath) && !File.Exists(item.LocalFilePath));

        if (isMissingFiles)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = _localizationService.GetString("Download.Error.MissingFiles");
            item.BytesDownloaded = 0;
            item.SpeedBytesPerSecond = 0;
            item.EstimatedSecondsRemaining = null;
            item.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
            return;
        }

        item.Status = DownloadStatus.Queued;
        item.ErrorMessage = null;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (_queuedIds.TryAdd(downloadId, 1))
        {
            _pendingIds.Enqueue(downloadId);
            _queueSignal.Release();
        }

            EnsureQueueWorkerStarted();
            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        }
        finally
        {
            _downloadStateGate.Release();
        }
    }

    private void EnsureQueueWorkerStarted()
    {
        if (Interlocked.Exchange(ref _isQueueWorkerStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        await BootstrapPendingDownloadsAsync();

        while (true)
        {
            await _queueSignal.WaitAsync();
            if (!_pendingIds.TryDequeue(out var id))
            {
                continue;
            }

            _queuedIds.TryRemove(id, out _);
            var workerTask = ExecuteDownloadAsync(id);
            _activeDownloadTasks[id] = workerTask;
            try
            {
                await workerTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Download worker failed for {DownloadId}", id);
            }
            finally
            {
                _activeDownloadTasks.TryRemove(id, out _);
            }
        }
    }

    private async Task BootstrapPendingDownloadsAsync()
    {
        try
        {
            // Clean up any corrupted Completed downloads (e.g. 32-byte error text files
            // that were saved as video) before resuming the queue.
            await ReconcileInvalidCompletedDownloadsAsync();

            using var db = await _contextFactory.CreateDbContextAsync();
            await _downloadStateGate.WaitAsync();
            try
            {
                var pendingItems = await db.DownloadItems
                    .Where(d => d.Status == DownloadStatus.Queued ||
                                d.Status == DownloadStatus.Downloading ||
                                d.Status == DownloadStatus.Failed)
                    .OrderBy(d => d.CreatedAt)
                    .ToListAsync();

                var hasChanges = false;
                foreach (var item in pendingItems)
                {
                    if (_credentialFailureRequestedIds.ContainsKey(item.Id))
                    {
                        continue;
                    }

                    var isMissingFiles = item.BytesDownloaded > 0 &&
                                         (!string.IsNullOrWhiteSpace(item.TempFilePath) && !File.Exists(item.TempFilePath)) &&
                                         (!string.IsNullOrWhiteSpace(item.LocalFilePath) && !File.Exists(item.LocalFilePath));

                    if (isMissingFiles)
                    {
                        item.Status = DownloadStatus.Failed;
                        item.ErrorMessage = _localizationService.GetString("Download.Error.MissingFiles");
                        item.BytesDownloaded = 0;
                        item.SpeedBytesPerSecond = 0;
                        item.EstimatedSecondsRemaining = null;
                        item.UpdatedAt = DateTime.UtcNow;
                        hasChanges = true;
                        continue; // Do not add to queue
                    }

                    if (item.Status == DownloadStatus.Downloading)
                    {
                        // App unexpectedly closed; automatically resume by setting back to Queued
                        item.Status = DownloadStatus.Queued;
                        item.SpeedBytesPerSecond = 0;
                        item.EstimatedSecondsRemaining = null;
                        item.UpdatedAt = DateTime.UtcNow;
                        hasChanges = true;
                        // Fall through to add to memory queue below
                    }
                    else if (item.Status == DownloadStatus.Failed)
                    {
                        if (IsCredentialFailureMessage(item.ErrorMessage))
                        {
                            // Credential invalidation is a terminal user-visible
                            // failure until the user explicitly retries it. Do not
                            // turn its partial file into Paused on app startup.
                            continue;
                        }

                        var hasPartial = !string.IsNullOrWhiteSpace(item.TempFilePath) && File.Exists(item.TempFilePath);
                        if (hasPartial)
                        {
                            item.Status = DownloadStatus.Paused;
                            item.SpeedBytesPerSecond = 0;
                            item.EstimatedSecondsRemaining = null;
                            item.UpdatedAt = DateTime.UtcNow;
                            hasChanges = true;
                        }

                        continue;
                    }

                    if (_queuedIds.TryAdd(item.Id, 1))
                    {
                        _pendingIds.Enqueue(item.Id);
                        _queueSignal.Release();
                    }
                }

                if (hasChanges)
                {
                    await db.SaveChangesAsync();
                }

            }
            finally
            {
                _downloadStateGate.Release();
            }

            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BootstrapPendingDownloadsAsync failed.");
        }
    }

    private async Task ExecuteDownloadAsync(int downloadId)
    {
        using var startDb = await _contextFactory.CreateDbContextAsync();
        DownloadItem? item = null;
        await _downloadStateGate.WaitAsync();
        try
        {
            item = await startDb.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
            if (item == null ||
                item.Status == DownloadStatus.Completed ||
                item.Status == DownloadStatus.Canceled ||
                item.Status == DownloadStatus.Paused ||
                item.Status == DownloadStatus.Failed ||
                _credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }
        }
        finally
        {
            _downloadStateGate.Release();
        }

        if (item is null)
        {
            return;
        }

        // Wi-Fi policy is checked before the CTS is registered so a rejected
        // download can never leak a token in _activeDownloadCts.
        if (!TryCheckWifiPolicy(out var wifiMessage))
        {
            await MarkFailedAsync(downloadId, wifiMessage ?? string.Empty);
            return;
        }

        var settings = _settingsService.Settings;
        // Phase 25: Use a unified global directory, ignoring the profile parameter
        var profileDownloadDirectory = EnsureGlobalDownloadDirectory(settings.DownloadPath);
        var downloadDirectory = EnsureItemDownloadDirectory(profileDownloadDirectory, item);
        var extension = ResolveExtensionFromSource(item.SourceUrl);
        var safeName = BuildItemFileStem(item);
        var finalPath = string.IsNullOrWhiteSpace(item.LocalFilePath)
            ? CreateUniquePath(downloadDirectory, safeName, extension)
            : item.LocalFilePath!;
        var plainTempPath = string.IsNullOrWhiteSpace(item.TempFilePath)
            ? CreateUniquePath(downloadDirectory, safeName, extension + ".part")
            : item.TempFilePath!;
        var candidates = BuildDownloadCandidates(item.SourceUrl);
        var resumedBytes = File.Exists(plainTempPath) ? new FileInfo(plainTempPath).Length : 0L;
        if (resumedBytes < 0)
        {
            resumedBytes = 0;
        }

        var finalFileLength = File.Exists(finalPath) ? new FileInfo(finalPath).Length : 0L;
        if (finalFileLength > 0)
        {
            // Never mark a previously saved HLS/DASH manifest as Completed.
            // Also never mark a tiny/text error response as Completed.
            if (IsManifestFile(finalPath) || IsClearlyInvalidFinalFile(finalPath, finalFileLength))
            {
                TryDeleteFile(finalPath);
                // Fall through: re-download from scratch.
            }
            else
            {
                // Use the real size of the existing final file; resumedBytes
                // reflects the .part file and can be zero without one.
                await MarkCompletedAsync(downloadId, finalPath, finalFileLength, item.BytesTotal ?? finalFileLength, DateTime.UtcNow);
                return;
            }
        }

        if (item.BytesTotal.HasValue &&
            item.BytesTotal.Value > 0 &&
            File.Exists(plainTempPath))
        {
            var existingLength = new FileInfo(plainTempPath).Length;
            if (existingLength >= item.BytesTotal.Value || (item.BytesTotal.Value - existingLength < 1024 && existingLength > 1024 * 1024))
            {
                if (IsManifestFile(plainTempPath))
                {
                    TryDeleteFile(plainTempPath);
                }
                else
                {
                    File.Move(plainTempPath, finalPath, overwrite: true);
                    await MarkCompletedAsync(downloadId, finalPath, existingLength, item.BytesTotal, DateTime.UtcNow);
                    return;
                }
            }
        }

        // Registered only after every early-return path above, so a rejected or
        // already-complete download can never leak an entry in these dictionaries.
        var localCts = new CancellationTokenSource();
        var registered = false;
        await _downloadStateGate.WaitAsync();
        try
        {
            // The credential invalidation marker may have been set while the
            // worker was resolving paths. Recheck immediately before the first
            // Downloading write and CTS registration.
            if (_credentialFailureRequestedIds.ContainsKey(downloadId) ||
                item.Status is DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Completed)
            {
                return;
            }

            item.Status = DownloadStatus.Downloading;
            item.ErrorMessage = null;
            item.UpdatedAt = DateTime.UtcNow;
            item.BytesDownloaded = resumedBytes;
            item.SpeedBytesPerSecond = 0;
            item.EstimatedSecondsRemaining = null;
            item.LocalFilePath = finalPath;
            item.TempFilePath = plainTempPath;
            await startDb.SaveChangesAsync();

            _activeDownloadCts[downloadId] = localCts;
            _activeTempFiles[downloadId] = plainTempPath;
            registered = true;
        }
        finally
        {
            _downloadStateGate.Release();
            if (!registered)
            {
                localCts.Dispose();
            }
        }

        DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));

        // A DeleteAllDownloadsAsync that raced this CTS registration (the item
        // was not yet visible as active, so DeleteAll removed the record and
        // set the cancel flag) must not download into deleted paths. Honor the
        // flag here: the worker unwinds and its finally block cleans up.
        if (_cancelRequestedIds.ContainsKey(downloadId))
        {
            localCts.Cancel();
        }

        try
        {
            using var response = await SendFirstSuccessfulRequestAsync(candidates, resumedBytes, localCts.Token);
            if (response == null)
            {
                await MarkInterruptedAsPausedAsync(downloadId, _localizationService.GetString("Download.Error.NoResponse"));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => _localizationService.GetString("Download.Error.Unauthorized"),
                    System.Net.HttpStatusCode.NotFound => _localizationService.GetString("Download.Error.NotFound"),
                    _ => string.Format(_localizationService.GetString("Download.Error.ServerFormat"), (int)response.StatusCode)
                };
                
                // Clear any auto resume attempts so it doesn't loop
                _autoResumeAttempts.TryRemove(downloadId, out _);
                
                // Clean up any orphaned .part or final files before marking as failed
                await TryDeleteFileWithRetryAsync(plainTempPath, CancellationToken.None);
                await TryDeleteFileWithRetryAsync(finalPath, CancellationToken.None);
                
                await MarkFailedAsync(downloadId, errorMsg);
                return;
            }

            if (IsSegmentedManifestContentType(response.Content.Headers.ContentType?.MediaType))
            {
                TryDeleteFile(plainTempPath);
                TryDeleteFile(finalPath);
                _cancelRequestedIds[downloadId] = 1;
                return;
            }

            var supportsRange = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (resumedBytes > 0 && !supportsRange)
            {
                resumedBytes = 0;
                TryDeleteFile(plainTempPath);
            }

            var totalBytes = ResolveTotalBytes(response, resumedBytes);
            long downloaded = resumedBytes;
            var startedAt = DateTime.UtcNow;
            var lastPersistTick = DateTime.UtcNow;
            long lastPersistedBytes = resumedBytes;

            await using (var sourceStream = await response.Content.ReadAsStreamAsync(localCts.Token))
            {
                // Sniff the head of the body: servers often serve HLS/DASH
                // manifests with a generic content type (application/octet-stream)
                // or from extension-less URLs, so headers alone are not enough.
                var head = new byte[4096];
                var headRead = resumedBytes > 0
                    ? 0
                    : await sourceStream.ReadAsync(head.AsMemory(0, head.Length), localCts.Token);
                if (IsManifestContentKind(DetectContentKindFromContent(head.AsSpan(0, headRead))))
                {
                    TryDeleteFile(plainTempPath);
                    TryDeleteFile(finalPath);
                    _cancelRequestedIds[downloadId] = 1;
                    return;
                }

                // Detect servers that return HTTP 200 with an error/text body
                // (e.g. JSON {"error":"stream unavailable"} or HTML error pages).
                // Treat as a transient server-side problem → Paused so the user
                // can resume once the source is fixed.
                if (headRead > 0 &&
                    IsClearlyInvalidMediaResponse(
                        response.Content.Headers.ContentType?.MediaType,
                        head.AsSpan(0, headRead),
                        totalBytes))
                {
                    TryDeleteFile(plainTempPath);
                    TryDeleteFile(finalPath);
                    await MarkInterruptedAsPausedAsync(
                        downloadId,
                        _localizationService.GetString("Download.Error.InvalidServerResponse"));
                    return;
                }

                await using (var output = new FileStream(
                                 plainTempPath,
                                 resumedBytes > 0 ? FileMode.Append : FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 1024 * 64,
                                 true))
                {
                    if (headRead > 0)
                    {
                        await output.WriteAsync(head.AsMemory(0, headRead), localCts.Token);
                        downloaded += headRead;
                    }

                    var buffer = new byte[1024 * 64];
                    long lastBytes = resumedBytes;
                    var lastTick = DateTime.UtcNow;

                    while (true)
                    {
                        localCts.Token.ThrowIfCancellationRequested();
                        var read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), localCts.Token);
                        if (read <= 0)
                        {
                            break;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), localCts.Token);
                        downloaded += read;

                        var now = DateTime.UtcNow;
                        if ((now - lastTick).TotalMilliseconds >= 800)
                        {
                            var deltaBytes = downloaded - lastBytes;
                            var deltaSeconds = Math.Max(0.2, (now - lastTick).TotalSeconds);
                            var speed = deltaBytes / deltaSeconds;
                            var eta = speed > 0 && totalBytes.HasValue
                                ? (int?)Math.Max(0, (int)Math.Ceiling((totalBytes.Value - downloaded) / speed))
                                : null;
                            var shouldPersistByTime = (now - lastPersistTick).TotalMilliseconds >= ProgressPersistIntervalMs;
                            var shouldPersistByDelta = downloaded - lastPersistedBytes >= ProgressPersistMinDeltaBytes;
                            if (shouldPersistByTime || shouldPersistByDelta)
                            {
                                await _downloadStateGate.WaitAsync(localCts.Token);
                                try
                                {
                                    if (_credentialFailureRequestedIds.ContainsKey(downloadId))
                                    {
                                        return;
                                    }

                                    item.BytesDownloaded = downloaded;
                                    item.BytesTotal = totalBytes;
                                    item.SpeedBytesPerSecond = speed;
                                    item.EstimatedSecondsRemaining = eta;
                                    item.UpdatedAt = DateTime.UtcNow;
                                    await startDb.SaveChangesAsync(localCts.Token);
                                }
                                finally
                                {
                                    _downloadStateGate.Release();
                                }

                                DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Progress));
                                lastPersistTick = now;
                                lastPersistedBytes = downloaded;
                            }

                            lastTick = now;
                            lastBytes = downloaded;
                        }
                    }

                    await output.FlushAsync(localCts.Token);
                }
            }

            if (downloaded <= 0)
            {
                await MarkFailedAsync(downloadId, _localizationService.GetString("Download.Error.EmptyFile"));
                TryDeleteFile(plainTempPath);
                TryDeleteFile(finalPath);
                return;
            }

            if (totalBytes.HasValue && totalBytes.Value > 0 && downloaded < totalBytes.Value)
            {
                var missingBytes = totalBytes.Value - downloaded;
                var percent = (double)downloaded / totalBytes.Value;

                var tolerance = _settingsService.Settings.DownloadCompletionTolerance;

                // If we're extremely close (e.g. within 10KB or > tolerance for large files), treat as complete.
                // This handles servers that report slightly larger Content-Length than actual stream data.
                if (missingBytes < 1024 * 10 || (downloaded > 1024 * 1024 * 5 && percent > tolerance))
                {
                    _logger?.LogInformation("Download {DownloadId} finished with minor delta ({Missing} bytes). Treating as completed.", downloadId, missingBytes);
                }
                else
                {
                    _logger?.LogWarning(
                        "Download response ended early for item {DownloadId}. Downloaded={Downloaded}, Expected={Expected}",
                        downloadId,
                        downloaded,
                        totalBytes.Value);

                    await TryAutoResumeAfterTransientInterruptionAsync(
                        downloadId,
                        string.Format(_localizationService.GetString("Download.Error.EarlyEndFormat"), FormatBytes(downloaded), FormatBytes(totalBytes.Value)));
                    return;
                }
            }

            File.Move(plainTempPath, finalPath, overwrite: true);
            _autoResumeAttempts.TryRemove(downloadId, out _);

            // Final safety net: never mark a manifest text file as Completed.
            if (IsManifestFile(finalPath))
            {
                TryDeleteFile(finalPath);
                _cancelRequestedIds[downloadId] = 1;
                return;
            }

            await MarkCompletedAsync(downloadId, finalPath, downloaded, totalBytes, startedAt);
        }
        catch (OperationCanceledException ex)
        {
            var pausedRequested = _pauseRequestedIds.TryRemove(downloadId, out _);
            var cancelRequested = _cancelRequestedIds.ContainsKey(downloadId);
            var credentialFailureRequested = _credentialFailureRequestedIds.ContainsKey(downloadId);
            if (credentialFailureRequested)
            {
                // Profile credential-change invalidation owns the final Failed
                // write. Do not race it with a Paused update from cancellation.
            }
            else if (pausedRequested)
            {
                await MarkPausedAsync(downloadId);
            }
            else if (!localCts.IsCancellationRequested && IsTransientDownloadException(ex))
            {
                await TryAutoResumeAfterTransientInterruptionAsync(downloadId, ex.Message);
            }
            else if (!cancelRequested)
            {
                // Network timeout/interruption can also throw OperationCanceledException.
                // Do not delete artifacts unless user explicitly canceled.
                await MarkInterruptedAsPausedAsync(downloadId, _localizationService.GetString("Download.Error.Disconnected"));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Download failed for item {DownloadId}", downloadId);
            if (_credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }

            if (IsTransientDownloadException(ex))
            {
                await TryAutoResumeAfterTransientInterruptionAsync(downloadId, ex.Message);
            }
            else
            {
                _autoResumeAttempts.TryRemove(downloadId, out _);
                await MarkInterruptedAsPausedAsync(downloadId, UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Download.Error.Stopped"), ex));
            }
        }
        finally
        {
            _activeTempFiles.TryRemove(downloadId, out _);
            if (_activeDownloadCts.TryRemove(downloadId, out var activeCts))
            {
                activeCts.Dispose();
            }

            var ownsCancellationCleanup = false;
            await _downloadStateGate.WaitAsync();
            try
            {
                if (!_credentialFailureRequestedIds.ContainsKey(downloadId) &&
                    _cancelRequestedIds.TryRemove(downloadId, out _))
                {
                    ownsCancellationCleanup = true;
                    // Keep the gate until the row-removing transaction commits;
                    // a credential invalidation cannot claim the same row in
                    // the middle of this ownership decision.
                    await RemoveDownloadArtifactsAndRecordWithRetryAsync(
                        downloadId,
                        CancellationToken.None);
                }
            }
            finally
            {
                _downloadStateGate.Release();
            }

            if (ownsCancellationCleanup)
            {
                DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
            }
        }
    }

    private async Task<HttpResponseMessage?> SendFirstSuccessfulRequestAsync(
        IReadOnlyList<string> candidates,
        long startOffset,
        CancellationToken cancellationToken)
    {
        Exception? lastTransientException = null;

        foreach (var candidate in candidates)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                if (startOffset > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startOffset, null);
                }

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return response; // Return fatal errors to be handled by the caller
                }

                response.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientDownloadException(ex))
            {
                lastTransientException = ex;
            }
            catch
            {
                // Try next candidate.
            }
        }

        if (lastTransientException != null)
        {
            throw lastTransientException;
        }

        return null;
    }

    private async Task TryAutoResumeAfterTransientInterruptionAsync(int downloadId, string detail)
    {
        if (_credentialFailureRequestedIds.ContainsKey(downloadId))
        {
            return;
        }

        var attempt = _autoResumeAttempts.AddOrUpdate(downloadId, 1, static (_, current) => current + 1);
        if (attempt > MaxAutoResumeAttempts)
        {
            _autoResumeAttempts.TryRemove(downloadId, out _);
            await MarkInterruptedAsPausedAsync(
                downloadId,
                _localizationService.GetString("Download.Error.TooManyRetries"));
            return;
        }

        var safeDetail = UserFriendlyErrorMessage.FromText(detail);
        await MarkInterruptedAsPausedAsync(
            downloadId,
            string.Format(_localizationService.GetString("Download.Status.AutoResumingFormat"), attempt, MaxAutoResumeAttempts, safeDetail));

        var delayMs = Math.Min(4500, 1200 * attempt);
        await Task.Delay(delayMs);
        if (_credentialFailureRequestedIds.ContainsKey(downloadId))
        {
            return;
        }

        await ResumeDownloadAsync(downloadId, CancellationToken.None);
    }

    internal static bool IsTransientDownloadException(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is HttpRequestException or IOException or SocketException or TimeoutException)
            {
                return true;
            }

            var text = current.Message ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var lowered = text.ToLowerInvariant();
                if (lowered.Contains("response ended prematurely", StringComparison.Ordinal) ||
                    lowered.Contains("response ended", StringComparison.Ordinal) ||
                    lowered.Contains("unexpected end", StringComparison.Ordinal) ||
                    lowered.Contains("incomplete", StringComparison.Ordinal) ||
                    lowered.Contains("end of stream", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }

    internal static bool IsSegmentedManifestUrl(string? sourceUrl)
        => DetectContentKindFromUrl(sourceUrl) is DownloadContentKind.Hls or DownloadContentKind.Dash;

    internal static bool IsSegmentedManifestContentType(string? mediaType)
        => DetectContentKindFromContentType(mediaType) is DownloadContentKind.Hls or DownloadContentKind.Dash;

    /// <summary>
    /// Classifies a download target so HLS/DASH streams (which need a segment
    /// download engine) can be rejected instead of saving a useless manifest
    /// text file and marking it Completed.
    /// </summary>
    internal static DownloadContentKind DetectContentKindFromUrl(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return DownloadContentKind.Unsupported;
        }

        var normalized = sourceUrl.Trim().Trim('"', '\'');
        var path = normalized;
        string? query = null;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
            query = uri.Query;
        }

        var queryStart = path.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadContentKind.Hls;
        }

        if (path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadContentKind.Dash;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return DownloadContentKind.DirectFile;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separator]);
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            if (key.Equals("format", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("output", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("container", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("extension", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Equals("m3u8", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("m3u", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("hls", StringComparison.OrdinalIgnoreCase))
                {
                    return DownloadContentKind.Hls;
                }

                if (value.Equals("mpd", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("dash", StringComparison.OrdinalIgnoreCase))
                {
                    return DownloadContentKind.Dash;
                }
            }
        }

        return DownloadContentKind.DirectFile;
    }

    internal static DownloadContentKind DetectContentKindFromContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return DownloadContentKind.DirectFile;
        }

        var normalized = mediaType.Trim();
        if (normalized.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("application/mpegurl", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("audio/x-mpegurl", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadContentKind.Hls;
        }

        if (normalized.Equals("application/dash+xml", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadContentKind.Dash;
        }

        return DownloadContentKind.DirectFile;
    }

    /// <summary>
    /// Sniffs the leading bytes of a response/file body. Servers frequently
    /// serve manifests with a generic media type (e.g. application/octet-stream)
    /// or from extension-less URLs, so the headers alone are not enough.
    /// </summary>
    internal static DownloadContentKind DetectContentKindFromContent(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
        {
            return DownloadContentKind.DirectFile;
        }

        var span = head;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }
        else if (span.Length >= 2 &&
                 ((span[0] == 0xFF && span[1] == 0xFE) || (span[0] == 0xFE && span[1] == 0xFF)))
        {
            span = span[2..];
        }

        // HLS playlists always start with the #EXTM3U tag; media playlists
        // also carry #EXTINF lines.
        if (StartsWithAscii(span, "#EXTM3U") || StartsWithAscii(span, "#EXTINF"))
        {
            return DownloadContentKind.Hls;
        }

        // DASH manifests are XML documents with an <MPD> root element.
        if (ContainsAscii(span, "<mpd"))
        {
            return DownloadContentKind.Dash;
        }

        return DownloadContentKind.DirectFile;
    }

    internal static bool IsManifestContentKind(DownloadContentKind kind)
        => kind is DownloadContentKind.Hls or DownloadContentKind.Dash;

    private static bool IsManifestFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[4096];
            var read = fs.Read(head, 0, head.Length);
            return IsManifestContentKind(DetectContentKindFromContent(head.AsSpan(0, read)));
        }
        catch
        {
            return false;
        }
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            var b = data[i];
            if (b > 127)
            {
                return false;
            }

            if (char.ToLowerInvariant((char)b) != char.ToLowerInvariant(prefix[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> data, string pattern)
    {
        if (data.Length < pattern.Length)
        {
            return false;
        }

        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                var b = data[i + j];
                if (b > 127 || char.ToLowerInvariant((char)b) != char.ToLowerInvariant(pattern[j]))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static long? ResolveTotalBytes(HttpResponseMessage response, long resumedBytes)
    {
        if (response.Content.Headers.ContentRange?.Length is long rangedTotal && rangedTotal > 0)
        {
            return rangedTotal;
        }

        if (response.Content.Headers.ContentLength is long length && length > 0)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && resumedBytes > 0)
            {
                return resumedBytes + length;
            }

            return length;
        }

        return null;
    }

    private async Task RemoveDownloadArtifactsAndRecordWithRetryAsync(
        int downloadId,
        CancellationToken cancellationToken,
        ISet<int>? siblingDeletionIds = null)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RemoveDownloadArtifactsAndRecordAsync(
                    downloadId,
                    cancellationToken,
                    siblingDeletionIds);
                return;
            }
            catch (Exception ex) when (IsDatabaseBusyException(ex) && attempt < maxAttempts)
            {
                await Task.Delay(50 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RemoveDownloadArtifactsAndRecordAsync(
        int downloadId,
        CancellationToken cancellationToken,
        ISet<int>? siblingDeletionIds = null)
    {
        _pauseRequestedIds.TryRemove(downloadId, out _);
        _cancelRequestedIds.TryRemove(downloadId, out _);
        _queuedIds.TryRemove(downloadId, out _);
        _autoResumeAttempts.TryRemove(downloadId, out _);

        string? dirToCheck = null;
        string? mediaPath = null;
        string? posterPathToDelete = null;
        string? tempPath = null;

        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
        if (item != null)
        {
            var posterPath = ResolveLocalPosterPath(item);

            // A series poster is shared by every downloaded episode of that
            // series. Only delete the file (and restore the remote poster URL)
            // when no other surviving download references the same poster path;
            // siblingDeletionIds lists rows being removed by the same
            // DeleteAllDownloadsAsync call, which therefore do not count.
            var posterShared = posterPath != null &&
                await IsPosterSharedByOtherItemAsync(db, item, posterPath, siblingDeletionIds, cancellationToken);

            // 1. Restore the original remote URLs (and posters) on the mapped
            //    Channel/Episode rows, then remove the download record — all in
            //    one transaction so a partial failure never leaves content
            //    pointing at a file that no longer exists.
            await RestoreMappedEntitiesToSourceUrlAsync(db, item, posterShared);

            // 2. Collect file paths to delete AFTER transaction commits
            if (!string.IsNullOrWhiteSpace(item.LocalFilePath))
            {
                mediaPath = item.LocalFilePath;
                dirToCheck = Path.GetDirectoryName(item.LocalFilePath);
            }

            if (posterPath != null && !posterShared)
            {
                posterPathToDelete = posterPath;
                dirToCheck ??= Path.GetDirectoryName(posterPath);
            }

            if (!string.IsNullOrWhiteSpace(item.TempFilePath))
            {
                tempPath = item.TempFilePath;
                dirToCheck ??= Path.GetDirectoryName(item.TempFilePath);
            }

            // 3. Remove the DownloadItem record and commit transaction BEFORE deleting files.
            //    This ensures that if file deletion fails, the DB record is already gone
            //    and won't point to missing files. File deletion is best-effort cleanup.
            db.DownloadItems.Remove(item);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // 4. Now delete the files (best-effort). If any fail, the DB is already consistent.
        if (mediaPath != null)
        {
            await TryDeleteFileWithRetryAsync(mediaPath, cancellationToken);
        }

        if (posterPathToDelete != null)
        {
            await TryDeleteFileWithRetryAsync(posterPathToDelete, cancellationToken);
        }

        if (tempPath != null)
        {
            await TryDeleteFileWithRetryAsync(tempPath, cancellationToken);
        }

        if (_activeTempFiles.TryRemove(downloadId, out var activeTempPath))
        {
            await TryDeleteFileWithRetryAsync(activeTempPath, cancellationToken);
            dirToCheck ??= Path.GetDirectoryName(activeTempPath);
        }

        TryDeleteEmptyParentDirectories(dirToCheck);
    }

    private async Task CleanupMissingCompletedDownloadsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var staleItems = await db.DownloadItems
            .Where(d => d.Status == DownloadStatus.Completed &&
                        string.IsNullOrWhiteSpace(d.LocalFilePath) == false)
            .ToListAsync(cancellationToken);

        if (staleItems.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var item in staleItems)
        {
            if (!File.Exists(item.LocalFilePath!))
            {
                await RestoreMappedEntitiesToSourceUrlAsync(db, item);
                db.DownloadItems.Remove(item);
                changed = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.TempFilePath))
            {
                await TryDeleteFileWithRetryAsync(item.TempFilePath, cancellationToken);
                item.TempFilePath = null;
                item.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        }
    }

    private async Task MarkCompletedAsync(
        int downloadId,
        string filePath,
        long downloaded,
        long? total,
        DateTime startedAtUtc)
    {
        if (_credentialFailureRequestedIds.ContainsKey(downloadId))
        {
            return;
        }

        await MarkCompletedCoreAsync(
            downloadId,
            filePath,
            downloaded,
            total,
            startedAtUtc);
    }

    private async Task MarkCompletedCoreAsync(
        int downloadId,
        string filePath,
        long downloaded,
        long? total,
        DateTime startedAtUtc)
    {
        _autoResumeAttempts.TryRemove(downloadId, out _);

        using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        // --- Poster Downloading Logic ---
        if ((item.ChannelType == ChannelType.Series || item.ChannelType == ChannelType.VOD) &&
            !string.IsNullOrWhiteSpace(item.PosterUrl) &&
            item.PosterUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var posterPath = BuildPosterPath(item, filePath);
                if (!File.Exists(posterPath))
                {
                    using var posterResponse = await _httpClient.GetAsync(
                        item.PosterUrl,
                        HttpCompletionOption.ResponseHeadersRead);
                    posterResponse.EnsureSuccessStatusCode();

                    if (posterResponse.Content.Headers.ContentLength is > MaxPosterBytes)
                    {
                        throw new InvalidDataException("Poster exceeds the download size limit.");
                    }

                    var posterBytes = await posterResponse.Content.ReadAsByteArrayAsync();
                    if (posterBytes.Length == 0 || posterBytes.Length > MaxPosterBytes)
                    {
                        throw new InvalidDataException("Poster has an invalid size.");
                    }

                    var posterTempPath = posterPath + ".part";
                    await File.WriteAllBytesAsync(posterTempPath, posterBytes);
                    File.Move(posterTempPath, posterPath, overwrite: true);
                }

                // MobileRemoteImage requires a canonical file URI for offline posters.
                item.PosterUrl = ToFileUri(posterPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to download poster for completed item: {Url}", item.PosterUrl);
            }
        }
        // --------------------------------

        await _downloadStateGate.WaitAsync();
        try
        {
            // Poster/network work above intentionally runs outside the gate.
            // Recheck immediately before the terminal DB writes so a profile
            // credential invalidation can win while poster loading is pending.
            if (_credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }

            item.Status = DownloadStatus.Completed;
            item.LocalFilePath = filePath;
            item.TempFilePath = null;
            item.BytesDownloaded = downloaded;
            item.BytesTotal = total ?? downloaded;
            var elapsed = Math.Max(0.5, (DateTime.UtcNow - startedAtUtc).TotalSeconds);
            item.SpeedBytesPerSecond = downloaded / elapsed;
            item.EstimatedSecondsRemaining = 0;
            item.CompletedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            item.ErrorMessage = null;
            await db.SaveChangesAsync();

            await UpdateMappedEntitiesToLocalPathAsync(db, item, filePath);
            await db.SaveChangesAsync();
        }
        finally
        {
            _downloadStateGate.Release();
        }

        DownloadCompleted?.Invoke(this, item);
        DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
    }

    private async Task MarkPausedAsync(int downloadId)
        => await MarkPausedWithRetryAsync(downloadId, CancellationToken.None);

    private async Task MarkPausedWithRetryAsync(
        int downloadId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var retry = false;
            await _downloadStateGate.WaitAsync(cancellationToken);
            try
            {
                if (_credentialFailureRequestedIds.ContainsKey(downloadId))
                {
                    return;
                }

                using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var item = await db.DownloadItems.FirstOrDefaultAsync(
                    d => d.Id == downloadId,
                    cancellationToken);
                if (item == null)
                {
                    return;
                }

                if (item.Status is not DownloadStatus.Completed and not DownloadStatus.Canceled)
                {
                    item.Status = DownloadStatus.Paused;
                    item.SpeedBytesPerSecond = 0;
                    item.EstimatedSecondsRemaining = null;
                    item.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
                }

                return;
            }
            catch (Exception ex) when (IsDatabaseBusyException(ex) && attempt < maxAttempts)
            {
                retry = true;
            }
            finally
            {
                _downloadStateGate.Release();
            }

            if (retry)
            {
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }
    }

    private async Task WaitForActiveWorkerAsync(
        int downloadId,
        CancellationToken cancellationToken)
    {
        // The CTS is registered just before the first network await, while
        // the worker-task dictionary is populated by the queue loop one line
        // later. Cover that tiny registration window before falling back to a
        // bounded wait for the worker to unwind.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_activeDownloadTasks.TryGetValue(downloadId, out var workerTask))
            {
                try
                {
                    // A provider can leave an HTTP read pending even after its
                    // cancellation token is signalled. Never keep the UI
                    // command disabled until that network operation decides to
                    // return; the caller will persist Paused/Canceled below and
                    // the worker will observe the token when it unwinds.
                    var completed = await Task.WhenAny(
                        workerTask,
                        Task.Delay(TimeSpan.FromSeconds(1), cancellationToken))
                        .ConfigureAwait(false);

                    if (!ReferenceEquals(completed, workerTask))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _logger?.LogDebug(
                            "Timed out waiting for download worker {DownloadId} to stop.",
                            downloadId);
                        return;
                    }

                    await workerTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Download worker stopped after pause for {DownloadId}.", downloadId);
                }

                return;
            }

            if (!_activeDownloadCts.ContainsKey(downloadId))
            {
                return;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private async Task ObserveCredentialFailureWorkerAsync(
        int downloadId,
        Task? worker)
    {
        try
        {
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }
            else
            {
                while (_activeDownloadTasks.ContainsKey(downloadId) ||
                       _activeDownloadCts.ContainsKey(downloadId))
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(
                ex,
                "Credential-failure worker observation ended for {DownloadId}.",
                downloadId);
        }
        finally
        {
            // Keep the tombstone until the worker has truly exited. A late
            // cancellation/progress/completion continuation must never revive
            // or overwrite the Failed row after this method returns.
            _credentialFailureRequestedIds.TryRemove(downloadId, out _);
        }
    }

    private static bool IsDatabaseBusyException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException sqlite &&
                (sqlite.SqliteErrorCode == 5 || sqlite.SqliteExtendedErrorCode == 261))
            {
                return true;
            }

            if (current.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCredentialFailureMessage(string? message)
        => message?.StartsWith(
            CredentialChangeFailureMessage,
            StringComparison.Ordinal) == true;

    private async Task MarkInterruptedAsPausedAsync(int downloadId, string message)
    {
        await _downloadStateGate.WaitAsync();
        try
        {
            if (_credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
            if (item == null)
            {
                return;
            }

            item.Status = DownloadStatus.Paused;
            item.ErrorMessage = message;
            item.SpeedBytesPerSecond = 0;
            item.EstimatedSecondsRemaining = null;
            item.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        }
        finally
        {
            _downloadStateGate.Release();
        }
    }

    private static async Task UpdateMappedEntitiesToLocalPathAsync(AppDbContext db, DownloadItem item, string localPath)
    {
        var localPoster = !string.IsNullOrWhiteSpace(item.PosterUrl) && item.PosterUrl.StartsWith("file://") 
            ? item.PosterUrl 
            : (!string.IsNullOrWhiteSpace(item.PosterUrl) && Path.IsPathRooted(item.PosterUrl) ? $"file://{item.PosterUrl}" : null);

        if (item.ChannelType == ChannelType.VOD)
        {
            if (item.ChannelId.HasValue)
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == item.ChannelId.Value);
                if (channel != null)
                {
                    channel.StreamUrl = localPath;
                    if (localPoster != null) channel.LogoUrl = localPoster;
                }
            }
            else
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c =>
                    c.PlaylistId == item.PlaylistId &&
                    c.Type == ChannelType.VOD &&
                    c.StreamUrl == item.SourceUrl);
                if (channel != null)
                {
                    channel.StreamUrl = localPath;
                    if (localPoster != null) channel.LogoUrl = localPoster;
                }
            }

            return;
        }

        if (item.EpisodeId.HasValue)
        {
            var episode = await db.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s!.Series)
                .FirstOrDefaultAsync(e => e.Id == item.EpisodeId.Value);
                
            if (episode != null)
            {
                episode.StreamUrl = localPath;
                if (localPoster != null) 
                {
                    episode.CoverUrl = localPoster;
                    if (episode.Season != null)
                    {
                        episode.Season.CoverUrl = localPoster;
                        if (episode.Season.Series != null)
                        {
                            episode.Season.Series.CoverUrl = localPoster;
                        }
                    }
                }
            }
        }
        else
        {
            var episode = await db.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s!.Series)
                .FirstOrDefaultAsync(e => e.StreamUrl == item.SourceUrl);
                
            if (episode != null)
            {
                episode.StreamUrl = localPath;
                if (localPoster != null) 
                {
                    episode.CoverUrl = localPoster;
                    if (episode.Season != null)
                    {
                        episode.Season.CoverUrl = localPoster;
                        if (episode.Season.Series != null)
                        {
                            episode.Season.Series.CoverUrl = localPoster;
                        }
                    }
                }
            }
        }

        var seriesChannels = await db.Channels
            .Where(c => c.Type == ChannelType.Series && c.StreamUrl == item.SourceUrl && c.PlaylistId == item.PlaylistId)
            .ToListAsync();
        foreach (var channel in seriesChannels)
        {
            channel.StreamUrl = localPath;
            if (localPoster != null) channel.LogoUrl = localPoster;
        }
    }

    private static async Task RestoreMappedEntitiesToSourceUrlAsync(
        AppDbContext db,
        DownloadItem item,
        bool keepSharedPoster = false)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            return;
        }

        // When the poster file survives (shared by another episode), the mapped
        // entities must keep the local file URI instead of restoring the remote
        // poster URL, which would break their offline poster.
        string? RestorePoster(string? current)
            => keepSharedPoster ? current : RestoreMappedPoster(current, item);

        if (item.ChannelType == ChannelType.VOD)
        {
            if (item.ChannelId.HasValue)
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == item.ChannelId.Value);
                if (channel != null)
                {
                    channel.StreamUrl = item.SourceUrl;
                    channel.LogoUrl = RestorePoster(channel.LogoUrl);
                }
            }
            else
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c =>
                    c.PlaylistId == item.PlaylistId &&
                    c.Type == ChannelType.VOD &&
                    c.StreamUrl == item.LocalFilePath);
                if (channel != null)
                {
                    channel.StreamUrl = item.SourceUrl;
                    channel.LogoUrl = RestorePoster(channel.LogoUrl);
                }
            }

            return;
        }

        if (item.EpisodeId.HasValue)
        {
            var episode = await db.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s!.Series)
                .FirstOrDefaultAsync(e => e.Id == item.EpisodeId.Value);
            if (episode != null)
            {
                episode.StreamUrl = item.SourceUrl;
                episode.CoverUrl = RestorePoster(episode.CoverUrl);
                if (episode.Season != null)
                {
                    episode.Season.CoverUrl = RestorePoster(episode.Season.CoverUrl);
                    if (episode.Season.Series != null)
                    {
                        episode.Season.Series.CoverUrl = RestorePoster(episode.Season.Series.CoverUrl);
                    }
                }
            }
        }
        else
        {
            var episode = await db.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s!.Series)
                .FirstOrDefaultAsync(e => e.StreamUrl == item.LocalFilePath);
            if (episode != null)
            {
                episode.StreamUrl = item.SourceUrl;
                episode.CoverUrl = RestorePoster(episode.CoverUrl);
                if (episode.Season != null)
                {
                    episode.Season.CoverUrl = RestorePoster(episode.Season.CoverUrl);
                    if (episode.Season.Series != null)
                    {
                        episode.Season.Series.CoverUrl = RestorePoster(episode.Season.Series.CoverUrl);
                    }
                }
            }
        }

        var linkedSeriesChannels = await db.Channels
            .Where(c => c.Type == ChannelType.Series &&
                        c.PlaylistId == item.PlaylistId &&
                        c.StreamUrl == item.LocalFilePath)
            .ToListAsync();
        foreach (var channel in linkedSeriesChannels)
        {
            channel.StreamUrl = item.SourceUrl;
            channel.LogoUrl = RestorePoster(channel.LogoUrl);
        }
    }

    /// <summary>
    /// Returns true when another surviving download (not scheduled for deletion
    /// by the same operation) maps to the same poster file, which happens for
    /// every episode of a series sharing one folder-level poster.jpg.
    /// </summary>
    private static async Task<bool> IsPosterSharedByOtherItemAsync(
        AppDbContext db,
        DownloadItem item,
        string posterPath,
        ISet<int>? siblingDeletionIds,
        CancellationToken cancellationToken)
    {
        // Performance optimization: Instead of loading ALL DownloadItems into memory
        // and comparing poster paths in C#, we narrow the search based on content type.
        // Series episodes share posters at the series level; movies/VOD rarely share.
        
        if (item.ChannelType != ChannelType.Series)
        {
            // For non-series content (movies/VOD), poster sharing is rare.
            // Each movie typically has its own poster file. Skip the expensive check.
            return false;
        }

        // For series: poster is shared by episodes of the same series in the same profile.
        // Legacy rows may lack SeriesId; fall back to comparing poster paths against
        // every surviving series download in the profile.
        var seriesId = item.SeriesId;
        var candidates = db.DownloadItems
            .AsNoTracking()
            .Where(d => d.Id != item.Id &&
                        d.ChannelType == ChannelType.Series &&
                        d.ProfileId == item.ProfileId &&
                        d.Status != DownloadStatus.Failed &&
                        d.Status != DownloadStatus.Canceled &&
                        d.LocalFilePath != null);

        if (seriesId.HasValue && seriesId.Value > 0)
        {
            candidates = candidates.Where(d => d.SeriesId == seriesId);
        }

        var hasOtherEpisode = await candidates.AnyAsync(cancellationToken);

        if (!hasOtherEpisode)
        {
            return false;
        }

        // There are other episodes of the same series. Verify they actually use the same poster path.
        // This handles edge cases where SeriesId matches but file structure differs.
        var otherEpisodes = await candidates
            .Select(d => new { d.Id, d.LocalFilePath })
            .ToListAsync(cancellationToken);

        foreach (var other in otherEpisodes)
        {
            if (siblingDeletionIds?.Contains(other.Id) == true)
            {
                continue;
            }

            // Build the poster path for the other episode
            var otherPosterPath = other.LocalFilePath != null
                ? BuildPosterPathFromFilePath(other.LocalFilePath, ChannelType.Series)
                : null;

            if (otherPosterPath != null && PathsReferToSameFile(otherPosterPath, posterPath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds poster path from LocalFilePath without requiring the full DownloadItem.
    /// Used for performance optimization in IsPosterSharedByOtherItemAsync.
    /// </summary>
    private static string? BuildPosterPathFromFilePath(string filePath, ChannelType channelType)
    {
        var itemDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(itemDirectory))
        {
            return filePath + ".poster.jpg";
        }

        if (channelType == ChannelType.Series)
        {
            var seriesDirectory = Directory.GetParent(itemDirectory)?.FullName;
            return Path.Combine(seriesDirectory ?? itemDirectory, "poster.jpg");
        }

        var fileStem = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(itemDirectory, $"{fileStem}.poster.jpg");
    }

    /// <summary>
    /// Restores the poster a mapped entity had before the download overwrote it:
    /// back to the original remote poster when known, otherwise null when the
    /// current value is the (now deleted) local poster file.
    /// </summary>
    internal static string? RestoreMappedPoster(string? currentPoster, DownloadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePosterUrl))
        {
            return item.SourcePosterUrl;
        }

        var localPosterPath = ResolveLocalPosterPath(item);
        return localPosterPath != null && PathsReferToSameFile(currentPoster, localPosterPath)
            ? null
            : currentPoster;
    }

    private static string? ResolveLocalPosterPath(DownloadItem item)
        => string.IsNullOrWhiteSpace(item.LocalFilePath)
            ? null
            : BuildPosterPath(item, item.LocalFilePath);

    private static bool PathsReferToSameFile(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftPath = left.Trim().Trim('"', '\'');
        if (leftPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(leftPath, UriKind.Absolute, out var leftUri) &&
            leftUri.IsFile)
        {
            leftPath = leftUri.LocalPath;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(leftPath),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task MarkFailedAsync(int downloadId, string message)
    {
        await _downloadStateGate.WaitAsync();
        try
        {
            if (_credentialFailureRequestedIds.ContainsKey(downloadId))
            {
                return;
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
            if (item == null)
            {
                return;
            }

            // Keep the record in the DB as Failed so the user sees the error card
            // in the Downloads center and can retry or dismiss it explicitly.
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = message;
            item.SpeedBytesPerSecond = 0;
            item.EstimatedSecondsRemaining = null;
            item.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
        }
        finally
        {
            _downloadStateGate.Release();
        }
    }

    /// <summary>
    /// Returns true when the response body is clearly not a valid media file:
    /// JSON, HTML, plain-text error messages, or a suspiciously tiny payload.
    /// Used to detect HTTP 200 responses that carry an error body instead of video.
    /// </summary>
    internal static bool IsClearlyInvalidMediaResponse(
        string? contentType,
        ReadOnlySpan<byte> head,
        long? totalBytes)
    {
        var type = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        if (type.StartsWith("text/", StringComparison.Ordinal) ||
            type.Contains("json", StringComparison.Ordinal) ||
            type.Contains("xml", StringComparison.Ordinal) ||
            type.Contains("problem+", StringComparison.Ordinal))
        {
            return true;
        }

        // Strip UTF-8 / UTF-16 BOM before inspecting content
        var span = head;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }
        else if (span.Length >= 2 &&
                 ((span[0] == 0xFF && span[1] == 0xFE) ||
                  (span[0] == 0xFE && span[1] == 0xFF)))
        {
            span = span[2..];
        }

        if (StartsWithAscii(span, "{")           ||
            StartsWithAscii(span, "[")           ||
            StartsWithAscii(span, "<html")       ||
            StartsWithAscii(span, "<!doctype")   ||
            StartsWithAscii(span, "<?xml")       ||
            StartsWithAscii(span, "error")       ||
            StartsWithAscii(span, "invalid")     ||
            StartsWithAscii(span, "unauthorized") ||
            StartsWithAscii(span, "user not found"))
        {
            return true;
        }

        // A payload smaller than 64 KB that consists mostly of printable ASCII
        // is almost certainly a text/error response rather than media.
        if (totalBytes is > 0 and < 64 * 1024 && LooksMostlyLikeText(span))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when more than 80 % of the first 512 bytes are printable ASCII,
    /// which strongly suggests a text/error payload rather than binary media.
    /// </summary>
    private static bool LooksMostlyLikeText(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        var sample = span.Length > 512 ? span[..512] : span;
        var printable = 0;
        foreach (var b in sample)
        {
            if (b >= 0x20 && b < 0x7F)
            {
                printable++;
            }
        }

        return (double)printable / sample.Length > 0.80;
    }

    /// <summary>
    /// Returns true when a final (non-.part) file on disk is clearly invalid media:
    /// too small, looks like text, or is a manifest file.
    /// </summary>
    private static bool IsClearlyInvalidFinalFile(string filePath, long fileLength)
    {
        if (fileLength <= 0)
        {
            return true;
        }

        // Files below 64 KB are suspicious — real video files are always larger.
        if (fileLength < 64 * 1024)
        {
            try
            {
                using var fs = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var head = new byte[Math.Min((int)fileLength, 4096)];
                var read = fs.Read(head, 0, head.Length);
                return IsClearlyInvalidMediaResponse(
                    null,
                    head.AsSpan(0, read),
                    fileLength);
            }
            catch
            {
                return false; // Cannot read — assume valid to avoid data loss
            }
        }

        return false;
    }

    /// <summary>
    /// Scans all Completed downloads and resets any whose local file is missing,
    /// empty, or clearly an error-text payload (e.g. 32-byte HTTP 200 error body).
    /// Called once at startup before the queue is processed.
    /// </summary>
    private async Task ReconcileInvalidCompletedDownloadsAsync()
    {
        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            var completed = await db.DownloadItems
                .Where(d =>
                    d.Status == DownloadStatus.Completed &&
                    d.LocalFilePath != null)
                .ToListAsync();

            if (completed.Count == 0)
            {
                return;
            }

            var changed = false;
            var invalidMessage = _localizationService.GetString("Download.Error.InvalidDownloadedFile");

            foreach (var item in completed)
            {
                var path = item.LocalFilePath!;

                // File missing
                if (!File.Exists(path))
                {
                    await RestoreMappedEntitiesToSourceUrlAsync(db, item);
                    item.Status = DownloadStatus.Failed;
                    item.LocalFilePath = null;
                    item.TempFilePath = null;
                    item.BytesDownloaded = 0;
                    item.BytesTotal = null;
                    item.SpeedBytesPerSecond = 0;
                    item.EstimatedSecondsRemaining = null;
                    item.CompletedAt = null;
                    item.ErrorMessage = _localizationService.GetString("Download.Error.MissingFiles");
                    item.UpdatedAt = DateTime.UtcNow;
                    changed = true;
                    continue;
                }

                var fileInfo = new FileInfo(path);
                if (IsClearlyInvalidFinalFile(path, fileInfo.Length))
                {
                    _logger?.LogWarning(
                        "ReconcileInvalidCompletedDownloads: removing invalid file {Path} ({Size} bytes)",
                        path,
                        fileInfo.Length);

                    await RestoreMappedEntitiesToSourceUrlAsync(db, item);
                    TryDeleteFile(path);

                    item.Status = DownloadStatus.Failed;
                    item.LocalFilePath = null;
                    item.TempFilePath = null;
                    item.BytesDownloaded = 0;
                    item.BytesTotal = null;
                    item.SpeedBytesPerSecond = 0;
                    item.EstimatedSecondsRemaining = null;
                    item.CompletedAt = null;
                    item.ErrorMessage = invalidMessage;
                    item.UpdatedAt = DateTime.UtcNow;
                    changed = true;
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync();
                DownloadsChanged?.Invoke(this, new DownloadsChangedEventArgs(DownloadChangeKind.Structural));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ReconcileInvalidCompletedDownloadsAsync failed.");
        }
    }

    private static string? SerializeTrackList(IReadOnlyList<DownloadTrackOption>? items)
    {
        if (items == null || items.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    private static bool IsLocalFilePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(value.Trim(), @"^[a-zA-Z]:[\\/]")
               || value.StartsWith("/", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildDownloadCandidates(string sourceUrl) => [sourceUrl];

    private static string ResolveExtensionFromSource(string sourceUrl)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 8)
            {
                return ext;
            }
        }

        return ".mp4";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private string BuildSafeFileName(string rawName)
    {
        var value = string.IsNullOrWhiteSpace(rawName) ? _localizationService.GetString("Download.DefaultName") : rawName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        value = string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (value.Length > 120)
        {
            value = value[..120].Trim();
        }
        return value;
    }

    private static string CreateUniquePath(string directory, string fileNameWithoutExtension, string extension)
    {
        var candidate = Path.Combine(directory, fileNameWithoutExtension + extension);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; i <= 9999; i++)
        {
            var withSuffix = Path.Combine(directory, $"{fileNameWithoutExtension}_{i}{extension}");
            if (!File.Exists(withSuffix))
            {
                return withSuffix;
            }
        }

        return Path.Combine(directory, $"{fileNameWithoutExtension}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}");
    }

    private string EnsureGlobalDownloadDirectory(string? baseDownloadPath)
    {
        var basePath = _appPaths.NormalizeDownloadDirectory(baseDownloadPath);

        // Phase 25: No longer append "Profile_X", use the basePath directly as the global root
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }
        return basePath;
    }

    private string EnsureItemDownloadDirectory(string profilePath, DownloadItem item)
    {
        var category = item.ChannelType == ChannelType.Series ? "Series" : "Movies";
        var categoryPath = Path.Combine(profilePath, category);
        if (!Directory.Exists(categoryPath))
        {
            Directory.CreateDirectory(categoryPath);
        }

        if (item.ChannelType == ChannelType.Series)
        {
            // Folder structure must never depend on the visible episode name.
            // Prefer the structural metadata (SeriesTitle/SeasonNumber); the
            // DisplayName regex parse is only a fallback for legacy items.
            string seriesName;
            int seasonNumber;

            if (!string.IsNullOrWhiteSpace(item.SeriesTitle))
            {
                var structural = SeriesInfoParser.Parse(item.SeriesTitle);
                seriesName = BuildSafeFileName(structural.SeriesName);
                seasonNumber = item.SeasonNumber > 0
                    ? item.SeasonNumber
                    : structural.Season > 0 ? structural.Season : 1;
            }
            else
            {
                var parsed = SeriesInfoParser.Parse(item.DisplayName);
                seriesName = BuildSafeFileName(parsed.SeriesName);
                seasonNumber = parsed.Season > 0 ? parsed.Season : 1;
            }

            // Phase 27: Smart folder matching — scan existing series folders for a fuzzy match
            // so that "4400" from Provider A and "The 4400" from Provider B share the same folder.
            var seriesPath = FindMatchingSeriesFolder(categoryPath, seriesName)
                            ?? Path.Combine(categoryPath, seriesName);

            if (!Directory.Exists(seriesPath))
            {
                Directory.CreateDirectory(seriesPath);
            }

            // Phase 24: Use centralized parser for accurate Season folder grouping
            var sNum = seasonNumber > 0 ? seasonNumber : 1;
            var seasonPath = Path.Combine(seriesPath, $"Season {sNum:D2}");
            if (!Directory.Exists(seasonPath))
            {
                Directory.CreateDirectory(seasonPath);
            }
            return seasonPath;
        }

        return categoryPath;
    }

    /// <summary>
    /// Scans existing series folders under the category path and returns the first
    /// folder whose normalized name fuzzy-matches the given series name.
    /// Returns null if no match is found.
    /// </summary>
    private string? FindMatchingSeriesFolder(string categoryPath, string newSeriesName)
    {
        if (!Directory.Exists(categoryPath))
            return null;

        var normalizedNew = NormalizeFolderName(newSeriesName);
        if (string.IsNullOrWhiteSpace(normalizedNew))
            return null;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(categoryPath))
            {
                var existingName = Path.GetFileName(dir);
                var normalizedExisting = NormalizeFolderName(existingName);
                if (string.Equals(normalizedNew, normalizedExisting, StringComparison.OrdinalIgnoreCase))
                {
                    return dir; // Exact fuzzy match found — reuse this folder
                }
            }
        }
        catch
        {
            // IO errors are not critical; fall back to default behavior
        }

        return null;
    }

    /// <summary>
    /// Normalizes a folder/series name for fuzzy comparison by stripping non-alphanumeric chars,
    /// collapsing whitespace, and lowercasing.
    /// </summary>
    private static string NormalizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private string BuildItemFileStem(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.DisplayName)) return "download";

        // Phase 24: For Series, ensure the file name preserves critical SxE info but remains "BuildSafe"
        return BuildSafeFileName(item.DisplayName);
    }

    internal static string BuildPosterPath(DownloadItem item, string filePath)
    {
        var itemDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(itemDirectory))
        {
            return filePath + ".poster.jpg";
        }

        if (item.ChannelType == ChannelType.Series)
        {
            var seriesDirectory = Directory.GetParent(itemDirectory)?.FullName;
            return Path.Combine(seriesDirectory ?? itemDirectory, "poster.jpg");
        }

        var fileStem = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(itemDirectory, $"{fileStem}.poster.jpg");
    }

    private static string ToFileUri(string filePath)
    {
        try
        {
            return new Uri(Path.GetFullPath(filePath), UriKind.Absolute).AbsoluteUri;
        }
        catch
        {
            return $"file://{filePath}";
        }
    }



    private bool TryCheckWifiPolicy(out string? message)
    {
        message = null;
        var wifiOnly = _settingsService.Settings.DownloadWifiOnly;
        if (!wifiOnly)
        {
            return true;
        }

        if (_networkService is not null)
        {
            if (IsDownloadNetworkAllowed(wifiOnly, _networkService.CurrentNetworkStatus))
            {
                return true;
            }

            message = _localizationService.GetString("Download.Error.WifiOnly");
            return false;
        }

        var isUnmetered = NetworkInterface.GetAllNetworkInterfaces()
            .Any(i => i.OperationalStatus == OperationalStatus.Up &&
                      (i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                       i.NetworkInterfaceType == NetworkInterfaceType.Ethernet));

        if (!isUnmetered)
        {
            message = _localizationService.GetString("Download.Error.WifiOnly");
            return false;
        }

        return true;
    }

    internal static bool IsDownloadNetworkAllowed(bool wifiOnly, string? networkStatus)
    {
        if (!wifiOnly)
        {
            return true;
        }

        return string.Equals(networkStatus, "Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(networkStatus, "Ethernet", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore.
        }
    }

    private static async Task TryDeleteFileWithRetryAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            TryDeleteFile(path);
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stop retrying; the caller decides how to handle cancellation.
                return;
            }
        }
    }

    private void CleanupEmptyDownloadDirectories()
    {
        var root = EnsureGlobalDownloadDirectory(_settingsService.Settings.DownloadPath);
        foreach (var category in new[] { "Series", "Movies" })
        {
            var categoryPath = Path.Combine(root, category);
            if (Directory.Exists(categoryPath))
            {
                TryDeleteEmptyDirectoriesBottomUp(categoryPath);
            }
        }
    }

    private void TryDeleteEmptyParentDirectories(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return;
        }

        var root = EnsureGlobalDownloadDirectory(_settingsService.Settings.DownloadPath);
        var current = startDirectory;
        while (!string.IsNullOrWhiteSpace(current)
               && Directory.Exists(current)
               && IsPathInside(current, root)
               && !PathsEqual(current, root))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    return;
                }

                Directory.Delete(current, false);
                _logger?.LogInformation("Deleted empty download directory: {Dir}", current);
                current = Path.GetDirectoryName(current);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to delete potentially empty directory: {Dir}", current);
                return;
            }
        }
    }

    private void TryDeleteEmptyDirectoriesBottomUp(string directory)
    {
        try
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                TryDeleteEmptyDirectoriesBottomUp(child);
            }

            if (Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, false);
                _logger?.LogInformation("Deleted empty download directory: {Dir}", directory);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to cleanup empty download directory: {Dir}", directory);
        }
    }

    private static bool IsPathInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || PathsEqual(fullPath, fullRoot);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

}
