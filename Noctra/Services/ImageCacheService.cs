using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class ImageCacheService : IImageCacheService
{
    private static readonly HttpClient HttpClient = CreateOptimizedClient();
    private static readonly TimeSpan MemoryTtl = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, MemoryCacheEntry> _memoryCache = new();
    private readonly string _diskCachePath;

    public ImageCacheService()
    {
        _diskCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra",
            "image-cache");

        Directory.CreateDirectory(_diskCachePath);
    }

    public async Task<BitmapImage?> GetImageAsync(string url, int decodePixelWidth = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        ClearExpiredMemoryEntries();

        if (_memoryCache.TryGetValue(url, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Image;
        }

        var diskPath = GetDiskCachePath(url);
        if (File.Exists(diskPath))
        {
            var bytes = await File.ReadAllBytesAsync(diskPath, cancellationToken);
            var diskImage = CreateBitmap(bytes, decodePixelWidth);
            if (diskImage != null)
            {
                SetMemoryCache(url, diskImage);
                return diskImage;
            }
        }

        var downloadedBytes = await DownloadImageBytesAsync(url, cancellationToken);
        if (downloadedBytes == null || downloadedBytes.Length == 0)
        {
            return null;
        }

        var downloadedImage = CreateBitmap(downloadedBytes, decodePixelWidth);
        if (downloadedImage == null)
        {
            return null;
        }

        _ = SaveToDiskAsync(diskPath, downloadedBytes, cancellationToken);
        SetMemoryCache(url, downloadedImage);

        return downloadedImage;
    }

    public async Task PreloadAsync(IEnumerable<string> urls, int decodePixelWidth = 0, CancellationToken cancellationToken = default)
    {
        var uniqueUrls = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        var tasks = uniqueUrls.Select(url => GetImageAsync(url, decodePixelWidth, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public void ClearExpiredMemoryEntries()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _memoryCache)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                _memoryCache.TryRemove(entry.Key, out _);
            }
        }
    }

    private void SetMemoryCache(string url, BitmapImage image)
    {
        _memoryCache[url] = new MemoryCacheEntry(image, DateTime.UtcNow.Add(MemoryTtl));
    }

    private static async Task<byte[]?> DownloadImageBytesAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await NetworkRetry.ExecuteAsync(
                () => HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
                cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateOptimizedClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static async Task SaveToDiskAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }
        catch
        {
            // Disk cache write failures are non-fatal.
        }
    }

    private string GetDiskCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_diskCachePath, $"{hash}.img");
    }

    private static BitmapImage? CreateBitmap(byte[] data, int decodePixelWidth)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }

            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private sealed record MemoryCacheEntry(BitmapImage Image, DateTime ExpiresAt);
}

