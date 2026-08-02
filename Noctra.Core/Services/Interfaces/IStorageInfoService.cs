namespace Noctra.Services.Interfaces;

/// <summary>
/// Reports capacity and free space of the filesystem containing a directory.
/// Desktop implementations use DriveInfo, while Android must stat the actual
/// directory (emulated storage mounts), so storage cards show real device data.
/// </summary>
public interface IStorageInfoService
{
    /// <summary>
    /// Gets the total and available bytes for the filesystem containing <paramref name="directoryPath"/>.
    /// </summary>
    StorageInfo GetStorageInfo(string? directoryPath);
}

/// <summary>
/// Capacity snapshot for a filesystem.
/// </summary>
public readonly record struct StorageInfo(long TotalBytes, long AvailableBytes)
{
    public static StorageInfo Empty => new(0, 0);
}
