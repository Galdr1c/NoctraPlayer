namespace Noctra.Tests;

public sealed class BrowsePerformanceRegressionTests
{
    [Fact]
    public void ChannelPaging_DoesNotRefreshUnrelatedAuxiliaryCaches()
    {
        var method = MainViewModelMethod("public async Task LoadMoreChannelsAsync", "public async Task LoadMoreChannelsIfNeededAsync");

        Assert.DoesNotContain("UpdateMyList();", method, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateFavoriteChannels();", method, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateHistoryChannels();", method, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateDownloadedItems();", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderImport_DefersPostLoadEpgWorkUntilImportFinishes()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var loadChannels = Slice(source, "private async Task LoadChannelsAsync", "private async Task ReloadCurrentPlaylistUiAfterRefreshAsync");
        var loadingChanged = Slice(source, "partial void OnIsChannelLoadingChanged", "partial void OnActiveViewChanged");

        Assert.Contains("DeferPostChannelLoadBackgroundTasks", loadChannels, StringComparison.Ordinal);
        Assert.Contains("RunDeferredPostChannelLoadBackgroundTasks", loadingChanged, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesAllFilter_ReusesProviderNeutralSortedCache()
    {
        var method = MainViewModelMethod("private void UpdateSeriesViewItems()", "private void CommitSearch");

        Assert.Contains("GetOrBuildAllSeriesSort", method, StringComparison.Ordinal);
    }

    private static string MainViewModelMethod(string start, string end)
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        return Slice(source, start, end);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find method start: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not find method end: {end}");
        return source[startIndex..endIndex];
    }

    private static string ProjectFile(params string[] segments)
        => Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
