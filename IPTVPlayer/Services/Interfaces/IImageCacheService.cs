using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace IPTVPlayer.Services.Interfaces;

public interface IImageCacheService
{
    Task<BitmapImage?> GetImageAsync(string url, int decodePixelWidth = 0, CancellationToken cancellationToken = default);
    Task PreloadAsync(IEnumerable<string> urls, int decodePixelWidth = 0, CancellationToken cancellationToken = default);
    void ClearExpiredMemoryEntries();
}
