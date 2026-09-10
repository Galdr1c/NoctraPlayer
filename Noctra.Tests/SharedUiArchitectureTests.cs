namespace Noctra.Tests;

public sealed class SharedUiArchitectureTests
{
    [Fact]
    public void DesktopShell_RemainsLeftRailOnly()
    {
        var mainWindow = LoadProjectFile("Noctra.Avalonia", "MainWindow.axaml");
        var mainWindowCode = LoadProjectFile("Noctra.Avalonia", "MainWindow.axaml.cs");

        Assert.Contains("x:Name=\"SideBar\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Left\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("BottomNavigation", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("BottomNavigation", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPlayer_HostsSharedMobileFirstPresentationWithoutLockAction()
    {
        var adapter = LoadProjectFile(
            "Noctra.Avalonia", "Views", "VideoOverlayView.SharedPresentation.cs");

        Assert.Contains("new PlayerChromeView", adapter, StringComparison.Ordinal);
        Assert.Contains("new PlayerSheetOverlay", adapter, StringComparison.Ordinal);
        Assert.Contains("ShowLockAction = false", adapter, StringComparison.Ordinal);
        Assert.Contains("ShowPiPAction = true", adapter, StringComparison.Ordinal);
        Assert.Contains("overlayContent.Children[0].IsVisible = false", adapter, StringComparison.Ordinal);
        Assert.Contains("overlayContent.Children[1].IsVisible = false", adapter, StringComparison.Ordinal);
        Assert.Contains("overlayContent.Children[2].IsVisible = false", adapter, StringComparison.Ordinal);
        Assert.Contains("SuppressLegacyPanel(\"EpisodesPanel\")", adapter, StringComparison.Ordinal);
        Assert.Contains("EpisodeThumbnailTemplate", adapter, StringComparison.Ordinal);
        Assert.Contains("DesktopRemoteImage", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCompactControls_ConsumeSharedTransport()
    {
        var compactControls = LoadProjectFile(
            "Noctra.Mobile", "Views", "MobilePlayerCompactControls.axaml");

        Assert.Contains("using:Noctra.UI.Views.Player", compactControls, StringComparison.Ordinal);
        Assert.Contains("<player:PlayerTransportBar", compactControls, StringComparison.Ordinal);
        Assert.DoesNotContain("<player:MobilePlayerTransportBar", compactControls, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSheets_ConsumeSharedCommonPanelContent()
    {
        var sheets = LoadProjectFile(
            "Noctra.Mobile", "Views", "MobilePlayerSheets.axaml");

        Assert.Contains("using:Noctra.UI.Views.Player", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerMoreSheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerTrackSheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerQualitySheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerSubtitleAppearanceSheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerInfoSheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerEpisodesSheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<sharedPlayer:PlayerSleepSheet", sheets, StringComparison.Ordinal);
        Assert.Contains("<controls:RemoteImage", sheets, StringComparison.Ordinal);
        Assert.DoesNotContain("MobilePlayerEpisodesSheet", sheets, StringComparison.Ordinal);
        Assert.DoesNotContain("<player:MobilePlayerSubtitleAppearanceSheet", sheets, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPlayerTransport_OwnsTimelineAndCorePlaybackActions()
    {
        var transport = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerTransportBar.axaml");

        Assert.Contains("<player:PlayerTimeline", transport, StringComparison.Ordinal);
        Assert.Contains("PlayPauseCommand", transport, StringComparison.Ordinal);
        Assert.Contains("SkipBackwardCommand", transport, StringComparison.Ordinal);
        Assert.Contains("SkipForwardCommand", transport, StringComparison.Ordinal);
        Assert.Contains("ToggleMuteCommand", transport, StringComparison.Ordinal);
        Assert.Contains("OpenActionsPanelCommand", transport, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPlayerTimeline_PreservesMobileFirstInteractionContract()
    {
        var timeline = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerTimeline.axaml");
        var timelineCode = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerTimeline.axaml.cs");

        Assert.Contains("Height=\"40\"", timeline, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BufferBar\"", timeline, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FillBar\"", timeline, StringComparison.Ordinal);
        Assert.Contains("UpdateSeekPreview", timelineCode, StringComparison.Ordinal);
        Assert.Contains("StartSeekingCommand", timelineCode, StringComparison.Ordinal);
        Assert.Contains("SeekCommand", timelineCode, StringComparison.Ordinal);
        Assert.Contains("Key.Left or Key.Right", timelineCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedTopOverlay_ExposesPlatformCapabilitiesInsteadOfForkingLayout()
    {
        var overlay = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerTopOverlay.axaml");
        var overlayCode = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerTopOverlay.axaml.cs");

        Assert.Contains("ToggleLockCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("EnterPiPCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowLockAction", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowPiPAction", overlay, StringComparison.Ordinal);
        Assert.Contains("StyledProperty<bool> ShowLockActionProperty", overlayCode, StringComparison.Ordinal);
        Assert.Contains("StyledProperty<bool> ShowPiPActionProperty", overlayCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSheetOverlay_OwnsEveryPlatformIndependentPlayerPanel()
    {
        var sheetXaml = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerSheetOverlay.axaml");
        var sheetCode = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerSheetOverlay.axaml.cs");
        var episodesXaml = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerEpisodesSheet.axaml");
        var episodesCode = LoadProjectFile(
            "Noctra.UI", "Views", "Player", "PlayerEpisodesSheet.axaml.cs");

        Assert.Contains("PlayerMoreSheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("PlayerTrackSheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("PlayerQualitySheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("PlayerSubtitleAppearanceSheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("PlayerInfoSheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("PlayerEpisodesSheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("PlayerSleepSheet", sheetXaml, StringComparison.Ordinal);
        Assert.Contains("IsSubtitleAppearanceSettingsOpen", sheetCode, StringComparison.Ordinal);
        Assert.Contains("IsEpisodesPanelOpen", sheetCode, StringComparison.Ordinal);
        Assert.Contains("EpisodeThumbnailTemplate", sheetCode, StringComparison.Ordinal);
        Assert.Contains("EpisodeThumbnailTemplate", episodesXaml, StringComparison.Ordinal);
        Assert.Contains("IDataTemplate", episodesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Noctra.Mobile.Controls", episodesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Noctra.Avalonia.Controls", episodesXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchChrome_IsSharedWhileResultsVirtualizationStaysPlatformSpecific()
    {
        var shared = LoadProjectFile("Noctra.UI", "Views", "AdaptiveSearchView.axaml");
        var mobile = LoadProjectFile("Noctra.Mobile", "Views", "MobileSearchView.axaml");
        var desktop = LoadProjectFile("Noctra.Avalonia", "Views", "SearchView.axaml");

        Assert.Contains("SearchQuery", shared, StringComparison.Ordinal);
        Assert.Contains("ShowSearchIdleState", shared, StringComparison.Ordinal);
        Assert.Contains("ShowSearchEmptyState", shared, StringComparison.Ordinal);
        Assert.Contains("<shared:AdaptiveSearchView", mobile, StringComparison.Ordinal);
        Assert.Contains("<shared:AdaptiveSearchView", desktop, StringComparison.Ordinal);
        Assert.Contains("MobileSectionedCardFeed", mobile, StringComparison.Ordinal);
        Assert.Contains("DesktopSectionedCardFeed", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryScreens_ShareMobileFirstScaffoldAndKeepPlatformFeeds()
    {
        var shared = LoadProjectFile("Noctra.UI", "Views", "AdaptiveLibraryView.axaml");
        Assert.Contains("HeaderActionHost", shared, StringComparison.Ordinal);
        Assert.Contains("ItemsHost", shared, StringComparison.Ordinal);
        Assert.Contains("EmptyIconKind", shared, StringComparison.Ordinal);

        foreach (var view in new[] { "Favorites", "MyList", "History" })
        {
            var mobile = LoadProjectFile("Noctra.Mobile", "Views", $"Mobile{view}View.axaml");
            var desktop = LoadProjectFile("Noctra.Avalonia", "Views", $"{view}View.axaml");

            Assert.Contains("<shared:AdaptiveLibraryView", mobile, StringComparison.Ordinal);
            Assert.Contains("<shared:AdaptiveLibraryView", desktop, StringComparison.Ordinal);
            Assert.Contains("MobileSectionedCardFeed", mobile, StringComparison.Ordinal);
            Assert.Contains("DesktopSectionedCardFeed", desktop, StringComparison.Ordinal);
        }

        var mobileHistory = LoadProjectFile("Noctra.Mobile", "Views", "MobileHistoryView.axaml");
        var desktopHistory = LoadProjectFile("Noctra.Avalonia", "Views", "HistoryView.axaml");
        Assert.Contains("ClearHistoryCommand", mobileHistory, StringComparison.Ordinal);
        Assert.Contains("ClearHistoryCommand", desktopHistory, StringComparison.Ordinal);
    }

    private static string LoadProjectFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
