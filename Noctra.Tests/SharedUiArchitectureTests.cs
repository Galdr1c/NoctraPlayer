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
    public void MobileCompactControls_ConsumeSharedTransport()
    {
        var compactControls = LoadProjectFile(
            "Noctra.Mobile", "Views", "MobilePlayerCompactControls.axaml");

        Assert.Contains("using:Noctra.UI.Views.Player", compactControls, StringComparison.Ordinal);
        Assert.Contains("<player:PlayerTransportBar", compactControls, StringComparison.Ordinal);
        Assert.DoesNotContain("<player:MobilePlayerTransportBar", compactControls, StringComparison.Ordinal);
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
