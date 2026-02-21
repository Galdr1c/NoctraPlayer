using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class ContentDownloadService : IContentDownloadService
{
    private const int ProgressPersistIntervalMs = 1800;
    private const long ProgressPersistMinDeltaBytes = 1024 * 1024; // 1 MB
    private const int MaxAutoResumeAttempts = 3;
    private static readonly Regex SeriesEpisodeRegex = new(
        @"(s(?:eason)?\s*(?<s>\d{1,2})\s*e(?:pisode)?\s*(?<e>\d{1,3}))|((?<s2>\d{1,2})\s*x\s*(?<e2>\d{1,3}))|(sezon\s*(?<s3>\d{1,2})\s*b[oö]l[uü]m\s*(?<e3>\d{1,3}))|(b[oö]l[uü]m\s*(?<e4>\d{1,3}))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ISettingsService _settingsService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ContentDownloadService>? _logger;
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

    public ContentDownloadService(
        ISettingsService settingsService,
        IDbContextFactory<AppDbContext> contextFactory,
        HttpClient httpClient,
        ILogger<ContentDownloadService>? logger = null)
    {
        _settingsService = settingsService;
        _contextFactory = contextFactory;
        _httpClient = httpClient;
        _logger = logger;

        // Ensure worker starts on app launch to process pending/interrupted downloads
        EnsureQueueWorkerStarted();
    }

    public async Task<DownloadContentResult> QueueDownloadAsync(
        DownloadContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProfileId <= 0)
        {
            return new DownloadContentResult(false, false, "Profil bulunamadi.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            return new DownloadContentResult(false, false, "Indirme URL'i gecersiz.");
        }

        var normalizedSource = request.SourceUrl.Trim().Trim('"', '\'');
        if (IsLocalFilePath(normalizedSource))
        {
            return new DownloadContentResult(true, true, "Icerik zaten yerel indirildi.");
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
                return new DownloadContentResult(true, true, "Icerik daha once indirildi.", duplicate.Id);
            }

            return new DownloadContentResult(true, true, "Indirme zaten kuyrukta.", duplicate.Id);
        }

        var item = new DownloadItem
        {
            ProfileId = request.ProfileId,
            PlaylistId = request.PlaylistId,
            ChannelId = request.ChannelId > 0 ? request.ChannelId : null,
            EpisodeId = request.EpisodeId > 0 ? request.EpisodeId : null,
            ChannelType = request.ItemType == DownloadItemType.SeriesEpisode ? ChannelType.Series : ChannelType.VOD,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Icerik" : request.DisplayName.Trim(),
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
        return new DownloadContentResult(true, false, "Indirme kuyruga eklendi.", item.Id);
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
        int profileId,
        CancellationToken cancellationToken = default)
    {
        if (profileId <= 0)
        {
            return [];
        }

        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (!_lastCleanupUtcByProfile.TryGetValue(profileId, out var lastCleanupUtc) ||
            (DateTime.UtcNow - lastCleanupUtc).TotalSeconds >= 20)
        {
            await CleanupMissingCompletedDownloadsAsync(db, profileId, cancellationToken);
            _lastCleanupUtcByProfile[profileId] = DateTime.UtcNow;
        }

        return await db.DownloadItems
            .AsNoTracking()
            .Where(d => d.ProfileId == profileId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
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
        var items = await db.DownloadItems
            .Where(d => d.ProfileId == profileId)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            TryDeleteFile(item.LocalFilePath);
            TryDeleteFile(item.TempFilePath);
        }

        db.DownloadItems.RemoveRange(items);
        await db.SaveChangesAsync(cancellationToken);
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

        if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Failed)
        {
            return;
        }

        // Legacy format guard: If file path points to old encrypted format, fail explicitly.
        if (item.LocalFilePath?.EndsWith(".nctra", StringComparison.OrdinalIgnoreCase) == true ||
            item.TempFilePath?.EndsWith(".nctra.part", StringComparison.OrdinalIgnoreCase) == true ||
            item.TempFilePath?.EndsWith(".nctra", StringComparison.OrdinalIgnoreCase) == true)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = "Eski şifreli format. Lütfen tekrar indirin.";
            item.LocalFilePath = null;
            item.TempFilePath = null;
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
        if (item == null || item.Status == DownloadStatus.Completed || item.Status == DownloadStatus.Canceled)
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
        var profileDownloadDirectory = EnsureProfileDownloadDirectory(settings.DownloadPath, item.ProfileId);
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
        var candidates = BuildDownloadCandidates(item.SourceUrl, settings.DownloadQuality);
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
                await MarkInterruptedAsPausedAsync(downloadId, "Sunucudan yanit alinamadi, devam etmek icin 'Devam Et' kullanin.");
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
                await MarkFailedAsync(downloadId, "Indirme tamamlanamadi (bos dosya).");
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
                        $"Sunucu yaniti erken sonlandi ({FormatBytes(downloaded)}/{FormatBytes(totalBytes.Value)}).");
                    return;
                }
            }

            File.Move(plainTempPath, finalPath, overwrite: true);
            _autoResumeAttempts.TryRemove(downloadId, out _);
            await MarkCompletedAsync(downloadId, finalPath, downloaded, totalBytes, startedAt);
        }
        catch (OperationCanceledException)
        {
            var pausedRequested = _pauseRequestedIds.TryRemove(downloadId, out _);
            var cancelRequested = _cancelRequestedIds.ContainsKey(downloadId);
            if (pausedRequested)
            {
                await MarkPausedAsync(downloadId);
            }
            else if (!cancelRequested)
            {
                // Network timeout/interruption can also throw OperationCanceledException.
                // Do not delete artifacts unless user explicitly canceled.
                await MarkInterruptedAsPausedAsync(downloadId, "Baglanti kesildi, devam etmek icin 'Devam Et' kullanin.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Download failed for item {DownloadId}", downloadId);
            if (IsTransientResponseEndedException(ex))
            {
                await TryAutoResumeAfterTransientInterruptionAsync(downloadId, ex.Message);
            }
            else
            {
                _autoResumeAttempts.TryRemove(downloadId, out _);
                await MarkInterruptedAsPausedAsync(downloadId, UserFriendlyErrorMessage.WithPrefix("Indirme durduruldu", ex));
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

                response.Dispose();
            }
            catch
            {
                // Try next candidate.
            }
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
                $"Indirme durduruldu: baglanti birden fazla kez kesildi. Lutfen 'Devam Et' ile tekrar deneyin.");
            return;
        }

        var safeDetail = UserFriendlyErrorMessage.FromText(detail);
        await MarkInterruptedAsPausedAsync(
            downloadId,
            $"Baglanti kesildi, otomatik devam deneniyor ({attempt}/{MaxAutoResumeAttempts}). {safeDetail}");

        var delayMs = Math.Min(4500, 1200 * attempt);
        await Task.Delay(delayMs);
        await ResumeDownloadAsync(downloadId, CancellationToken.None);
    }

    private static bool IsTransientResponseEndedException(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
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

        using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
        if (item != null)
        {
            TryDeleteFileWithRetry(item.LocalFilePath);
            TryDeleteFileWithRetry(item.TempFilePath);
            db.DownloadItems.Remove(item);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (_activeTempFiles.TryGetValue(downloadId, out var tempPath))
        {
            TryDeleteFileWithRetry(tempPath);
        }
    }

    private async Task CleanupMissingCompletedDownloadsAsync(
        AppDbContext db,
        int profileId,
        CancellationToken cancellationToken)
    {
        var staleItems = await db.DownloadItems
            .Where(d => d.ProfileId == profileId &&
                        d.Status == DownloadStatus.Completed &&
                        !string.IsNullOrWhiteSpace(d.LocalFilePath))
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
        if (item.ChannelType == ChannelType.VOD)
        {
            if (item.ChannelId.HasValue)
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == item.ChannelId.Value);
                if (channel != null)
                {
                    channel.StreamUrl = localPath;
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
                }
            }

            return;
        }

        if (item.EpisodeId.HasValue)
        {
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.Id == item.EpisodeId.Value);
            if (episode != null)
            {
                episode.StreamUrl = localPath;
            }
        }
        else
        {
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.StreamUrl == item.SourceUrl);
            if (episode != null)
            {
                episode.StreamUrl = localPath;
            }
        }

        var seriesChannels = await db.Channels
            .Where(c => c.Type == ChannelType.Series && c.StreamUrl == item.SourceUrl && c.PlaylistId == item.PlaylistId)
            .ToListAsync();
        foreach (var channel in seriesChannels)
        {
            channel.StreamUrl = localPath;
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

        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = message;
        item.SpeedBytesPerSecond = 0;
        item.EstimatedSecondsRemaining = null;
        item.UpdatedAt = DateTime.UtcNow;
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

    private static IReadOnlyList<string> BuildDownloadCandidates(string sourceUrl, DownloadQuality quality)
    {
        var candidates = new List<string>();
        if (quality == DownloadQuality.Standard)
        {
            var standardCandidate = TryBuildStandardQualityCandidate(sourceUrl);
            if (!string.IsNullOrWhiteSpace(standardCandidate) &&
                !string.Equals(standardCandidate, sourceUrl, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(standardCandidate);
            }
        }

        candidates.Add(sourceUrl);
        return candidates;
    }

    private static string? TryBuildStandardQualityCandidate(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var currentOutput = query["output"];
        if (currentOutput == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(currentOutput) ||
            string.Equals(currentOutput, "m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        query.Set("output", "m3u8");
        var builder = new UriBuilder(uri) { Query = query.ToString() };
        return builder.Uri.ToString();
    }

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

    private static string BuildSafeFileName(string rawName)
    {
        var value = string.IsNullOrWhiteSpace(rawName) ? "icerik" : rawName.Trim();
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

    private static string EnsureProfileDownloadDirectory(string? baseDownloadPath, int profileId)
    {
        var basePath = string.IsNullOrWhiteSpace(baseDownloadPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noctra", "Downloads")
            : baseDownloadPath;

        var profilePath = Path.Combine(basePath, $"Profile_{profileId}");
        if (!Directory.Exists(profilePath))
        {
            Directory.CreateDirectory(profilePath);
        }
        return profilePath;
    }

    private static string EnsureItemDownloadDirectory(string profilePath, DownloadItem item)
    {
        var category = item.ChannelType == ChannelType.Series ? "Series" : "Movies";
        var categoryPath = Path.Combine(profilePath, category);
        if (!Directory.Exists(categoryPath))
        {
            Directory.CreateDirectory(categoryPath);
        }

        if (item.ChannelType == ChannelType.Series && !string.IsNullOrWhiteSpace(item.DisplayName))
        {
            var seriesName = BuildSafeFileName(ExtractSeriesName(item.DisplayName));
            var seriesPath = Path.Combine(categoryPath, seriesName);
            if (!Directory.Exists(seriesPath))
            {
                Directory.CreateDirectory(seriesPath);
            }
            return seriesPath;
        }

        return categoryPath;
    }

    private static string BuildItemFileStem(DownloadItem item)
    {
        return BuildSafeFileName(item.DisplayName ?? "download");
    }

    private static string ExtractSeriesName(string displayName)
    {
        var match = SeriesEpisodeRegex.Match(displayName);
        if (match.Success)
        {
            return displayName[..match.Index].Trim();
        }
        return displayName;
    }



    private bool TryCheckWifiPolicy(out string? message)
    {
        message = null;
        if (!_settingsService.Settings.DownloadWifiOnly)
        {
            return true;
        }

        var isUnmetered = NetworkInterface.GetAllNetworkInterfaces()
            .Any(i => i.OperationalStatus == OperationalStatus.Up &&
                      (i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                       i.NetworkInterfaceType == NetworkInterfaceType.Ethernet));

        if (!isUnmetered)
        {
            message = "Sadece Wi-Fi veya Ethernet uzerinden indirme yapilabilir (ayarlardan degistirilebilir).";
            return false;
        }

        return true;
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

}
