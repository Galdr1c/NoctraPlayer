using System;
using System.IO;
using Android.Content;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Android storage reporting that stats the actual directory. DriveInfo is not
/// reliable on Android (the "/" root does not represent the emulated storage
/// mount where downloads live), so use Java.IO.File on the directory itself.
/// </summary>
public sealed class AndroidStorageInfoService : IStorageInfoService
{
    public AndroidStorageInfoService(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public StorageInfo GetStorageInfo(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return StorageInfo.Empty;
        }

        try
        {
            var info = new Java.IO.File(directoryPath);
            if (!info.Exists())
            {
                return StorageInfo.Empty;
            }

            return new StorageInfo(info.TotalSpace, info.UsableSpace);
        }
        catch
        {
            return StorageInfo.Empty;
        }
    }
}
