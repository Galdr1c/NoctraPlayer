using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Services.Interfaces;
using Noctra.Core.Services;
using Noctra.ViewModels;
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
    private static readonly ConcurrentDictionary<string, byte> FailedUrlLog = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> CacheLruList = new();
    private static readonly object CacheLock = new();
    private static readonly SemaphoreSlim HttpDownloadGate = new(8, 8);
    private const int MaxCacheEntries = 1500;
    private const int PreloadConcurrency = 10;
    private const int HttpImageMaxAttempts = 4;
    private const int HttpRetryBaseDelayMs = 250;

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

    public static async Task PreloadAsync(IEnumerable<string?> urls, int maxCount = 120, CancellationToken cancellationToken = default)
    {
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

            normalizedUrls.Add(normalized);
            if (normalizedUrls.Count >= maxCount)
            {
                break;
            }
        }

        if (normalizedUrls.Count == 0)
        {
            return;
        }

        using var throttle = new SemaphoreSlim(PreloadConcurrency, PreloadConcurrency);
        var tasks = new List<Task>(normalizedUrls.Count);
        foreach (var normalized in normalizedUrls)
        {
            tasks.Add(Task.Run(async () =>
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var task = InFlightLoads.GetOrAdd(normalized, static url => DownloadBitmapAsync(url));
                    await task.ConfigureAwait(false);
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

        if (Source == null && !string.IsNullOrWhiteSpace(Url))
        {
            StartImageLoad();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelPendingLoad();
        base.OnDetachedFromVisualTree(e);
    }

    private void StartImageLoad()
    {
        CancelPendingLoad();

        var normalizedUrl = NormalizeUrl(Url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            SetSourceOnUiThread(null);
            return;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(normalizedUrl, out var cached))
            {
                // LRU usage update
                CacheLruList.Remove(normalizedUrl);
                CacheLruList.AddLast(normalizedUrl);

                SetSourceOnUiThread(cached);
                return;
            }
        }

        // DO NOT clear the existing source immediately here if we already have an image.
        // This ensures a smooth transition from a provider poster to a TMDB poster without flashing a placeholder.
        // If we don't have an image, it will remain as placeholder until loaded.

        _loadCts = new CancellationTokenSource();
        var loadTask = InFlightLoads.GetOrAdd(normalizedUrl, static url => DownloadBitmapAsync(url));
        _ = AwaitImageAsync(normalizedUrl, loadTask, _loadCts.Token);
    }
    private async Task AwaitImageAsync(string url, Task<Bitmap?> loadTask, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            TrySetSource(url, bitmap, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore stale requests when the control is recycled.
        }
        catch
        {
            TrySetSource(url, null, cancellationToken);
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
            try
            {
                await HttpDownloadGate.WaitAsync().ConfigureAwait(false);
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
                await stream.CopyToAsync(memory).ConfigureAwait(false);
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
                HttpDownloadGate.Release();
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
        lock (CacheLock)
        {
            if (Cache.ContainsKey(url))
            {
                CacheLruList.Remove(url);
                CacheLruList.AddLast(url);
                return;
            }

            // Evict if limit reached
            while (Cache.Count >= MaxCacheEntries && CacheLruList.First != null)
            {
                var oldest = CacheLruList.First.Value;
                CacheLruList.RemoveFirst();
                Cache.TryRemove(oldest, out _);
            }

            if (Cache.TryAdd(url, bitmap))
            {
                CacheLruList.AddLast(url);
            }
        }
    }

    private void SetSourceOnUiThread(Bitmap? bitmap)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Source = bitmap;
            IsImageLoaded = bitmap != null;
            return;
        }

        Dispatcher.UIThread.Post(() => {
            Source = bitmap;
            IsImageLoaded = bitmap != null;
        }, DispatcherPriority.Background);
    }

    private void TrySetSource(string sourceUrl, Bitmap? bitmap, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var currentUrl = NormalizeUrl(Url);
            if (!string.Equals(currentUrl, sourceUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Source = bitmap;
            IsImageLoaded = bitmap != null;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
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
            MaxConnectionsPerServer = 32
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(14)
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
        if (FailedUrlLog.Count > 300 || !FailedUrlLog.TryAdd(url, 0))
        {
            return;
        }

        StartupDiagnostics.Log($"[RemoteImage] Failed: {reason} | {url}");
    }
}
