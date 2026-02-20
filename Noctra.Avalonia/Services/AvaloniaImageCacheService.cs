using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Noctra.Services;

namespace Noctra.Avalonia.Services;

public class AvaloniaImageCacheService
{
    private static readonly HttpClient HttpClient = CreateOptimizedClient();
    // Use a smaller memory cache than WPF since Avalonia handles bitmaps differently
    private static readonly TimeSpan MemoryTtl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, MemoryCacheEntry> _memoryCache = new();
    private readonly string _diskCachePath;

    public AvaloniaImageCacheService()
    {
        _diskCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra",
            "image-cache-avalonia");

        Directory.CreateDirectory(_diskCachePath);
    }

    public async Task<object?> GetImageAsync(string url, int decodePixelWidth = 0, CancellationToken cancellationToken = default)
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
            try 
            {
                using var stream = File.OpenRead(diskPath);
                var diskImage = DecodeBitmap(stream, decodePixelWidth);
                if (diskImage != null)
                {
                    SetMemoryCache(url, diskImage);
                    return diskImage;
                }
            }
            catch
            {
                // corrupted file
                try { File.Delete(diskPath); } catch { }
            }
        }

        var downloadedBytes = await DownloadImageBytesAsync(url, cancellationToken);
        if (downloadedBytes == null || downloadedBytes.Length == 0)
        {
            return null;
        }

        using (var ms = new MemoryStream(downloadedBytes))
        {
            var downloadedImage = DecodeBitmap(ms, decodePixelWidth);
            if (downloadedImage == null)
            {
                return null;
            }

            _ = SaveToDiskAsync(diskPath, downloadedBytes, cancellationToken);
            SetMemoryCache(url, downloadedImage);
            return downloadedImage;
        }
    }

    private Bitmap? DecodeBitmap(Stream stream, int width)
    {
        try
        {
            if (width > 0)
            {
                return Bitmap.DecodeToWidth(stream, width);
            }
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
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

    private void SetMemoryCache(string url, Bitmap image)
    {
        var newEntry = new MemoryCacheEntry(image, DateTime.UtcNow.Add(MemoryTtl));
        _memoryCache.AddOrUpdate(
            url,
            _ => newEntry,
            (url, existing) =>
            {
                return newEntry;
            });
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

    private sealed record MemoryCacheEntry(Bitmap Image, DateTime ExpiresAt);
}
