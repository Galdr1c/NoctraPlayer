namespace Noctra.Core.Services;

public sealed class DesktopAppPathService : IAppPathService
{
    public DesktopAppPathService(
        string? userDataDirectory = null,
        string? localApplicationDataDirectory = null)
    {
        UserDataDirectory = userDataDirectory ?? ResolveUserDataDirectory();
        var localDataDirectory = localApplicationDataDirectory ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        SettingsDirectory = Path.Combine(UserDataDirectory, "Settings");
        DownloadsDirectory = Path.Combine(UserDataDirectory, "Downloads");
        LegacyDownloadsDirectory = Path.Combine(localDataDirectory, "Noctra", "Downloads");
        DatabasePath = Path.Combine(UserDataDirectory, "noctra_v1.db");
        LegacyDatabasePath = Path.Combine(UserDataDirectory, "noctra.db");
        TempPlaybackDirectory = Path.Combine(UserDataDirectory, "TempPlayback");
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
    }

    public string NormalizeDownloadDirectory(string? path)
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
