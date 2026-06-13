namespace Noctra.Core.Services;

public interface IAppPathService
{
    string UserDataDirectory { get; }
    string SettingsDirectory { get; }
    string DownloadsDirectory { get; }
    string LegacyDownloadsDirectory { get; }
    string DatabasePath { get; }
    string LegacyDatabasePath { get; }
    string TempPlaybackDirectory { get; }
    string LogsDirectory { get; }

    void EnsureUserDataDirectory();
    string NormalizeDownloadDirectory(string? path);
}
