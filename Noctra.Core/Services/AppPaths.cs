namespace Noctra.Core.Services;

public static class AppPaths
{
    public static string UserDataDirectory { get; } = ResolveUserDataDirectory();

    public static string SettingsDirectory => Path.Combine(UserDataDirectory, "Settings");
    public static string DownloadsDirectory => Path.Combine(UserDataDirectory, "Downloads");
    public static string LegacyDownloadsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Noctra",
        "Downloads");
    public static string DatabasePath => Path.Combine(UserDataDirectory, "noctra_v1.db");
    public static string LegacyDatabasePath => Path.Combine(UserDataDirectory, "noctra.db");
    public static string TempPlaybackDirectory => Path.Combine(UserDataDirectory, "TempPlayback");
    public static string LogsDirectory => Path.Combine(UserDataDirectory, "Logs");

    public static void EnsureUserDataDirectory()
    {
        Directory.CreateDirectory(UserDataDirectory);
    }

    public static string NormalizeDownloadDirectory(string? path)
    {
        var fallback = DownloadsDirectory;
        var candidate = string.IsNullOrWhiteSpace(path)
            ? fallback
            : path.Trim().Trim('"');

        if (IsSamePath(candidate, LegacyDownloadsDirectory))
        {
            candidate = fallback;
        }

        try
        {
            var full = Path.GetFullPath(candidate);
            Directory.CreateDirectory(full);
            return full;
        }
        catch
        {
            Directory.CreateDirectory(fallback);
            return Path.GetFullPath(fallback);
        }
    }

    private static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveUserDataDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return Path.Combine(documents, "Noctra");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra");
    }
}
