using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MobileSettingsBackendAuditTests
{
    [Theory]
    [InlineData(false, "Cellular", true)]
    [InlineData(true, "Wi-Fi", true)]
    [InlineData(true, "Ethernet", true)]
    [InlineData(true, "Cellular", false)]
    [InlineData(true, "Offline", false)]
    [InlineData(true, "Unvalidated", false)]
    public void DownloadWifiPolicy_UsesTheActivePlatformNetwork(
        bool wifiOnly,
        string networkStatus,
        bool expected)
    {
        Assert.Equal(
            expected,
            ContentDownloadService.IsDownloadNetworkAllowed(wifiOnly, networkStatus));
    }

    [Fact]
    public async Task DisabledEpg_DoesNotReturnCachedPrograms()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>(MockBehavior.Strict);
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Settings)
            .Returns(new AppSettings { EpgEnabled = false });

        var service = new EpgService(
            factory.Object,
            new HttpClient(),
            settings.Object,
            Mock.Of<ILocalizationService>(),
            new LanguageDetectionService());

        var result = await service.GetCurrentProgramsAsync(
            [new Channel { Id = 42, Type = ChannelType.Live, Name = "Test" }]);

        Assert.Empty(result);
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public void StaleChannelRefresh_IsDueImmediatelyWhenTheProfileReturns()
    {
        var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            TimeSpan.Zero,
            MainViewModel.CalculateInitialRefreshDelay(
                TimeSpan.FromHours(12),
                now.AddHours(-13),
                now));
        Assert.Equal(
            TimeSpan.FromHours(11),
            MainViewModel.CalculateInitialRefreshDelay(
                TimeSpan.FromHours(12),
                now.AddHours(-1),
                now));
    }

    [Fact]
    public void MobileSettings_HidesDesktopOnlyOrUnsupportedControls()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var viewCode = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml.cs");
        var downloads = ReadProjectFile("Noctra.Core", "Services", "ContentDownloadService.cs");

        Assert.DoesNotContain("<TextBox Classes=\"readonly\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"DownloadQuality\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("IsChecked=\"{Binding ClearHistoryOnExit}\"", view, StringComparison.Ordinal);
        Assert.Contains("Mobile.Settings.Download.OriginalQuality", view, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsSelectionKind.DownloadQuality", viewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBuildStandardQualityCandidate", downloads, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidDownloadCompletion_UsesANativeNotification()
    {
        var manifest = ReadProjectFile("Noctra.Android", "Properties", "AndroidManifest.xml");
        var dialogs = ReadProjectFile("Noctra.Android", "Services", "AndroidDialogService.cs");
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        Assert.Contains("android.permission.POST_NOTIFICATIONS", manifest, StringComparison.Ordinal);
        Assert.Contains("NotificationManager", dialogs, StringComparison.Ordinal);
        Assert.Contains("Resource.Drawable.ic_notification_noctra", dialogs, StringComparison.Ordinal);
        Assert.Contains("EnsureNotificationPermissionAsync", dialogs, StringComparison.Ordinal);
        Assert.Contains("TryHandleRequestPermissionsResult", activity, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ShowNotificationAsync(string title, string message) =>\r\n        ShowAlertAsync",
            dialogs,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Download_RemainsLimitedToVodAndSeries()
    {
        var player = ReadProjectFile("Noctra.Core", "ViewModels", "PlayerViewModel.cs");
        var downloads = ReadProjectFile("Noctra.Core", "Services", "ContentDownloadService.cs");

        Assert.Contains("!IsLiveContent", player, StringComparison.Ordinal);
        Assert.Contains("DownloadItemType.SeriesEpisode ? ChannelType.Series : ChannelType.VOD", downloads, StringComparison.Ordinal);
        Assert.DoesNotContain("ChannelType.Live", downloads, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileDownloadPath_IsPassiveAndCannotReceiveTextFocus()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        Assert.DoesNotContain("<TextBox Classes=\"readonly\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DownloadPathDisplay\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DownloadPath}\"", view, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", view, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
