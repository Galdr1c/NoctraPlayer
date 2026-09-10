namespace Noctra.Tests;

public sealed class SharedDownloadsPresentationTests
{
    [Fact]
    public void SharedDownloads_OwnsDuplicatedOuterPresentation()
    {
        var shared = Source("Noctra.UI", "Views", "AdaptiveDownloadsContentView.axaml");

        Assert.Contains("Downloads.Title", shared, StringComparison.Ordinal);
        Assert.Contains("Downloads.Storage.Title", shared, StringComparison.Ordinal);
        Assert.Contains("StorageOtherPercent", shared, StringComparison.Ordinal);
        Assert.Contains("StorageNoctraPercent", shared, StringComparison.Ordinal);
        Assert.Contains("StoragePendingPercent", shared, StringComparison.Ordinal);
        Assert.Contains("StorageFreePercent", shared, StringComparison.Ordinal);
        Assert.Contains("Downloads.Storage.Warning", shared, StringComparison.Ordinal);
        Assert.Contains("Downloads.Empty.Title", shared, StringComparison.Ordinal);
        Assert.Contains("DeleteAllDownloadsCommand", shared, StringComparison.Ordinal);
        Assert.Contains("ContentHost", shared, StringComparison.Ordinal);
        Assert.Contains("PlatformStorageActions", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileDownloads_PreservesScrollAndTabBehaviorWhileUsingSharedOuterUi()
    {
        var code = Source("Noctra.Mobile", "Views", "MobileDownloadsView.axaml.cs");
        var adapter = Source("Noctra.Mobile", "Views", "MobileDownloadsView.SharedUi.cs");
        var xaml = Source("Noctra.Mobile", "Views", "MobileDownloadsView.axaml");

        Assert.Contains("InstallSharedDownloadsPresentation();", code, StringComparison.Ordinal);
        Assert.Contains("PrimaryScrollContent.Content is not StackPanel", adapter, StringComparison.Ordinal);
        Assert.Contains("OfType<TabControl>().FirstOrDefault()", adapter, StringComparison.Ordinal);
        Assert.Contains("ContentHost = tabHost", adapter, StringComparison.Ordinal);
        Assert.Contains("PrimaryScrollContent.Content = _sharedDownloadsContent", adapter, StringComparison.Ordinal);
        Assert.Contains("MobileNavigationScrollState.TryCapture(PrimaryScrollContent", code, StringComparison.Ordinal);
        Assert.Contains("OpenDownloadSortSheet_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionSheetHost", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopDownloads_UsesSameOuterUiAndKeepsDesktopOnlyFolderAction()
    {
        var code = Source("Noctra.Avalonia", "Views", "DownloadsView.axaml.cs");
        var adapter = Source("Noctra.Avalonia", "Views", "DownloadsView.SharedUi.cs");

        Assert.Contains("InstallSharedDownloadsPresentation();", code, StringComparison.Ordinal);
        Assert.Contains("DownloadsScrollViewer.Content is not StackPanel", adapter, StringComparison.Ordinal);
        Assert.Contains("ContentHost = tabHost", adapter, StringComparison.Ordinal);
        Assert.Contains("PlatformStorageActions = openFolderButton", adapter, StringComparison.Ordinal);
        Assert.Contains("OpenDownloadsFolderCommand", adapter, StringComparison.Ordinal);
        Assert.Contains("DownloadsScrollViewer.Content = _sharedDownloadsContent", adapter, StringComparison.Ordinal);
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
