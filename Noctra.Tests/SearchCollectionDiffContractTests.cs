namespace Noctra.Tests;

public sealed class SearchCollectionDiffContractTests
{
    [Fact]
    public void MainViewModel_SearchCommitsAndClearsUseIdentitySynchronization()
    {
        var source = Source("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var commitStart = source.IndexOf("private async Task<bool> UpdateSearchBucketsIncrementallyAsync", StringComparison.Ordinal);
        var commitEnd = source.IndexOf("private const int SearchSimilarScoreThreshold", commitStart, StringComparison.Ordinal);
        var commit = source[commitStart..commitEnd];

        Assert.Equal(6, Count(commit, "SynchronizeSearchItems("));
        Assert.DoesNotContain("SetItems(Search", commit, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchLiveChannels.Clear()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchSeriesChannels.Clear()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchVodChannels.Clear()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileAndDesktopSectionFeedsUseSectionLocalSynchronizationFallback()
    {
        var mobile = Source("Noctra.Mobile", "Controls", "MobileSectionedCardFeed.cs");
        var desktop = Source("Noctra.Avalonia", "Controls", "DesktopSectionedCardFeed.cs");

        Assert.Contains("TrySynchronizeSection", mobile, StringComparison.Ordinal);
        Assert.Contains("TrySynchronizeSection", desktop, StringComparison.Ordinal);
        Assert.Contains("ShouldRefreshFollowingGroupHeader", mobile, StringComparison.Ordinal);
        Assert.Contains("ShouldRefreshFollowingGroupHeader", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchViews_DoNotTriggerGenericPagingWhileUserScrolls()
    {
        var mobileView = Source("Noctra.Mobile", "Views", "MobileSearchView.axaml");
        var mobileCode = Source("Noctra.Mobile", "Views", "MobileSearchView.axaml.cs");
        var desktopView = Source("Noctra.Avalonia", "Views", "SearchView.axaml");
        var desktopCode = Source("Noctra.Avalonia", "Views", "SearchView.axaml.cs");

        Assert.DoesNotContain("ScrollChanged=", mobileView, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileScrollPaging", mobileCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollChanged=", desktopView, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollPaging", desktopCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchFeeds_DimAndRejectInputWhileNewQueryIsLoading()
    {
        var mobile = Source("Noctra.Mobile", "Views", "MobileSearchView.axaml");
        var desktop = Source("Noctra.Avalonia", "Views", "SearchView.axaml");

        Assert.Contains(
            "IsHitTestVisible=\"{Binding IsSearching, Converter={StaticResource InverseBoolConverter}}\"",
            mobile,
            StringComparison.Ordinal);
        Assert.Contains(
            "Opacity=\"{Binding IsSearching, Converter={StaticResource BoolToOpacityConverter}, ConverterParameter=0.4}\"",
            mobile,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsHitTestVisible=\"{Binding IsSearching, Converter={StaticResource InverseBoolConverter}}\"",
            desktop,
            StringComparison.Ordinal);
        Assert.Contains(
            "Opacity=\"{Binding IsSearching, Converter={StaticResource BoolToOpacityConverter}, ConverterParameter=0.4}\"",
            desktop,
            StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsSearching}\"", desktop, StringComparison.Ordinal);
        Assert.Contains("Search.Searching", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchViews_UnsubscribeWhenDataContextIsCleared()
    {
        var mobile = Source("Noctra.Mobile", "Views", "MobileSearchView.axaml.cs");
        var desktop = Source("Noctra.Avalonia", "Views", "SearchView.axaml.cs");

        Assert.Contains("DataContext is not MainViewModel", mobile, StringComparison.Ordinal);
        Assert.Contains("ViewModel is not", desktop, StringComparison.Ordinal);
        Assert.Contains("UnsubscribeFromSearchReset();", mobile, StringComparison.Ordinal);
        Assert.Contains("UnsubscribeFromSearchReset();", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSearch_UsesShortCoalescingDelayInsteadOfTypingDebounce()
    {
        var source = Source("Noctra.Core", "ViewModels", "MainViewModel.cs");

        Assert.Contains("private readonly int _filterDelayMs = 75;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly int _filterDelayMs = 300;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesPaging_MarshalsObservableCollectionMutationThroughDispatcher()
    {
        var source = Source("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var start = source.IndexOf("public Task LoadMoreSeriesAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private bool ShouldUseTmdbVisualEnrichment", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("_dispatcherService.Invoke", method, StringComparison.Ordinal);
        Assert.Contains("SeriesViewItems.AddRange", method, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Source(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. path]));
    }
}
