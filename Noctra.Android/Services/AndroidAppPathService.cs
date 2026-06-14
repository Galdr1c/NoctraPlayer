using System;
using System.IO;
using Android.Content;
using Noctra.Core.Services;
using AndroidEnvironment = Android.OS.Environment;

namespace Noctra.Android.Services;

public sealed class AndroidAppPathService : IAppPathService
{
    public AndroidAppPathService(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        UserDataDirectory = context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android FilesDir is unavailable.");
        var cacheDirectory = context.CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android CacheDir is unavailable.");
        var externalDownloads = context
            .GetExternalFilesDir(AndroidEnvironment.DirectoryDownloads)
            ?.AbsolutePath;

        SettingsDirectory = Path.Combine(UserDataDirectory, "Settings");
        DownloadsDirectory = externalDownloads
            ?? Path.Combine(UserDataDirectory, "Downloads");
        LegacyDownloadsDirectory = DownloadsDirectory;
        DatabasePath = Path.Combine(UserDataDirectory, "noctra_v1.db");
        LegacyDatabasePath = Path.Combine(UserDataDirectory, "noctra.db");
        TempPlaybackDirectory = Path.Combine(cacheDirectory, "TempPlayback");
        LogsDirectory = Path.Combine(UserDataDirectory, "Logs");
    }

    public string UserDataDirectory { get; }
    public string SettingsDirectory { get; }
    public string DownloadsDirectory { get; }
    public string LegacyDownloadsDirectory { get; }
    public string DatabasePath { get; }
    public string LegacyDatabasePath { get; }
    public string TempPlaybackDirectory { get; }
    public string LogsDirectory { get; }

    public void EnsureUserDataDirectory()
    {
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(DownloadsDirectory);
        Directory.CreateDirectory(TempPlaybackDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public string NormalizeDownloadDirectory(string? path)
    {
        Directory.CreateDirectory(DownloadsDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return DownloadsDirectory;
        }

        try
        {
            var root = Path.GetFullPath(DownloadsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(path.Trim().Trim('"'));
            var relative = Path.GetRelativePath(root, candidate);

            if (relative == ".." ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return DownloadsDirectory;
            }

            Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch
        {
            return DownloadsDirectory;
        }
    }
}
