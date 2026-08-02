using System.IO;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Desktop storage reporting based on the drive root of the directory.
/// </summary>
public sealed class DesktopStorageInfoService : IStorageInfoService
{
    public StorageInfo GetStorageInfo(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return StorageInfo.Empty;
        }

        var driveRoot = Path.GetPathRoot(directoryPath);
        if (string.IsNullOrEmpty(driveRoot))
        {
            return StorageInfo.Empty;
        }

        var drive = new DriveInfo(driveRoot);
        if (!drive.IsReady)
        {
            return StorageInfo.Empty;
        }

        return new StorageInfo(drive.TotalSize, drive.AvailableFreeSpace);
    }
}
