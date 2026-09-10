namespace Noctra.Tests;

public sealed class DesktopMobileFirstShellTests
{
    [Fact]
    public void DesktopShell_CollapsesLegacyHeaderWithoutRemovingLifecycleAnchor()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");
        var mobileShell = Source("Noctra.Mobile", "Views", "MainView.axaml");

        Assert.Contains("CollapseLegacyDesktopHeader", adapter, StringComparison.Ordinal);
        Assert.Contains("HeaderBar.Height = 0", adapter, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"SharedSearchContent\"", searchView, StringComparison.Ordinal);
        Assert.Contains("SharedSearchContent.FocusSearchInput()", searchCode, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopSettingsRail_ReusesExistingSettingsFlow()
    {
        var adapter = Source("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");

        Assert.Contains("MaterialIconKind.CogOutline", adapter, StringComparison.Ordinal);
        Assert.Contains("Settings.Title", adapter, StringComparison.Ordinal);
        Assert.Contains("SettingsButton_Click(sender, e)", adapter, StringComparison.Ordinal);
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
