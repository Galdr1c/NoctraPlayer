using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Noctra.Core.Collections;
using Noctra.Core.Services;

namespace Noctra.Mobile.Controls;

public class RemoteImage : Image
{
    private const int DefaultDecodePixelWidth = 384;
    private const int MaxDecodePixelWidth = 2048;
    private const int MaxCacheEntries = 128;
    private const long MaxCacheBytes = 64L * 1024L * 1024L;

    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(Url));

    public static readonly StyledProperty<int> DecodePixelWidthProperty =
        AvaloniaProperty.Register<RemoteImage, int>(nameof(DecodePixelWidth), DefaultDecodePixelWidth);

    public static readonly StyledProperty<bool> IsImageLoadedProperty =
        AvaloniaProperty.Register<RemoteImage, bool>(nameof(IsImageLoaded), false);

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly ByteBudgetLruCache<string, Bitmap> Cache = new(
        MaxCacheBytes,
        MaxCacheEntries,
        StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> InFlightLoads = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> FailedUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DownloadGate = new(6, 6);
    private static readonly SemaphoreSlim DecodeGate = new(2, 2);

    private const int HttpImageMaxAttempts = 2;
    private const int HttpRetryBaseDelayMs = 250;
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(2);

    private CancellationTokenSource? _loadCts;

    static RemoteImage()
    {
        UrlProperty.Changed.AddClassHandler<RemoteImage>((control, _) => control.StartImageLoad());
        DecodePixelWidthProperty.Changed.AddClassHandler<RemoteImage>((control, _) => control.StartImageLoad());
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
        Opacity = 0;
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(180)
            }
        };
    }

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public int DecodePixelWidth
    {
        get => GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    public bool IsImageLoaded
    {
        get => GetValue(IsImageLoadedProperty);
        set => SetValue(IsImageLoadedProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartImageLoad();
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

        var decodePixelWidth = NormalizeDecodePixelWidth(DecodePixelWidth);
        if (TryApplyCachedSource(normalizedUrl, decodePixelWidth))
        {
            return;
        }

        if (IsRecentlyFailed(normalizedUrl))
        {
            SetSourceOnUiThread(null);
            return;
        }

        _loadCts = new CancellationTokenSource();
        _ = LoadAndApplyAsync(normalizedUrl, decodePixelWidth, _loadCts.Token);
    }

    private async Task LoadAndApplyAsync(string url, int decodePixelWidth, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await GetOrStartBitmapLoadAsync(url, decodePixelWidth, cancellationToken).ConfigureAwait(false);
            TrySetSource(url, bitmap, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            TrySetSource(url, null, cancellationToken);
        }
    }

    private static async Task<Bitmap?> GetOrStartBitmapLoadAsync(
        string url,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        var cacheKey = CreateCacheKey(url, decodePixelWidth);
        if (Cache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        if (IsRecentlyFailed(url))
        {
            return null;
        }

        var loadTask = InFlightLoads.GetOrAdd(
            cacheKey,
            _ => DownloadBitmapAsync(url, cacheKey, decodePixelWidth));
        return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Bitmap?> DownloadBitmapAsync(string url, string cacheKey, int decodePixelWidth)
    {
        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadHttpBitmapAsync(url, cacheKey, uri, decodePixelWidth).ConfigureAwait(false);
            }

            if (uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return LoadFileBitmap(uri, decodePixelWidth);
            }

            if (uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                return LoadAssetBitmap(uri, decodePixelWidth);
            }

            if (uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                return TryDecodeDataUri(url, decodePixelWidth);
            }

            return null;
        }
        catch
        {
            MarkFailureCooldown(url);
            return null;
        }
        finally
        {
            DownloadGate.Release();
            InFlightLoads.TryRemove(cacheKey, out _);
        }
    }

    private static async Task<Bitmap?> DownloadHttpBitmapAsync(
        string normalizedUrl,
        string cacheKey,
        Uri uri,
        int decodePixelWidth)
    {
        foreach (var requestUri in BuildRequestUriCandidates(uri))
        {
            var bitmap = await DownloadHttpBitmapWithRetryAsync(
                    normalizedUrl,
                    cacheKey,
                    requestUri,
                    decodePixelWidth)
                .ConfigureAwait(false);
            if (bitmap != null)
            {
                return bitmap;
            }
        }

        return null;
    }

    private static async Task<Bitmap?> DownloadHttpBitmapWithRetryAsync(
        string normalizedUrl,
        string cacheKey,
        Uri uri,
        int decodePixelWidth)
    {
        for (var attempt = 0; attempt < HttpImageMaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await HttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    MarkFailureCooldown(normalizedUrl);
                    if (IsPermanentHttpFailure(response.StatusCode) || attempt == HttpImageMaxAttempts - 1)
                    {
                        return null;
                    }

                    await DelayBeforeRetryAsync(response, attempt).ConfigureAwait(false);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var memory = new MemoryStream();
                using var bodyCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await stream.CopyToAsync(memory, bodyCts.Token).ConfigureAwait(false);

                if (memory.Length == 0)
                {
                    MarkFailureCooldown(normalizedUrl);
                    return null;
                }

                memory.Position = 0;
                var header = new byte[Math.Min(memory.Length, 128)];
                await memory.ReadAsync(header).ConfigureAwait(false);
                memory.Position = 0;

                if (IsHtmlContent(header))
                {
                    MarkFailureCooldown(normalizedUrl);
                    return null;
                }

                var bitmap = await DecodeHttpBitmapAsync(memory, decodePixelWidth).ConfigureAwait(false);
                AddToCache(cacheKey, bitmap);
                return bitmap;
            }
            catch when (attempt < HttpImageMaxAttempts - 1)
            {
                MarkFailureCooldown(normalizedUrl);
                await Task.Delay(HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1)).ConfigureAwait(false);
            }
            catch
            {
                MarkFailureCooldown(normalizedUrl);
                return null;
            }
        }

        return null;
    }

    private static bool IsPermanentHttpFailure(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound
            or HttpStatusCode.Gone;

    private static async Task DelayBeforeRetryAsync(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is { } delay && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return;
        }

        await Task.Delay(HttpRetryBaseDelayMs * (attempt + 1) * (attempt + 1)).ConfigureAwait(false);
    }

    private static Bitmap? LoadFileBitmap(Uri uri, int decodePixelWidth)
    {
        if (!File.Exists(uri.LocalPath))
        {
            return null;
        }

        using var file = File.OpenRead(uri.LocalPath);
        return DecodeBitmap(file, decodePixelWidth);
    }

    private static Bitmap? LoadAssetBitmap(Uri uri, int decodePixelWidth)
    {
        using var asset = AssetLoader.Open(uri);
        return DecodeBitmap(asset, decodePixelWidth);
    }

    private static IReadOnlyList<Uri> BuildRequestUriCandidates(Uri originalUri)
    {
        if (!originalUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return [originalUri];
        }

        try
        {
            var httpsUri = new UriBuilder(originalUri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            return originalUri.Host.Equals("image.tmdb.org", StringComparison.OrdinalIgnoreCase)
                ? [httpsUri]
                : [originalUri, httpsUri];
        }
        catch
        {
            return [originalUri];
        }
    }

    private static Bitmap? TryDecodeDataUri(string url, int decodePixelWidth)
    {
        var commaIndex = url.IndexOf(',');
        if (commaIndex < 0 || commaIndex >= url.Length - 1)
        {
            return null;
        }

        var header = url[..commaIndex];
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(url[(commaIndex + 1)..]);
            using var memory = new MemoryStream(bytes);
            return DecodeBitmap(memory, decodePixelWidth);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHtmlContent(byte[] buffer)
    {
        if (buffer.Length < 4)
        {
            return false;
        }

        try
        {
            var snippet = System.Text.Encoding.UTF8.GetString(buffer).TrimStart();
            return snippet.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<body", StringComparison.OrdinalIgnoreCase)
                || snippet.StartsWith("<div", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AddToCache(string url, Bitmap bitmap)
        => Cache.TryAdd(url, bitmap, EstimateBitmapBytes(bitmap));

    private static long EstimateBitmapBytes(Bitmap bitmap)
        => Math.Max(1L, (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4L);

    private bool TryApplyCachedSource(string normalizedUrl, int decodePixelWidth)
    {
        var cacheKey = CreateCacheKey(normalizedUrl, decodePixelWidth);
        if (!Cache.TryGet(cacheKey, out var cached))
        {
            return false;
        }

        SetSourceOnUiThread(cached, normalizedUrl);
        return true;
    }

    private static Bitmap DecodeBitmap(Stream stream, int decodePixelWidth)
        => Bitmap.DecodeToWidth(
            stream,
            NormalizeDecodePixelWidth(decodePixelWidth),
            BitmapInterpolationMode.MediumQuality);

    private static async Task<Bitmap> DecodeHttpBitmapAsync(Stream stream, int decodePixelWidth)
    {
        await DecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return DecodeBitmap(stream, decodePixelWidth);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static int NormalizeDecodePixelWidth(int decodePixelWidth)
        => Math.Clamp(decodePixelWidth, 64, MaxDecodePixelWidth);

    private static string CreateCacheKey(string normalizedUrl, int decodePixelWidth)
        => $"{NormalizeDecodePixelWidth(decodePixelWidth)}|{normalizedUrl}";

    private void SetSourceOnUiThread(Bitmap? bitmap, string? sourceUrl = null)
    {
        void Apply()
        {
            Source = bitmap;
            IsImageLoaded = bitmap != null;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return;
        }

        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Render);
    }

    private void TrySetSource(string sourceUrl, Bitmap? bitmap, CancellationToken cancellationToken)
    {
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
            return;
        }

        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Render);
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
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Noctra.Mobile/1.0");
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
        if (normalized.Length < 8 ||
            normalized.Equals("logo n/a", StringComparison.OrdinalIgnoreCase) ||
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
                normalized = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.ToString();
            }
        }

        return normalized;
    }

    private static void MarkFailureCooldown(string url)
    {
        FailedUntilUtc[url] = DateTime.UtcNow.Add(FailureCooldown);
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

}
