using Noctra.Core.Services;

namespace Noctra.Tests;

public sealed class AppPathServiceTests
{
    [Fact]
    public void DesktopPaths_AreDerivedFromConfiguredRoots()
    {
        var userData = Path.Combine(Path.GetTempPath(), "NoctraTests", "UserData");
        var localData = Path.Combine(Path.GetTempPath(), "NoctraTests", "LocalData");
        var paths = new DesktopAppPathService(userData, localData);

        Assert.Equal(userData, paths.UserDataDirectory);
        Assert.Equal(Path.Combine(userData, "Settings"), paths.SettingsDirectory);
        Assert.Equal(Path.Combine(userData, "Downloads"), paths.DownloadsDirectory);
        Assert.Equal(Path.Combine(localData, "Noctra", "Downloads"), paths.LegacyDownloadsDirectory);
        Assert.Equal(Path.Combine(userData, "noctra_v1.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(userData, "noctra.db"), paths.LegacyDatabasePath);
        Assert.Equal(Path.Combine(userData, "TempPlayback"), paths.TempPlaybackDirectory);
        Assert.Equal(Path.Combine(userData, "Logs"), paths.LogsDirectory);
    }

    [Fact]
    public void NormalizeDownloadDirectory_MapsLegacyPathToCurrentDownloads()
    {
        var root = CreateTestRoot();
        try
        {
            var paths = new DesktopAppPathService(
                Path.Combine(root, "UserData"),
                Path.Combine(root, "LocalData"));

            var normalized = paths.NormalizeDownloadDirectory(paths.LegacyDownloadsDirectory);

            Assert.Equal(Path.GetFullPath(paths.DownloadsDirectory), normalized);
            Assert.True(Directory.Exists(normalized));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CacheService_UsesInjectedApplicationPaths()
    {
        var root = CreateTestRoot();
        try
        {
            var paths = new DesktopAppPathService(
                Path.Combine(root, "UserData"),
                Path.Combine(root, "LocalData"));
            Directory.CreateDirectory(paths.LogsDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(paths.LogsDirectory, "test.log"),
                new byte[128]);
            await File.WriteAllBytesAsync(paths.DatabasePath, new byte[256]);

            var service = new CacheService(paths);

            Assert.Equal(384, await service.GetCacheSizeAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "NoctraTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
