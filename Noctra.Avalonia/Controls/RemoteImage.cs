using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Services.Interfaces;
using Noctra.Core.Services;
using Noctra.ViewModels;
using Noctra.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Animation;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Noctra.Avalonia.Controls;

public class RemoteImage : Image
{
    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(Url));

    public static readonly StyledProperty<bool> IsImageLoadedProperty =
        AvaloniaProperty.Register<RemoteImage, bool>(nameof(IsImageLoaded), false);

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> InFlightLoads = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> FailedUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> CacheLruList = new();
    private static readonly Dictionary<string, LinkedListNode<string>> NodeMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();
    private static readonly object ActiveControlsLock = new();
    private static readonly List<WeakReference<RemoteImage>> ActiveControls = new();
    private static readonly SemaphoreSlim ImageLoadGate = new(6, 6);
    private static readonly SemaphoreSlim HttpDownloadGate = new(4, 4);
    private const int MaxCacheEntries = 500;
    private const int PreloadConcurrency = 3;
    private const int HttpImageMaxAttempts = 2;
    private const int HttpRetryBaseDelayMs = 250;
    private const int DeferredLoadDelayMs = 20;
    private const int MaxTransientEmptyUrlLogs = 40;
    private static readonly string DiskCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Noctra",
        "ImageCache");
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HostFailureCooldown = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, int> HostFailureCounts = new(StringComparer.OrdinalIgnoreCase);
    private const int HostFailureThreshold = 3;
    private static readonly HashSet<string> KnownBadImageHosts = new(StringComparer.OrdinalIgnoreCase);
    private static int TransientEmptyUrlLogCount;

    private CancellationTokenSource? _loadCts;

    static RemoteImage()
    {
        UrlProperty.Changed.AddClassHandler<RemoteImage>((control, _) => control.StartImageLoad());
        IsImageLoadedProperty.Changed.AddClassHandler<RemoteImage>((control, e) =>
        {
            if (e.NewValue is bool isLoaded)
            {
                control.Opacity = isLoaded ? 1.0 : 0.0;
            }
        });
    }

    public RemoteImage()
    {
        // Initialize for fade-in effect
        Opacity = 0;
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromSeconds(0.25)
            }
        };

        DataContextChanged += (_, _) => RetryCurrentUrlIfNeeded("data-context");
    }

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public bool IsImageLoaded
    {
        get => GetValue(IsImageLoadedProperty);
        set => SetValue(IsImageLoadedProperty, value);
    }

    public static async Task PreloadAsync(IEnumerable<string?> urls, int maxCount = 40, CancellationToken cancellationToken = default)
    {
        using var trace = PerformanceTraceService.Shared?.BeginOperation("IMAGE", "RemoteImage.PreloadAsync", $"max={maxCount}");
        if (urls == null)
        {
            return;
        }

        var normalizedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawUrl in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = NormalizeUrl(rawUrl);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (Cache.ContainsKey(normalized))
            {
                continue;
            }

            if (IsRecentlyFailed(normalized))
            {
                PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage.PreloadAsync skip-failed", $"host={ExtractHost(normalized)}");
                continue;
            }

            if (IsKnownBadImageHost(normalized))
            {
                PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage.PreloadAsync skip-known-bad-host", $"host={ExtractHost(normalized)}");
                MarkFailureCooldown(normalized);
                continue;
            }

            normalizedUrls.Add(normalized);
            if (normalizedUrls.Count >= maxCount)
            {
                break;
            }
        }

        if (normalizedUrls.Count == 0)
        {
            PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage.PreloadAsync skipped", "no uncached urls");
            return;
        }

        PerformanceTraceService.Shared?.Counter("IMAGE", "RemoteImage.PreloadAsync urls", normalizedUrls.Count);
        using var throttle = new SemaphoreSlim(PreloadConcurrency, PreloadConcurrency);
        var tasks = new List<Task>(normalizedUrls.Count);
        foreach (var normalized in normalizedUrls)
        {
            tasks.Add(Task.Run(async () =>
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await GetOrQueueBitmapLoadAsync(normalized, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    throttle.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        TrackActiveControl(this);
        RetryCurrentUrlIfNeeded("attached");
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelPendingLoad();
        UntrackActiveControl(this);
        base.OnDetachedFromVisualTree(e);
    }

    private void StartImageLoad()
    {
        CancelPendingLoad();

        var normalizedUrl = NormalizeUrl(Url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            if (DataContext == null)
            {
                var count = Interlocked.Increment(ref TransientEmptyUrlLogCount);
                if (count <= MaxTransientEmptyUrlLogs)
                {
                    PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage empty-url transient", BuildEmptyUrlDetails());
                }
                else if (count == MaxTransientEmptyUrlLogs + 1)
                {
                    PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage empty-url transient suppressed", $"limit={MaxTransientEmptyUrlLogs}");
                }
            }
            else
            {
                PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage empty-url", BuildEmptyUrlDetails());
            }

            SetSourceOnUiThread(null);
            return;
        }

        if (IsKnownBadImageHost(normalizedUrl))
        {
            PerformanceTraceService.Shared?.Event(
                "IMAGE",
                "RemoteImage known-bad-host skipped",
                $"host={ExtractHost(normalizedUrl)} data={DescribeDataContext(DataContext)}");
            MarkFailureCooldown(normalizedUrl);
            SetSourceOnUiThread(null);
            return;
        }

        if (TryApplyCachedSource(normalizedUrl, "memory-cache"))
        {
            return;
        }

        if (IsRecentlyFailed(normalizedUrl))
        {
            PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage skip-failed", $"host={ExtractHost(normalizedUrl)} data={DescribeDataContext(DataContext)}");
            SetSourceOnUiThread(null);
            return;
        }

        // DO NOT clear the existing source immediately here if we already have an image.
        // This ensures a smooth transition from a provider poster to a TMDB poster without flashing a placeholder.
        // If we don't have an image, it will remain as placeholder until loaded.

        _loadCts = new CancellationTokenSource();
        PerformanceTraceService.Shared?.Event(
            "IMAGE",
            "RemoteImage load-scheduled",
            $"host={ExtractHost(normalizedUrl)} data={DescribeDataContext(DataContext)}");
        _ = AwaitImageAsync(normalizedUrl, _loadCts.Token);
    }

    private void RetryCurrentUrlIfNeeded(string reason)
    {
        if (reason == "data-context" && DataContext == null)
        {
            return;
        }

        var normalizedUrl = NormalizeUrl(Url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return;
        }

        if (Source == null || !IsImageLoaded)
        {
            if (TryApplyCachedSource(normalizedUrl, $"retry-{reason}"))
            {
                return;
            }

            if (IsRecentlyFailed(normalizedUrl))
            {
                StartImageLoad();
                return;
            }

            PerformanceTraceService.Shared?.Event(
                "IMAGE",
                "RemoteImage retry-current-url",
                $"reason={reason} host={ExtractHost(normalizedUrl)} data={DescribeDataContext(DataContext)}");
            StartImageLoad();
        }
    }

    private async Task AwaitImageAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DeferredLoadDelayMs, cancellationToken).ConfigureAwait(false);
            var currentUrl = NormalizeUrl(Url);
            if (!string.Equals(currentUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                PerformanceTraceService.Shared?.Event(
                    "IMAGE",
                    "RemoteImage load-url-changed",
                    $"from={ExtractHost(url)} to={ExtractHost(currentUrl ?? string.Empty)} data={DescribeDataContext(DataContext)}");
                return;
            }

            if (TryApplyCachedSource(url, "await-cache"))
            {
                return;
            }

            PerformanceTraceService.Shared?.Event(
                "IMAGE",
                "RemoteImage load-dispatch",
                $"host={ExtractHost(url)} data={DescribeDataContext(DataContext)}");
            var bitmap = await GetOrQueueBitmapLoadAsync(url, cancellationToken).ConfigureAwait(false);
            if (bitmap == null)
            {
                PerformanceTraceService.Shared?.Event(
                    "IMAGE",
                    "RemoteImage load-null",
                    $"host={ExtractHost(url)} data={DescribeDataContext(DataContext)}");
            }

            TrySetSource(url, bitmap, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            PerformanceTraceService.Shared?.Event(
                "IMAGE",
                "RemoteImage load-cancelled",
                $"host={ExtractHost(url)} data={DescribeDataContext(DataContext)}");
        }
        catch
        {
            TrySetSource(url, null, cancellationToken);
        }
    }

    private static async Task<Bitmap?> GetOrQueueBitmapLoadAsync(string url, CancellationToken cancellationToken)
    {
        if (IsRecentlyFailed(url))
        {
            PerformanceTraceService.Shared?.Event("IMAGE", "Skip recent failed image load", ExtractHost(url));
            return null;
        }

        if (InFlightLoads.TryGetValue(url, out var existing))
        {
            PerformanceTraceService.Shared?.Event("IMAGE", "Await existing image load", ExtractHost(url));
            return await existing.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        using var gateTrace = PerformanceTraceService.Shared?.BeginOperation("IMAGE", "WaitImageLoadGate", ExtractHost(url));
        await ImageLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (InFlightLoads.TryGetValue(url, out existing))
            {
                return await existing.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            PerformanceTraceService.Shared?.Event("IMAGE", "Queue image load", ExtractHost(url));
            var task = InFlightLoads.GetOrAdd(url, static loadUrl => DownloadBitmapAsync(loadUrl));
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ImageLoadGate.Release();
        }
    }

    private static async Task<Bitmap?> DownloadBitmapAsync(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                var cached = TryLoadDiskCache(url);
                if (cached != null)
                {
                    AddToCache(url, cached);
                    return cached;
                }

                return await DownloadHttpBitmapAsync(url, uri).ConfigureAwait(false);
            }

            if (uri.Scheme == Uri.UriSchemeFile)
            {
                if (!File.Exists(uri.LocalPath))
                {
                    return null;
                }

                using var file = File.OpenRead(uri.LocalPath);
                return new Bitmap(file);
            }

            if (uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                using var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            if (uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                return TryDecodeDataUri(url);
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            InFlightLoads.TryRemove(url, out _);
        }
    }

    private static async Task<Bitmap?> DownloadHttpBitmapAsync(string normalizedUrl, Uri uri)
    {
        var requestUris = BuildRequestUriCandidates(uri);
        foreach (var requestUri in requestUris)
        {
            var result = await DownloadHttpBitmapWithRetryAsync(normalizedUrl, requestUri).ConfigureAwait(false);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static async Task<Bitmap?> DownloadHttpBitmapWithRetryAsync(string normalizedUrl, Uri uri)
    {
        for (var attempt = 0; attempt < HttpImageMaxAttempts; attempt++)
        {
            var httpGateAcquired = false;
            try
            {
                using var gateTrace = PerformanceTraceService.Shared?.BeginOperation("IMAGE", "WaitHttpDownloadGate", $"host={uri.Host} attempt={attempt + 1}");
                await HttpDownloadGate.WaitAsync().ConfigureAwait(false);
                httpGateAcquired = true;
                using var trace = PerformanceTraceService.Shared?.BeginOperation("IMAGE", "DownloadHttpBitmap", $"host={uri.Host} attempt={attempt + 1}");
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await HttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    LogFailure(normalizedUrl, $"HTTP {(int)response.StatusCode}");
                    // Permanent client errors should fail fast to avoid pointless retries.
                    if (response.StatusCode is HttpStatusCode.BadRequest or
                        HttpStatusCode.Unauthorized or
                        HttpStatusCode.Forbidden or
                        HttpStatusCode.NotFound or
                        HttpStatusCode.Gone)
                    {
                        return null;
                    }

                    if (attempt < HttpImageMaxAttempts - 1)
                    {
                        // Respect Retry-After when available (common for 429/503).
                        var retryAfter = response.Headers.RetryAfter?.Delta;
                        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                        {
                            await Task.Delay(retryAfter.Value).ConfigureAwait(false);
                        }
                        else
                        {
                            var backoffMs = HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1);
                            await Task.Delay(backoffMs).ConfigureAwait(false);
                        }
                        continue;
                    }

                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var memory = new MemoryStream();
                // Body download için 8 saniyelik toplam timeout - 44 saniyelik takılmaları önler
                using var bodyCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await stream.CopyToAsync(memory, bodyCts.Token).ConfigureAwait(false);
                var length = memory.Length;
                
                if (length == 0)
                {
                    LogFailure(normalizedUrl, "EMPTY_BODY");
                    return null;
                }

                memory.Position = 0;
                
                // --- SNIFF FOR HTML/PLAINTEXT ERRORS ---
                // Bazı paneller 200 OK ile HTML hata sayfası döner. Bitmap(stream) fırlatmadan önce kontrol edelim.
                byte[] buffer = new byte[Math.Min(length, 128)];
                await memory.ReadAsync(buffer).ConfigureAwait(false);
                memory.Position = 0;

                if (IsHtmlContent(buffer))
                {
                    LogFailure(normalizedUrl, "HTML_CONTENT");
                    return null;
                }

                // --- BITMAP DECODE ---
                try
                {
                    var bitmap = new Bitmap(memory);
                    SaveDiskCache(normalizedUrl, memory.ToArray());
                    AddToCache(normalizedUrl, bitmap);
                    return bitmap;
                }
                catch (ArgumentException)
                {
                    LogFailure(normalizedUrl, "INVALID_IMAGE_FORMAT");
                    return null;
                }
            }
            catch (HttpRequestException) when (attempt < HttpImageMaxAttempts - 1)
            {
                var backoffMs = HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1);
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (attempt < HttpImageMaxAttempts - 1)
            {
                LogFailure(normalizedUrl, "Timeout");
                var backoffMs = HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1);
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (attempt < HttpImageMaxAttempts - 1)
            {
                LogFailure(normalizedUrl, "BodyTimeout");
                var backoffMs = HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1);
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < HttpImageMaxAttempts - 1)
            {
                LogFailure(normalizedUrl, ex.GetType().Name);
                var backoffMs = HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1);
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogFailure(normalizedUrl, ex.GetType().Name);
                return null;
            }
            finally
            {
                if (httpGateAcquired)
                {
                    HttpDownloadGate.Release();
                }
            }
        }

        LogFailure(normalizedUrl, "RetryExhausted");
        return null;
    }

    private static bool IsHtmlContent(byte[] buffer)
    {
        if (buffer.Length < 4) return false;
        
        // Convert to string to check for start tags
        try
        {
            var snippet = System.Text.Encoding.UTF8.GetString(buffer).TrimStart();
            return snippet.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<body", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<div", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static IReadOnlyList<Uri> BuildRequestUriCandidates(Uri originalUri)
    {
        if (originalUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var httpsBuilder = new UriBuilder(originalUri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                var httpsUri = httpsBuilder.Uri;

                if (originalUri.Host.Equals("image.tmdb.org", StringComparison.OrdinalIgnoreCase))
                {
                    return [httpsUri];
                }

                return [originalUri, httpsUri];
            }
            catch
            {
                return [originalUri];
            }
        }

        return [originalUri];
    }

    private static Bitmap? TryDecodeDataUri(string url)
    {
        var commaIndex = url.IndexOf(',');
        if (commaIndex < 0 || commaIndex >= url.Length - 1)
        {
            return null;
        }

        var header = url[..commaIndex];
        var payload = url[(commaIndex + 1)..];

        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var memory = new MemoryStream(bytes);
            return new Bitmap(memory);
        }
        catch
        {
            return null;
        }
    }

    private static void AddToCache(string url, Bitmap bitmap)
    {
        var added = false;
        lock (CacheLock)
        {
            if (Cache.ContainsKey(url))
            {
                if (NodeMap.TryGetValue(url, out var lruNode))
                {
                    CacheLruList.Remove(lruNode);
                    NodeMap[url] = CacheLruList.AddLast(url);
                }
                return;
            }

            // Evict if limit reached
            while (Cache.Count >= MaxCacheEntries && CacheLruList.First != null)
            {
                var oldestNode = CacheLruList.First;
                var oldest = oldestNode.Value;
                CacheLruList.RemoveFirst();
                NodeMap.Remove(oldest);
                if (Cache.TryRemove(oldest, out var evicted))
                {
                    // Do not dispose here. Active Image controls may still reference this Bitmap as Source.
                    // Removing it from the cache is enough; GC can collect it once no control references it.
                    PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage cache-evict", $"host={ExtractHost(oldest)}");
                }
            }

            if (Cache.TryAdd(url, bitmap))
            {
                NodeMap[url] = CacheLruList.AddLast(url);
                added = true;
            }
        }

        if (added)
        {
            NotifyActiveControlsSourceAvailable(url, bitmap);
        }
    }

    private bool TryApplyCachedSource(string normalizedUrl, string reason)
    {
        Bitmap? cached;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(normalizedUrl, out cached))
            {
                return false;
            }

            if (NodeMap.TryGetValue(normalizedUrl, out var lruNode))
            {
                CacheLruList.Remove(lruNode);
                NodeMap[normalizedUrl] = CacheLruList.AddLast(normalizedUrl);
            }
        }

        PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage cache-hit", $"host={ExtractHost(normalizedUrl)} reason={reason} data={DescribeDataContext(DataContext)}");
        SetSourceOnUiThread(cached, normalizedUrl, reason);
        return true;
    }

    private void SetSourceOnUiThread(Bitmap? bitmap, string? sourceUrl = null, string? reason = null)
    {
        void TraceApplied()
        {
            if (bitmap != null && !string.IsNullOrWhiteSpace(sourceUrl))
            {
                var details = $"host={ExtractHost(sourceUrl)} cached=true data={DescribeDataContext(DataContext)}";
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    details += $" reason={reason}";
                }

                PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage source-applied", details);
            }

        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Source = bitmap;
            IsImageLoaded = bitmap != null;
            TraceApplied();
            return;
        }

        Dispatcher.UIThread.Post(() => {
            Source = bitmap;
            IsImageLoaded = bitmap != null;
            TraceApplied();
        }, DispatcherPriority.Render);
    }

    private void TrySetSource(string sourceUrl, Bitmap? bitmap, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage source-skip-cancelled", $"host={ExtractHost(sourceUrl)} data={DescribeDataContext(DataContext)}");
            return;
        }

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage source-skip-cancelled", $"host={ExtractHost(sourceUrl)} data={DescribeDataContext(DataContext)}");
                return;
            }

            var currentUrl = NormalizeUrl(Url);
            if (!string.Equals(currentUrl, sourceUrl, StringComparison.OrdinalIgnoreCase))
            {
                PerformanceTraceService.Shared?.Event(
                    "IMAGE",
                    "RemoteImage source-skip-url-mismatch",
                    $"from={ExtractHost(sourceUrl)} to={ExtractHost(currentUrl ?? string.Empty)} data={DescribeDataContext(DataContext)}");
                return;
            }

            Source = bitmap;
            IsImageLoaded = bitmap != null;
            if (bitmap != null)
            {
                PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage source-applied", $"host={ExtractHost(sourceUrl)} cached=false data={DescribeDataContext(DataContext)}");
            }

        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply, DispatcherPriority.Render);
        }
    }

    private void CancelPendingLoad()
    {
        var current = Interlocked.Exchange(ref _loadCts, null);
        if (current == null)
        {
            return;
        }

        current.Cancel();
        current.Dispose();
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Noctra.Avalonia/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        return client;
    }

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalized = url.Trim().Trim('"', '\'');
        if (normalized.Length < 8)
        {
            return null;
        }

        if (normalized.Equals("logo n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        normalized = normalized.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + normalized;
        }

        if (!normalized.Contains("://", StringComparison.Ordinal) &&
            normalized.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + normalized;
        }

        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace(" ", "%20", StringComparison.Ordinal);
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
                uri.Host.Equals("image.tmdb.org", StringComparison.OrdinalIgnoreCase) &&
                uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                normalized = builder.Uri.ToString();
            }
        }

        return normalized;
    }

    private static void LogFailure(string url, string reason)
    {
        // Only log the first failure per URL to avoid log spam.
        // FailedUntilUtc naturally deduplicates because MarkFailureCooldown adds the URL.
        var isNewFailure = !FailedUntilUtc.ContainsKey(url);
        MarkFailureCooldown(url);

        // Track host-level failures for escalation
        var host = ExtractHost(url);
        if (!string.IsNullOrEmpty(host))
        {
            var hostCount = HostFailureCounts.AddOrUpdate(host, 1, (_, c) => c + 1);
            if (hostCount >= HostFailureThreshold)
            {
                // This host keeps failing - apply long cooldown to all its URLs
                FailedUntilUtc[url] = DateTime.UtcNow.Add(HostFailureCooldown);
                PerformanceTraceService.Shared?.Event("IMAGE", "HostEscalated", $"host={host} count={hostCount} cooldown={HostFailureCooldown.TotalMinutes}m");
            }
        }

        if (isNewFailure)
        {
            PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage failure", $"host={host} reason={reason}");
        }
    }

    private static void MarkFailureCooldown(string url)
    {
        FailedUntilUtc[url] = DateTime.UtcNow.Add(FailureCooldown);
    }

    private static Bitmap? TryLoadDiskCache(string url)
    {
        try
        {
            var path = GetDiskCachePath(url);
            if (!File.Exists(path))
            {
                return null;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length <= 0)
            {
                File.Delete(path);
                return null;
            }

            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage disk-cache-hit", $"host={ExtractHost(url)} bytes={fileInfo.Length}");
            return bitmap;
        }
        catch
        {
            TryDeleteDiskCache(url);
            return null;
        }
    }

    private static void SaveDiskCache(string url, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DiskCacheDirectory);
            var path = GetDiskCachePath(url);
            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllBytes(path, bytes);
            PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage disk-cache-save", $"host={ExtractHost(url)} bytes={bytes.Length}");
        }
        catch
        {
            // Disk cache is opportunistic; UI image loading must not fail because persistence failed.
        }
    }

    private static void TryDeleteDiskCache(string url)
    {
        try
        {
            var path = GetDiskCachePath(url);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static string GetDiskCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        return Path.Combine(DiskCacheDirectory, hash + ".img");
    }

    private static bool IsRecentlyFailed(string url)
    {
        if (!FailedUntilUtc.TryGetValue(url, out var untilUtc))
        {
            return false;
        }

        if (untilUtc > DateTime.UtcNow)
        {
            return true;
        }

        FailedUntilUtc.TryRemove(url, out _);
        return false;
    }

    private static bool IsKnownBadImageHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;

        // Statik liste (elle eklenenler)
        if (KnownBadImageHosts.Contains(host))
            return true;

        // Dinamik: 3+ kez başarısız olan host otomatik kötü host sayılır
        if (HostFailureCounts.TryGetValue(host, out var count) && count >= HostFailureThreshold)
            return true;

        return false;
    }

    private static void TrackActiveControl(RemoteImage control)
    {
        lock (ActiveControlsLock)
        {
            for (var i = ActiveControls.Count - 1; i >= 0; i--)
            {
                if (!ActiveControls[i].TryGetTarget(out var existing))
                {
                    ActiveControls.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(existing, control))
                {
                    return;
                }
            }

            ActiveControls.Add(new WeakReference<RemoteImage>(control));
        }
    }

    private static void UntrackActiveControl(RemoteImage control)
    {
        lock (ActiveControlsLock)
        {
            for (var i = ActiveControls.Count - 1; i >= 0; i--)
            {
                if (!ActiveControls[i].TryGetTarget(out var existing) || ReferenceEquals(existing, control))
                {
                    ActiveControls.RemoveAt(i);
                }
            }
        }
    }

    private static void NotifyActiveControlsSourceAvailable(string sourceUrl, Bitmap bitmap)
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (ActiveControlsLock)
            {
                for (var i = ActiveControls.Count - 1; i >= 0; i--)
                {
                    if (!ActiveControls[i].TryGetTarget(out var control))
                    {
                        ActiveControls.RemoveAt(i);
                        continue;
                    }

                    var currentUrl = NormalizeUrl(control.Url);
                    if (!string.Equals(currentUrl, sourceUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ReferenceEquals(control.Source, bitmap) && control.IsImageLoaded)
                    {
                        continue;
                    }

                    control.Source = bitmap;
                    control.IsImageLoaded = true;
                    PerformanceTraceService.Shared?.Event("IMAGE", "RemoteImage source-applied", $"host={ExtractHost(sourceUrl)} cached=true reason=cache-notify data={DescribeDataContext(control.DataContext)}");
                }
            }
        }, DispatcherPriority.Render);
    }

    private string BuildEmptyUrlDetails()
    {
        return $"control={GetType().Name} data={DescribeDataContext(DataContext)}";
    }

    private static string DescribeDataContext(object? dataContext)
    {
        return dataContext switch
        {
            Channel channel => $"Channel id={channel.Id} type={channel.Type} playlist={channel.PlaylistId} name={TrimForLog(channel.Name)} group={TrimForLog(channel.GroupTitle)} logo={HasValue(channel.LogoUrl)} backdrop={HasValue(channel.BackdropUrl)}",
            Series series => $"Series id={series.Id} playlist={series.PlaylistId} name={TrimForLog(series.Name)} group={TrimForLog(series.GroupTitle)} cover={HasValue(series.CoverUrl)} backdrop={HasValue(series.BackdropUrl)}",
            Episode episode => $"Episode id={episode.Id} season={episode.SeasonId} name={TrimForLog(episode.Name)} cover={HasValue(episode.CoverUrl)}",
            null => "<null>",
            _ => dataContext.GetType().Name
        };
    }

    private static string HasValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "empty" : "set";

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var normalized = value.Replace("|", "/", StringComparison.Ordinal).Trim();
        return normalized.Length <= 48 ? normalized : normalized[..48] + "...";
    }

    private static string ExtractHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "<invalid>";
    }
}
