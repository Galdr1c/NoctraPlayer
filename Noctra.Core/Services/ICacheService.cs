using System.Threading.Tasks;

namespace Noctra.Core.Services;

public interface ICacheService
{
    /// <summary>
    /// Calculates the total size of cache files in bytes.
    /// </summary>
    Task<long> GetCacheSizeAsync();

    /// <summary>
    /// Returns a human-readable string representation of the cache size (e.g., "12.5 MB").
    /// </summary>
    Task<string> GetCacheSizeStringAsync();

    /// <summary>
    /// Deletes all files in the cache directories.
    /// </summary>
    Task ClearCacheAsync();
}
