using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class ContentDownloadService : IContentDownloadService
{
    private const string EncryptedExtension = ".nctra";
    private const string DownloadTempExtension = ".nctra.part";
    private const string PlaybackCacheExtension = ".playcache";
    private const int ProgressPersistIntervalMs = 1800;
    private const long ProgressPersistMinDeltaBytes = 1024 * 1024; // 1 MB
    private static readonly Regex SeriesEpisodeRegex = new(
        @"(s(?:eason)?\s*(?<s>\d{1,2})\s*e(?:pisode)?\s*(?<e>\d{1,3}))|((?<s2>\d{1,2})\s*x\s*(?<e2>\d{1,3}))|(sezon\s*(?<s3>\d{1,2})\s*b[oö]l[uü]m\s*(?<e3>\d{1,3}))|(b[oö]l[uü]m\s*(?<e4>\d{1,3}))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ISettingsService _settingsService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ContentDownloadService>? _logger;
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly ConcurrentQueue<int> _pendingIds = new();
    private readonly ConcurrentDictionary<int, byte> _queuedIds = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeDownloadCts = new();
    private readonly ConcurrentDictionary<int, string> _activeTempFiles = new();
    private readonly ConcurrentDictionary<int, byte> _pauseRequestedIds = new();
    private readonly ConcurrentDictionary<int, byte> _cancelRequestedIds = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastCleanupUtcByProfile = new();
    private readonly byte[] _encryptionKey;
    private readonly byte[] _hmacKey;
    private int _isQueueWorkerStarted;

    public event EventHandler? DownloadsChanged;

    public ContentDownloadService(
        ISettingsService settingsService,
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        ILogger<ContentDownloadService>? logger = null)
    {
        _settingsService = settingsService;
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _logger = logger;
        (_encryptionKey, _hmacKey) = CreateCryptoKeys();
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
        if (IsEncryptedLocalPath(normalizedSource))
        {
            return new DownloadContentResult(true, true, "Icerik zaten yerel indirildi.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
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

    public async Task<string> ResolvePlayableUrlAsync(
        string streamUrl,
        CancellationToken cancellationToken = default)
    {
        await CleanupPlaybackCacheAsync(cancellationToken);

        if (!IsEncryptedLocalPath(streamUrl))
        {
            return streamUrl;
        }

        if (!File.Exists(streamUrl))
        {
            var fallback = await TryRestoreMissingLocalPathAsync(streamUrl, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return streamUrl;
        }

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra",
            "TempPlayback");
        Directory.CreateDirectory(cacheRoot);

        var cacheName = $"{ComputeSha1(streamUrl)}{PlaybackCacheExtension}";
        var cachePath = Path.Combine(cacheRoot, cacheName);
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
        {
            return cachePath;
        }

        try
        {
            await DecryptFileAsync(streamUrl, cachePath, cancellationToken);
        }
        catch (InvalidDataException)
        {
            // Corrupted/mismatched encrypted file: attempt one-time repair from persisted temp payload.
            var repaired = await TryRepairEncryptedDownloadAsync(streamUrl, cancellationToken);
            if (!repaired)
            {
                throw;
            }

            await DecryptFileAsync(streamUrl, cachePath, cancellationToken);
        }
        try
        {
            File.SetAttributes(cachePath, FileAttributes.Hidden | FileAttributes.Temporary);
        }
        catch
        {
            // no-op
        }
        return cachePath;
    }

    private async Task<bool> TryRepairEncryptedDownloadAsync(
        string encryptedPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedEncryptedPath = NormalizePath(encryptedPath);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidates = await db.DownloadItems
                .Where(d => d.LocalEncryptedPath != null)
                .ToListAsync(cancellationToken);

            var item = candidates.FirstOrDefault(d =>
                string.Equals(NormalizePath(d.LocalEncryptedPath), normalizedEncryptedPath, StringComparison.OrdinalIgnoreCase));
            if (item == null && candidates.Count > 0)
            {
                item = candidates.FirstOrDefault(d =>
                    string.Equals(Path.GetFileName(d.LocalEncryptedPath), Path.GetFileName(normalizedEncryptedPath), StringComparison.OrdinalIgnoreCase));
            }

            var tempPath = item?.TempFilePath;
            if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
            {
                var siblingTemp = normalizedEncryptedPath + ".part";
                if (File.Exists(siblingTemp))
                {
                    tempPath = siblingTemp;
                }
            }

            if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
            {
                return false;
            }

            var tempLength = new FileInfo(tempPath).Length;
            if (tempLength <= 0)
            {
                return false;
            }

            var extension = ResolveExtensionFromSource(item?.SourceUrl ?? string.Empty);
            if (File.Exists(normalizedEncryptedPath))
            {
                TryDeleteFileWithRetry(normalizedEncryptedPath);
            }

            await EncryptFileWithRetryAsync(tempPath, normalizedEncryptedPath, extension, cancellationToken);
            TryDeleteFileWithRetry(tempPath);

            if (item != null)
            {
                item.LocalEncryptedPath = normalizedEncryptedPath;
                item.Status = DownloadStatus.Completed;
                item.BytesDownloaded = tempLength;
                item.BytesTotal = tempLength;
                item.SpeedBytesPerSecond = 0;
                item.EstimatedSecondsRemaining = 0;
                item.TempFilePath = null;
                item.ErrorMessage = null;
                item.CompletedAt ??= DateTime.Now;
                item.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync(cancellationToken);
            }

            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Encrypted download repair failed for path {EncryptedPath}", encryptedPath);
            return false;
        }
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
        string missingEncryptedPath,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems
            .FirstOrDefaultAsync(d => d.LocalEncryptedPath == missingEncryptedPath, cancellationToken);
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
        try
        {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Noctra",
                "TempPlayback");
            if (!Directory.Exists(cacheRoot))
            {
                return Task.CompletedTask;
            }

            var threshold = DateTime.Now.AddMinutes(-2);
            foreach (var file in Directory.EnumerateFiles(cacheRoot, "*" + PlaybackCacheExtension))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime <= threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // no-op
                }
            }
        }
        catch
        {
            // no-op
        }

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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var items = await db.DownloadItems
            .Where(d => d.ProfileId == profileId)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            TryDeleteFile(item.LocalEncryptedPath);
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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
        if (item == null)
        {
            return;
        }

        if (item.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
        {
            item.Status = DownloadStatus.Paused;
            item.UpdatedAt = DateTime.Now;
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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
        if (item == null)
        {
            return;
        }

        if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Failed)
        {
            return;
        }

        item.Status = DownloadStatus.Queued;
        item.ErrorMessage = null;
        item.UpdatedAt = DateTime.Now;
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
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
                    // App beklenmedik kapandıysa indirme yarım kalır; açılışta kullanıcıdan devam ettirme beklenir.
                    item.Status = DownloadStatus.Paused;
                    item.SpeedBytesPerSecond = 0;
                    item.EstimatedSecondsRemaining = null;
                    item.UpdatedAt = DateTime.Now;
                    hasChanges = true;
                    continue;
                }

                if (item.Status == DownloadStatus.Failed)
                {
                    var hasPartial = !string.IsNullOrWhiteSpace(item.TempFilePath) && File.Exists(item.TempFilePath);
                    if (hasPartial)
                    {
                        item.Status = DownloadStatus.Paused;
                        item.SpeedBytesPerSecond = 0;
                        item.EstimatedSecondsRemaining = null;
                        item.UpdatedAt = DateTime.Now;
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
        await using var startScope = _scopeFactory.CreateAsyncScope();
        var startDb = startScope.ServiceProvider.GetRequiredService<AppDbContext>();
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
            item.UpdatedAt = DateTime.Now;
            await startDb.SaveChangesAsync();
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var settings = _settingsService.Settings;
        var profileDownloadDirectory = EnsureProfileDownloadDirectory(settings.DownloadPath, item.ProfileId);
        var downloadDirectory = EnsureItemDownloadDirectory(profileDownloadDirectory, item);
        var extension = ResolveExtensionFromSource(item.SourceUrl);
        var safeName = BuildItemFileStem(item);
        var encryptedPath = string.IsNullOrWhiteSpace(item.LocalEncryptedPath)
            ? CreateUniquePath(downloadDirectory, safeName, EncryptedExtension)
            : item.LocalEncryptedPath!;
        var plainTempPath = string.IsNullOrWhiteSpace(item.TempFilePath)
            ? CreateUniquePath(downloadDirectory, safeName, DownloadTempExtension)
            : item.TempFilePath!;
        _activeTempFiles[downloadId] = plainTempPath;
        var candidates = BuildDownloadCandidates(item.SourceUrl, settings.DownloadQuality);
        var resumedBytes = File.Exists(plainTempPath) ? new FileInfo(plainTempPath).Length : 0L;
        if (resumedBytes < 0)
        {
            resumedBytes = 0;
        }

        if (File.Exists(encryptedPath) && new FileInfo(encryptedPath).Length > 0)
        {
            await MarkCompletedAsync(downloadId, encryptedPath, resumedBytes, item.BytesTotal ?? resumedBytes, DateTime.UtcNow);
            return;
        }

        if (item.BytesTotal.HasValue &&
            item.BytesTotal.Value > 0 &&
            resumedBytes >= item.BytesTotal.Value &&
            File.Exists(plainTempPath))
        {
            await EncryptFileWithRetryAsync(plainTempPath, encryptedPath, extension, localCts.Token);
            TryDeleteFileWithRetry(plainTempPath);
            await MarkCompletedAsync(downloadId, encryptedPath, resumedBytes, item.BytesTotal, DateTime.UtcNow);
            return;
        }

        item.Status = DownloadStatus.Downloading;
        item.ErrorMessage = null;
        item.UpdatedAt = DateTime.Now;
        item.BytesDownloaded = resumedBytes;
        item.SpeedBytesPerSecond = 0;
        item.EstimatedSecondsRemaining = null;
        item.LocalEncryptedPath = encryptedPath;
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
                            item.UpdatedAt = DateTime.Now;
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
                TryDeleteFile(encryptedPath);
                return;
            }

            await EncryptFileWithRetryAsync(plainTempPath, encryptedPath, extension, localCts.Token);
            TryDeleteFileWithRetry(plainTempPath);
            await MarkCompletedAsync(downloadId, encryptedPath, downloaded, totalBytes, startedAt);
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
            await MarkInterruptedAsPausedAsync(downloadId, $"Indirme durduruldu: {ex.Message}");
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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId, cancellationToken);
        if (item != null)
        {
            TryDeleteFileWithRetry(item.LocalEncryptedPath);
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
                        !string.IsNullOrWhiteSpace(d.LocalEncryptedPath))
            .ToListAsync(cancellationToken);

        if (staleItems.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var item in staleItems)
        {
            if (!File.Exists(item.LocalEncryptedPath!))
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
                item.UpdatedAt = DateTime.Now;
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
        string encryptedPath,
        long downloaded,
        long? total,
        DateTime startedAtUtc)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        item.Status = DownloadStatus.Completed;
        item.LocalEncryptedPath = encryptedPath;
        item.TempFilePath = null;
        item.BytesDownloaded = downloaded;
        item.BytesTotal = total ?? downloaded;
        var elapsed = Math.Max(0.5, (DateTime.UtcNow - startedAtUtc).TotalSeconds);
        item.SpeedBytesPerSecond = downloaded / elapsed;
        item.EstimatedSecondsRemaining = 0;
        item.CompletedAt = DateTime.Now;
        item.UpdatedAt = DateTime.Now;
        item.ErrorMessage = null;
        await db.SaveChangesAsync();

        await UpdateMappedEntitiesToLocalPathAsync(db, item, encryptedPath);
        await db.SaveChangesAsync();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task MarkPausedAsync(int downloadId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        item.Status = DownloadStatus.Paused;
        item.SpeedBytesPerSecond = 0;
        item.EstimatedSecondsRemaining = null;
        item.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task MarkInterruptedAsPausedAsync(int downloadId, string message)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        item.Status = DownloadStatus.Paused;
        item.ErrorMessage = message;
        item.SpeedBytesPerSecond = 0;
        item.EstimatedSecondsRemaining = null;
        item.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task UpdateMappedEntitiesToLocalPathAsync(AppDbContext db, DownloadItem item, string encryptedPath)
    {
        if (item.ChannelType == ChannelType.VOD)
        {
            if (item.ChannelId.HasValue)
            {
                var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == item.ChannelId.Value);
                if (channel != null)
                {
                    channel.StreamUrl = encryptedPath;
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
                    channel.StreamUrl = encryptedPath;
                }
            }

            return;
        }

        if (item.EpisodeId.HasValue)
        {
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.Id == item.EpisodeId.Value);
            if (episode != null)
            {
                episode.StreamUrl = encryptedPath;
            }
        }
        else
        {
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.StreamUrl == item.SourceUrl);
            if (episode != null)
            {
                episode.StreamUrl = encryptedPath;
            }
        }

        var seriesChannels = await db.Channels
            .Where(c => c.Type == ChannelType.Series && c.StreamUrl == item.SourceUrl && c.PlaylistId == item.PlaylistId)
            .ToListAsync();
        foreach (var channel in seriesChannels)
        {
            channel.StreamUrl = encryptedPath;
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
                    c.StreamUrl == item.LocalEncryptedPath);
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
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.StreamUrl == item.LocalEncryptedPath);
            if (episode != null)
            {
                episode.StreamUrl = item.SourceUrl;
            }
        }

        var linkedSeriesChannels = await db.Channels
            .Where(c => c.Type == ChannelType.Series &&
                        c.PlaylistId == item.PlaylistId &&
                        c.StreamUrl == item.LocalEncryptedPath)
            .ToListAsync();
        foreach (var channel in linkedSeriesChannels)
        {
            channel.StreamUrl = item.SourceUrl;
        }
    }

    private async Task MarkFailedAsync(int downloadId, string message)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.DownloadItems.FirstOrDefaultAsync(d => d.Id == downloadId);
        if (item == null)
        {
            return;
        }

        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = message;
        item.SpeedBytesPerSecond = 0;
        item.EstimatedSecondsRemaining = null;
        item.UpdatedAt = DateTime.Now;
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

    private static (byte[] EncryptionKey, byte[] HmacKey) CreateCryptoKeys()
    {
        var baseValue = $"{Environment.MachineName}|{Environment.UserName}|NoctraDownloadKeyV2";
        using var sha = SHA512.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(baseValue));
        var enc = hash.Take(32).ToArray();
        var mac = hash.Skip(32).Take(32).ToArray();
        return (enc, mac);
    }

    private async Task EncryptFileAsync(string sourcePath, string encryptedPath, string originalExtension)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, true);
        await using var output = new FileStream(encryptedPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 64, true);

        var header = $"NOCTRA|2|{originalExtension}|{input.Length}";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        await output.WriteAsync(BitConverter.GetBytes(headerBytes.Length));
        await output.WriteAsync(headerBytes);

        var iv = RandomNumberGenerator.GetBytes(16);
        await output.WriteAsync(iv);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        await using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
        {
            await input.CopyToAsync(crypto);
            await crypto.FlushAsync();
            crypto.FlushFinalBlock();
        }

        output.Position = 0;
        using var hmac = new HMACSHA256(_hmacKey);
        var hash = hmac.ComputeHash(output);
        output.Position = output.Length;
        await output.WriteAsync(hash);
    }

    private async Task EncryptFileWithRetryAsync(
        string sourcePath,
        string encryptedPath,
        string originalExtension,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (attempt > 1 && File.Exists(encryptedPath))
                {
                    TryDeleteFile(encryptedPath);
                }

                await EncryptFileAsync(sourcePath, encryptedPath, originalExtension);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            await Task.Delay(250 * attempt, cancellationToken);
        }

        throw lastError ?? new IOException("Sifreleme adimi basarisiz.");
    }

    private async Task DecryptFileAsync(string encryptedPath, string plainPath, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, true);
        if (input.Length < 64)
        {
            throw new InvalidDataException("Bozuk indirme dosyasi.");
        }

        var totalLength = input.Length;
        input.Position = totalLength - 32;
        var expectedHmac = new byte[32];
        await input.ReadExactlyAsync(expectedHmac, cancellationToken);

        input.Position = 0;
        using (var hmac = new HMACSHA256(_hmacKey))
        {
            var dataLength = totalLength - 32;
            using var limited = new LimitedLengthReadStream(input, dataLength);
            var computed = hmac.ComputeHash(limited);
            if (!CryptographicOperations.FixedTimeEquals(expectedHmac, computed))
            {
                throw new InvalidDataException("Dosya dogrulamasi basarisiz.");
            }
        }

        input.Position = 0;
        var lenBuffer = new byte[4];
        await input.ReadExactlyAsync(lenBuffer, cancellationToken);
        var headerLength = BitConverter.ToInt32(lenBuffer, 0);
        if (headerLength <= 0 || headerLength > 1024)
        {
            throw new InvalidDataException("Gecersiz dosya basligi.");
        }

        var headerBytes = new byte[headerLength];
        await input.ReadExactlyAsync(headerBytes, cancellationToken);
        var header = Encoding.UTF8.GetString(headerBytes);
        if (!header.StartsWith("NOCTRA|2|", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Desteklenmeyen dosya formati.");
        }

        var iv = new byte[16];
        await input.ReadExactlyAsync(iv, cancellationToken);

        var encryptedDataLength = totalLength - 32 - 4 - headerLength - 16;
        if (encryptedDataLength <= 0)
        {
            throw new InvalidDataException("Sifreli veri yok.");
        }

        await using var output = new FileStream(plainPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 64, true);
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        await using var crypto = new CryptoStream(
            new LimitedLengthReadStream(input, encryptedDataLength),
            aes.CreateDecryptor(),
            CryptoStreamMode.Read);
        await crypto.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string? TryGetStoredOriginalExtension(string encryptedPath)
    {
        try
        {
            using var input = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lenBuffer = new byte[4];
            if (input.Read(lenBuffer, 0, 4) != 4)
            {
                return null;
            }

            var headerLength = BitConverter.ToInt32(lenBuffer, 0);
            if (headerLength <= 0 || headerLength > 1024)
            {
                return null;
            }

            var headerBytes = new byte[headerLength];
            if (input.Read(headerBytes, 0, headerBytes.Length) != headerBytes.Length)
            {
                return null;
            }

            var header = Encoding.UTF8.GetString(headerBytes);
            var parts = header.Split('|');
            if (parts.Length < 4)
            {
                return null;
            }

            var ext = parts[2];
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 12)
            {
                return null;
            }

            return ext.StartsWith('.') ? ext : "." + ext;
        }
        catch
        {
            return null;
        }
    }

    private bool TryCheckWifiPolicy(out string message)
    {
        message = string.Empty;
        if (!_settingsService.Settings.DownloadWifiOnly)
        {
            return true;
        }

        try
        {
            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces().ToList();
            var activeInterfaces = allInterfaces.Where(n => n.OperationalStatus == OperationalStatus.Up).ToList();
            if (activeInterfaces.Count == 0)
            {
                message = "Ag baglantisi bulunamadi.";
                return false;
            }

            var hasWirelessAdapter = allInterfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            if (!hasWirelessAdapter)
            {
                return true;
            }

            var hasActiveWifi = activeInterfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            if (hasActiveWifi)
            {
                return true;
            }

            message = "Sadece Wi-Fi ile indirme acik.";
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string EnsureProfileDownloadDirectory(string? configuredPath, int profileId)
    {
        var rootFallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra",
            "Downloads");
        var root = string.IsNullOrWhiteSpace(configuredPath)
            ? rootFallback
            : configuredPath.Trim().Trim('"');

        if (!Path.IsPathFullyQualified(root))
        {
            root = rootFallback;
        }

        var profilePath = Path.Combine(root, $"profile_{profileId}");
        Directory.CreateDirectory(profilePath);
        return profilePath;
    }

    private static string EnsureItemDownloadDirectory(string profileRoot, DownloadItem item)
    {
        string directory;
        if (item.ChannelType == ChannelType.Series)
        {
            ParseSeriesNaming(item.DisplayName, out var seriesTitle, out var seasonNumber, out _, out _);
            directory = Path.Combine(
                profileRoot,
                "Diziler",
                BuildSafeFileName(seriesTitle),
                $"Sezon {Math.Max(1, seasonNumber):00}");
        }
        else if (item.ChannelType == ChannelType.VOD)
        {
            directory = Path.Combine(
                profileRoot,
                "Filmler",
                BuildSafeFileName(item.DisplayName));
        }
        else
        {
            directory = Path.Combine(profileRoot, "Diger");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string BuildItemFileStem(DownloadItem item)
    {
        if (item.ChannelType != ChannelType.Series)
        {
            return BuildSafeFileName(item.DisplayName);
        }

        ParseSeriesNaming(item.DisplayName, out _, out var seasonNumber, out var episodeNumber, out var episodeTitle);
        if (episodeNumber > 0)
        {
            var safeEpisodeTitle = BuildSafeFileName(string.IsNullOrWhiteSpace(episodeTitle)
                ? $"Bolum {episodeNumber:00}"
                : episodeTitle);
            return $"S{Math.Max(1, seasonNumber):00}E{episodeNumber:00} - {safeEpisodeTitle}";
        }

        return BuildSafeFileName(item.DisplayName);
    }

    private static void ParseSeriesNaming(
        string? rawName,
        out string seriesTitle,
        out int seasonNumber,
        out int episodeNumber,
        out string episodeTitle)
    {
        var input = string.IsNullOrWhiteSpace(rawName) ? "Dizi" : rawName.Trim();
        seriesTitle = input;
        seasonNumber = 1;
        episodeNumber = 0;
        episodeTitle = string.Empty;

        var match = SeriesEpisodeRegex.Match(input);
        if (!match.Success)
        {
            return;
        }

        seasonNumber = ParseGroupNumber(match, "s", "s2", "s3");
        if (seasonNumber <= 0)
        {
            seasonNumber = 1;
        }

        episodeNumber = ParseGroupNumber(match, "e", "e2", "e3", "e4");
        var before = input[..match.Index].Trim(' ', '-', '_', '|', ':', '.');
        var after = input[(match.Index + match.Length)..].Trim(' ', '-', '_', '|', ':', '.');

        if (!string.IsNullOrWhiteSpace(before))
        {
            seriesTitle = before;
        }

        if (!string.IsNullOrWhiteSpace(after))
        {
            episodeTitle = after;
        }
    }

    private static int ParseGroupNumber(Match match, params string[] groupNames)
    {
        foreach (var groupName in groupNames)
        {
            if (!match.Groups[groupName].Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups[groupName].Value, out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static bool IsEncryptedLocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.EndsWith(EncryptedExtension, StringComparison.OrdinalIgnoreCase);
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

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("output", out var output))
        {
            return null;
        }

        var currentOutput = output.ToString();
        if (string.IsNullOrWhiteSpace(currentOutput) ||
            string.Equals(currentOutput, "m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var queryMap = query.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        queryMap["output"] = "m3u8";
        return QueryHelpers.AddQueryString(uri.GetLeftPart(UriPartial.Path), queryMap);
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

        return Path.Combine(directory, $"{fileNameWithoutExtension}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
    }

    private static string ComputeSha1(string value)
    {
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private sealed class LimitedLengthReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LimitedLengthReadStream(Stream inner, long length)
        {
            _inner = inner;
            _remaining = Math.Max(0, length);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, _remaining);
            var read = _inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(buffer.Length, _remaining);
            var read = await _inner.ReadAsync(buffer[..toRead], cancellationToken);
            _remaining -= read;
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
