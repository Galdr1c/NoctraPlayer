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
