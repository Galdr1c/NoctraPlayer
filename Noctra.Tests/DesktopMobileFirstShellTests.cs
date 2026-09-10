namespace Noctra.Tests;

public sealed class DesktopMobileFirstShellTests
{
    [Fact]
    public void DesktopShell_CollapsesLegacyHeaderWithoutRemovingLifecycleAnchor()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");
        var mobileShell = Source("Noctra.Mobile", "Views", "MainView.axaml");

        Assert.Contains("CollapseLegacyDesktopHeader", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderSearchBox.IsEnabled = false", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderSearchBox.IsVisible = false", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderSearchBox.Focusable = false", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderBar.Child = null", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderBar.Height = 0", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderBar.Focusable = false", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderBar.IsEnabled = false", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderBar.IsHitTestVisible = false", adapter, StringComparison.Ordinal);
        Assert.Contains("rootGrid.RowDefinitions[0].Height = new GridLength(0)", adapter, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeaderBar\"", mobileShell, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"False\"", mobileShell, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopRail_FollowsCanonicalMobileNavigationOrdering()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");
        var mobileShell = Source("Noctra.Mobile", "Views", "MainView.axaml");

        Assert.Contains("rail.Children.IndexOf(NavSeriesBtn)", adapter, StringComparison.Ordinal);
        Assert.Contains("Favorites before My List", adapter, StringComparison.Ordinal);
        Assert.Contains("rail.Children.IndexOf(NavDownloadsBtn)", adapter, StringComparison.Ordinal);
        Assert.Contains("rail.Children.Insert(settingsIndex, _desktopSettingsNavButton)", adapter, StringComparison.Ordinal);

        var searchIndex = mobileShell.IndexOf("Tag=\"Search\"", StringComparison.Ordinal);
        var favoritesIndex = mobileShell.IndexOf("Tag=\"Favorites\"", StringComparison.Ordinal);
        var myListIndex = mobileShell.IndexOf("Tag=\"MyList\"", StringComparison.Ordinal);
        var settingsIndex = mobileShell.IndexOf("Tag=\"Settings\"", StringComparison.Ordinal);
        Assert.True(searchIndex >= 0 && favoritesIndex > searchIndex && myListIndex > favoritesIndex && settingsIndex > myListIndex);
    }

    [Fact]
    public void DesktopRail_RemainsUsableAtCompactWindowHeights()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");

        Assert.Contains("EnableAdaptiveRailScrolling();", adapter, StringComparison.Ordinal);
        Assert.Contains("SideBar.Child = new ScrollViewer", adapter, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto", adapter, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopSearchRail_NavigatesToSearchPageInsteadOfSearchingInShell()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");
        var searchView = Source("Noctra.Avalonia", "Views", "SearchView.axaml");
        var searchCode = Source("Noctra.Avalonia", "Views", "SearchView.axaml.cs");

        var handlerStart = adapter.IndexOf("private void DesktopSearchNav_Click", StringComparison.Ordinal);
        var handlerEnd = adapter.IndexOf("private void DesktopSettingsNav_Click", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = adapter[handlerStart..handlerEnd];

        Assert.Contains("NavigateSearch_Click(sender, e)", handler, StringComparison.Ordinal);
        Assert.Contains("FocusSearchInput", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitSearchCommand", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderSearchBox", handler, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SharedSearchContent\"", searchView, StringComparison.Ordinal);
        Assert.Contains("SharedSearchContent.FocusSearchInput()", searchCode, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopSettingsRail_OpensCompleteDedicatedSettingsSurface()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");
        var settingsWindow = Source("Noctra.Avalonia", "Views", "SettingsWindow.axaml");
        var sharedAdapter = Source("Noctra.Avalonia", "Views", "SettingsWindow.SharedUi.cs");

        var handlerStart = adapter.IndexOf("private void DesktopSettingsNav_Click", StringComparison.Ordinal);
        var handlerEnd = adapter.IndexOf("private void MobileFirstShellViewModel_PropertyChanged", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = adapter[handlerStart..handlerEnd];

        Assert.Contains("SettingsButton_Click(sender, e)", handler, StringComparison.Ordinal);
        Assert.Contains("CloseSidebar()", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDesktopSettingsPageAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("AdaptiveSettingsOverviewView", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("AdvancedSettingsRequested", adapter, StringComparison.Ordinal);

        Assert.Contains("Settings.Nav.Profile", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Settings.Nav.Playback", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Settings.Nav.Channels", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Settings.Nav.Notifications", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Settings.Nav.Privacy", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Settings.Section.Appearance", settingsWindow, StringComparison.Ordinal);

        Assert.Contains("new SettingsCommonSectionsView()", sharedAdapter, StringComparison.Ordinal);
        Assert.Contains("Take(3)", sharedAdapter, StringComparison.Ordinal);
        Assert.Contains("BackToProfilesRequested", sharedAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSettings_ConsumesSharedExactOverlapWithoutRemovingPlatformSections()
    {
        var adapter = Source("Noctra.Mobile", "Views", "MobileSettingsView.SharedUi.cs");
        var mobileSettings = Source("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        Assert.Contains("new SharedSettingsCommonSectionsView()", adapter, StringComparison.Ordinal);
        Assert.Contains("new SharedSettingsThemePickerView()", adapter, StringComparison.Ordinal);
        Assert.Contains("Take(3)", adapter, StringComparison.Ordinal);
        Assert.Contains("Appearance keeps", adapter, StringComparison.Ordinal);
        Assert.Contains("Playback keeps", adapter, StringComparison.Ordinal);
        Assert.Contains("Settings.Language.Title", mobileSettings, StringComparison.Ordinal);
        Assert.Contains("Settings.Playback.UserAgent", mobileSettings, StringComparison.Ordinal);
        Assert.Contains("Settings.Playback.Quality", mobileSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopSettings_DoesNotExposePartialInShellMoreSettingsBridge()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");

        Assert.DoesNotContain("PlatformContent", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAdvancedSettingsAction", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopAdvancedSettingsRequested", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("More settings", adapter, StringComparison.OrdinalIgnoreCase);
    }

    private static string Source(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. path]));
    }
}
