namespace Noctra.Tests;

public sealed class MobileDownloadsVirtualizationTests
{
    [Fact]
    public void DownloadCenterUsesVirtualizedListsForLargeCollections()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileDownloadsView.axaml");

        Assert.Contains("ItemsSource=\"{Binding ActiveDownloadingItems}\"", view, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding QueuedDownloadItems}\"", view, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FailedDownloadItems}\"", view, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel CacheLength=\"0.5\" />", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<ItemsControl ItemsSource=\"{Binding ActiveDownloadingItems}\">",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<ItemsControl ItemsSource=\"{Binding QueuedDownloadItems}\">",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<ItemsControl ItemsSource=\"{Binding FailedDownloadItems}\">",
            view,
            StringComparison.Ordinal);

        var viewModel = ReadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        Assert.Contains("ScheduleDownloadCenterRefresh", viewModel, StringComparison.Ordinal);
        Assert.Contains("_downloadsRefreshGate", viewModel, StringComparison.Ordinal);

        var downloadsNavigation = Slice(
            viewModel,
            "else if (view == AppView.Downloads)",
            "else SelectedChannelType = null;");
        Assert.DoesNotContain(
            "_ = RefreshDownloadedItemsFromDatabaseAsync();",
            downloadsNavigation,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (!IsDownloadCenterVisible)",
            downloadsNavigation,
            StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find source marker: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not find source marker: {end}");
        return source[startIndex..endIndex];
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
