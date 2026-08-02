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

public class ContentDownloadService : IContentDownloadService
{
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
    private readonly ConcurrentQueue<int> _pendingIds = new();
    private readonly ConcurrentDictionary<int, byte> _queuedIds = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeDownloadCts = new();
    private readonly ConcurrentDictionary<int, string> _activeTempFiles = new();
    private readonly ConcurrentDictionary<int, byte> _pauseRequestedIds = new();
    private readonly ConcurrentDictionary<int, byte> _cancelRequestedIds = new();
    private readonly ConcurrentDictionary<int, int> _autoResumeAttempts = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastCleanupUtcByProfile = new();
    private int _isQueueWorkerStarted;

    public event EventHandler? DownloadsChanged;
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

        var duplicate = await db.DownloadItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.ProfileId == request.ProfileId &&
                     d.SourceUrl == normalizedSource &&
                     d.Status != DownloadStatus.Failed &&
                     d.Status != DownloadStatus.Canceled,
                cancellationToken);
        if (duplicate != null)
        {
            if (duplicate.IsCompleted)
            {
                return new DownloadContentResult(true, true, _localizationService.GetString("Download.Status.AlreadyDownloaded"), duplicate.Id);
            }

            return new DownloadContentResult(true, true, _localizationService.GetString("Download.Status.AlreadyInQueue"), duplicate.Id);
        }

        var item = new DownloadItem
        {
            ProfileId = request.ProfileId,
            PlaylistId = request.PlaylistId,
            ChannelId = request.ChannelId > 0 ? request.ChannelId : null,
            EpisodeId = request.EpisodeId > 0 ? request.EpisodeId : null,
            ChannelType = request.ItemType == DownloadItemType.SeriesEpisode ? ChannelType.Series : ChannelType.VOD,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? _localizationService.GetString("Download.DefaultName") : request.DisplayName.Trim(),
            PosterUrl = request.PosterUrl,
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

        db.DownloadItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        if (_queuedIds.TryAdd(item.Id, 1))
        {
            _pendingIds.Enqueue(item.Id);
            _queueSignal.Release();
        }

        EnsureQueueWorkerStarted();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
        return new DownloadContentResult(true, false, _localizationService.GetString("Download.Status.AddedToQueue"), item.Id);
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
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
        
        var activeItems = await db.DownloadItems
            .Where(d => d.ProfileId == profileId && 
                        (d.Status == DownloadStatus.Queued || 
                         d.Status == DownloadStatus.Downloading || 
                         d.Status == DownloadStatus.Paused))
            .ToListAsync(cancellationToken);

        if (activeItems.Count == 0) return;

        foreach (var item in activeItems)
        {
            // Cancel any running worker for this item
            if (_activeDownloadCts.TryGetValue(item.Id, out var cts))
            {
                cts.Cancel();
            }

            // Mark as Failed in DB
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = errorMessage;
            item.UpdatedAt = DateTime.UtcNow;
            
            _queuedIds.TryRemove(item.Id, out _);
            _pauseRequestedIds.TryRemove(item.Id, out _);
            _autoResumeAttempts.TryRemove(item.Id, out _);
        }

        await db.SaveChangesAsync(cancellationToken);
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteProfileDownloadsAsync(
        int profileId,
        CancellationToken cancellationToken = default)
    {
        if (profileId <= 0)
        {
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var downloadIds = await db.DownloadItems
            .Where(d => d.ProfileId == profileId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var downloadId in downloadIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cancelRequestedIds[downloadId] = 1;
            if (_activeDownloadCts.TryGetValue(downloadId, out var cts))
            {
                cts.Cancel();
            }

            await RemoveDownloadArtifactsAndRecordAsync(downloadId, cancellationToken);
        }

        CleanupEmptyDownloadDirectories();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CancelDownloadAsync(
        int downloadId,
        CancellationToken cancellationToken = default)
    {
        if (downloadId <= 0)
        {
            return;
        }

        _pauseRequestedIds.TryRemove(downloadId, out _);
        _cancelRequestedIds[downloadId] = 1;

        if (_activeDownloadCts.TryGetValue(downloadId, out var cts))
        {
            cts.Cancel();
            // UI responsiveness first: cleanup happens asynchronously in worker finally block.
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            await RemoveDownloadArtifactsAndRecordAsync(downloadId, cancellationToken);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
            try
            {
                await ExecuteDownloadAsync(id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Download worker failed for {DownloadId}", id);
            }
        }
    }

    private async Task BootstrapPendingDownloadsAsync()
    {
        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var pendingItems = await db.DownloadItems
                .Where(d => d.Status == DownloadStatus.Queued ||
                            d.Status == DownloadStatus.Downloading ||
                            d.Status == DownloadStatus.Failed)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();

            var hasChanges = false;
            foreach (var item in pendingItems)
            {
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

            DownloadsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BootstrapPendingDownloadsAsync failed.");
        }
    }

    private async Task ExecuteDownloadAsync(int downloadId)
    {
        var localCts = new CancellationTokenSource();
        _activeDownloadCts[downloadId] = localCts;
        using var startDb = await _contextFactory.CreateDbContextAsync();
        var item = await startDb.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null || 
            item.Status == DownloadStatus.Completed || 
            item.Status == DownloadStatus.Canceled ||
            item.Status == DownloadStatus.Paused)
        {
            _activeDownloadCts.TryRemove(downloadId, out _);
            localCts.Dispose();
            return;
        }

        if (!TryCheckWifiPolicy(out var wifiMessage))
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = wifiMessage;
            item.UpdatedAt = DateTime.UtcNow;
            await startDb.SaveChangesAsync();
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
        _activeTempFiles[downloadId] = plainTempPath;
        var candidates = BuildDownloadCandidates(item.SourceUrl);
        var resumedBytes = File.Exists(plainTempPath) ? new FileInfo(plainTempPath).Length : 0L;
        if (resumedBytes < 0)
        {
            resumedBytes = 0;
        }

        if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
        {
            await MarkCompletedAsync(downloadId, finalPath, resumedBytes, item.BytesTotal ?? resumedBytes, DateTime.UtcNow);
            return;
        }

        if (item.BytesTotal.HasValue &&
            item.BytesTotal.Value > 0 &&
            File.Exists(plainTempPath))
        {
            var existingLength = new FileInfo(plainTempPath).Length;
            if (existingLength >= item.BytesTotal.Value || (item.BytesTotal.Value - existingLength < 1024 && existingLength > 1024 * 1024))
            {
                File.Move(plainTempPath, finalPath, overwrite: true);
                await MarkCompletedAsync(downloadId, finalPath, existingLength, item.BytesTotal, DateTime.UtcNow);
                return;
            }
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
        DownloadsChanged?.Invoke(this, EventArgs.Empty);

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
                await MarkFailedAsync(downloadId, errorMsg);
                return;
            }

            if (IsSegmentedManifestContentType(response.Content.Headers.ContentType?.MediaType))
            {
                TryDeleteFile(plainTempPath);
                TryDeleteFile(finalPath);
                await MarkFailedAsync(
                    downloadId,
                    _localizationService.GetString("Download.Error.UnsupportedStreaming"));
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
            await using (var output = new FileStream(
                             plainTempPath,
                             resumedBytes > 0 ? FileMode.Append : FileMode.Create,
                             FileAccess.Write,
                             FileShare.Read,
                             1024 * 64,
                             true))
            {
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
                            item.BytesDownloaded = downloaded;
                            item.BytesTotal = totalBytes;
                            item.SpeedBytesPerSecond = speed;
                            item.EstimatedSecondsRemaining = eta;
                            item.UpdatedAt = DateTime.UtcNow;
                            await startDb.SaveChangesAsync(localCts.Token);
                            DownloadsChanged?.Invoke(this, EventArgs.Empty);
                            lastPersistTick = now;
                            lastPersistedBytes = downloaded;
                        }

                        lastTick = now;
                        lastBytes = downloaded;
                    }
                }

                await output.FlushAsync(localCts.Token);
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
            await MarkCompletedAsync(downloadId, finalPath, downloaded, totalBytes, startedAt);
        }
        catch (OperationCanceledException ex)
        {
            var pausedRequested = _pauseRequestedIds.TryRemove(downloadId, out _);
            var cancelRequested = _cancelRequestedIds.ContainsKey(downloadId);
            if (pausedRequested)
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

            if (_cancelRequestedIds.TryRemove(downloadId, out _))
            {
                await RemoveDownloadArtifactsAndRecordAsync(downloadId, CancellationToken.None);
                DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return false;
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
            path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
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
            if ((key.Equals("format", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("output", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("container", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("extension", StringComparison.OrdinalIgnoreCase)) &&
                (value.Equals("m3u8", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("m3u", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("mpd", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("hls", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("dash", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsSegmentedManifestContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        return mediaType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("application/mpegurl", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("audio/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("application/dash+xml", StringComparison.OrdinalIgnoreCase);
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

    private async Task RemoveDownloadArtifactsAndRecordAsync(int downloadId, CancellationToken cancellationToken)
    {
        _pauseRequestedIds.TryRemove(downloadId, out _);
        _cancelRequestedIds.TryRemove(downloadId, out _);
        _queuedIds.TryRemove(downloadId, out _);
        _autoResumeAttempts.TryRemove(downloadId, out _);

        string? dirToCheck = null;

        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
        if (item != null)
        {
            await RestoreMappedEntitiesToSourceUrlAsync(db, item);
            
            if (!string.IsNullOrWhiteSpace(item.LocalFilePath))
            {
                TryDeleteFileWithRetry(item.LocalFilePath);
                dirToCheck = Path.GetDirectoryName(item.LocalFilePath);
            }
            if (!string.IsNullOrWhiteSpace(item.TempFilePath))
            {
                TryDeleteFileWithRetry(item.TempFilePath);
                dirToCheck ??= Path.GetDirectoryName(item.TempFilePath);
            }
            
            // Phase 27: Explicitly removing the row from DB on Cancel
            db.DownloadItems.Remove(item);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (_activeTempFiles.TryRemove(downloadId, out var tempPath))
        {
            TryDeleteFileWithRetry(tempPath);
            dirToCheck ??= Path.GetDirectoryName(tempPath);
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
                TryDeleteFileWithRetry(item.TempFilePath);
                item.TempFilePath = null;
                item.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task MarkCompletedAsync(
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
        
        DownloadCompleted?.Invoke(this, item);
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task MarkPausedAsync(int downloadId)
    {
        using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        item.Status = DownloadStatus.Paused;
        item.SpeedBytesPerSecond = 0;
        item.EstimatedSecondsRemaining = null;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task MarkInterruptedAsPausedAsync(int downloadId, string message)
    {
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
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
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

    private static async Task RestoreMappedEntitiesToSourceUrlAsync(AppDbContext db, DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            return;
        }

        if (item.ChannelType == ChannelType.VOD)
        {
            if (item.ChannelId.HasValue)
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == item.ChannelId.Value);
                if (channel != null)
                {
                    channel.StreamUrl = item.SourceUrl;
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
                }
            }

            return;
        }

        if (item.EpisodeId.HasValue)
        {
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.Id == item.EpisodeId.Value);
            if (episode != null)
            {
                episode.StreamUrl = item.SourceUrl;
            }
        }
        else
        {
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.StreamUrl == item.LocalFilePath);
            if (episode != null)
            {
                episode.StreamUrl = item.SourceUrl;
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
        }
    }

    private async Task MarkFailedAsync(int downloadId, string message)
    {
        using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        // Phase 27: User requested cancelled/failed downloads to be completely removed from DB
        db.DownloadItems.Remove(item);
        await db.SaveChangesAsync();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
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

        if (item.ChannelType == ChannelType.Series && !string.IsNullOrWhiteSpace(item.DisplayName))
        {
            var parsed = SeriesInfoParser.Parse(item.DisplayName);
            var seriesName = BuildSafeFileName(parsed.SeriesName);

            // Phase 27: Smart folder matching — scan existing series folders for a fuzzy match
            // so that "4400" from Provider A and "The 4400" from Provider B share the same folder.
            var seriesPath = FindMatchingSeriesFolder(categoryPath, seriesName)
                            ?? Path.Combine(categoryPath, seriesName);

            if (!Directory.Exists(seriesPath))
            {
                Directory.CreateDirectory(seriesPath);
            }

            // Phase 24: Use centralized parser for accurate Season folder grouping
            var sNum = parsed.Season > 0 ? parsed.Season : 1;
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

    private static void TryDeleteFileWithRetry(string? path)
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

            Thread.Sleep(80);
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
