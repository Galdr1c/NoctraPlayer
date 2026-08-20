namespace Noctra.Tests;

public sealed class EpgVisibleRefreshContractTests
{
    [Fact]
    public void VirtualizedGridsExposeRealizedSourceItemsAndLiveViewsPublishThem()
    {
        var mobileGrid = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));
        var desktopGrid = File.ReadAllText(ProjectSource(
            "Noctra.Avalonia", "Controls", "DesktopVirtualizingCardGrid.cs"));
        var mobileLive = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Views", "MobileLiveView.axaml.cs"));
        var desktopLive = File.ReadAllText(ProjectSource(
            "Noctra.Avalonia", "Views", "LiveView.axaml.cs"));
        var mainViewModel = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("GetVisibleSourceItems", mobileGrid, StringComparison.Ordinal);
        Assert.Contains("GetVisibleSourceItems", desktopGrid, StringComparison.Ordinal);
        Assert.Contains("SetVisibleEpgChannels", mobileLive, StringComparison.Ordinal);
        Assert.Contains("SetVisibleEpgChannels", desktopLive, StringComparison.Ordinal);
        Assert.Contains("FilteredChannels.CollectionChanged", mobileLive, StringComparison.Ordinal);
        Assert.Contains("FilteredChannels.CollectionChanged", desktopLive, StringComparison.Ordinal);
        Assert.Contains("var expectedGeneration = viewModel.VisibleEpgChannelsInvalidationGeneration", mobileLive, StringComparison.Ordinal);
        Assert.Contains("var expectedGeneration = viewModel.VisibleEpgChannelsInvalidationGeneration", desktopLive, StringComparison.Ordinal);
        Assert.Contains("PublishVisibleEpgSnapshot(viewModel, expectedGeneration)", mobileLive, StringComparison.Ordinal);
        Assert.Contains("PublishVisibleEpgSnapshot(viewModel, expectedGeneration)", desktopLive, StringComparison.Ordinal);
        Assert.DoesNotContain("EnrichChannelsWithEpgAsync(Channels)", mainViewModel, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
