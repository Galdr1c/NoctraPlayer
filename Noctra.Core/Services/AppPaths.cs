namespace Noctra.Core.Services;

public static class AppPaths
{
    private static readonly IAppPathService Default = new DesktopAppPathService();

    public static string UserDataDirectory => Default.UserDataDirectory;
    public static string SettingsDirectory => Default.SettingsDirectory;
    public static string DownloadsDirectory => Default.DownloadsDirectory;
    public static string LegacyDownloadsDirectory => Default.LegacyDownloadsDirectory;
    public static string DatabasePath => Default.DatabasePath;
    public static string LegacyDatabasePath => Default.LegacyDatabasePath;
    public static string TempPlaybackDirectory => Default.TempPlaybackDirectory;
    public static string LogsDirectory => Default.LogsDirectory;

    public static void EnsureUserDataDirectory() => Default.EnsureUserDataDirectory();
    public static string NormalizeDownloadDirectory(string? path) =>
        Default.NormalizeDownloadDirectory(path);
}
