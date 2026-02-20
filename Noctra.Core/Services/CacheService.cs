using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Noctra.Core.Services;

public class CacheService : ICacheService
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Noctra");

    private static readonly string[] CacheDirectories =
    {
        "image-cache",
        "image-cache-avalonia",
        "TempPlayback",
        "logs"
    };

    public Task<long> GetCacheSizeAsync()
    {
        return Task.Run(() =>
        {
            long size = 0;
            foreach (var dirName in CacheDirectories)
            {
                var path = Path.Combine(AppDataPath, dirName);
                if (Directory.Exists(path))
                {
                    size += GetDirectorySize(path);
                }
            }
            return size;
        });
    }

    public async Task<string> GetCacheSizeStringAsync()
    {
        var bytes = await GetCacheSizeAsync();
        return FormatBytes(bytes);
    }

    public Task ClearCacheAsync()
    {
        return Task.Run(() =>
        {
            foreach (var dirName in CacheDirectories)
            {
                var path = Path.Combine(AppDataPath, dirName);
                if (!Directory.Exists(path)) continue;

                var files = Directory.GetFiles(path);
                foreach (var file in files)
                {
                    try
                    {
                        // Don't delete the active startup.log if possible, 
                        // but actually StartupDiagnostics might hold a lock anyway.
                        File.Delete(file);
                    }
                    catch
                    {
                        // Skip files that are in use (like active logs)
                    }
                }

                // Also try to delete subdirectories (especially in TempPlayback)
                var subDirs = Directory.GetDirectories(path);
                foreach (var subDir in subDirs)
                {
                    try
                    {
                        Directory.Delete(subDir, true);
                    }
                    catch
                    {
                        // Skip if in use
                    }
                }
            }
        });
    }

    private long GetDirectorySize(string path)
    {
        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            return files.Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:N1} {units[unitIndex]}";
    }
}
