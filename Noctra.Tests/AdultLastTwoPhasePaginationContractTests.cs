using System.Text.RegularExpressions;

namespace Noctra.Tests;

public sealed class AdultLastTwoPhasePaginationContractTests
{
    [Fact]
    public void TwoPhaseKeysetAndCallerAuthoritativeAdultGroupsArePinned()
    {
        var playlistService = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "PlaylistService.cs"));
        var viewModel = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var requestContract = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "Interfaces", "IContentQueryService.cs"));

        Assert.Contains("GetTwoPhaseKeysetPageAsync", playlistService, StringComparison.Ordinal);
        Assert.Contains(
            "callerAdultGroups ?? await GetAdultGroupsAsync(context, playlistId, cancellationToken)",
            playlistService,
            StringComparison.Ordinal);
        Assert.Contains("AdultPhase", requestContract, StringComparison.Ordinal);
        Assert.Contains("_lastChannelCursorAdultPhase", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileResetSuppressesFilterSideEffectsIncludingSortOrder()
    {
        var viewModel = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "MainViewModel.cs"));

        var sortHandler = Regex.Match(
            viewModel,
            @"OnSelectedSortOrderChanged\(ChannelSortOrder value\)\s*\{.*?\n    \}",
            RegexOptions.Singleline);
        Assert.True(sortHandler.Success, "OnSelectedSortOrderChanged handler not found");
        Assert.Contains(
            "_suppressFilterRefresh || _suppressNavigationFilterRefresh",
            sortHandler.Value,
            StringComparison.Ordinal);

        var resetMethod = Regex.Match(
            viewModel,
            @"private void ClearProfileState\(\)\s*\{.*?\n    \}",
            RegexOptions.Singleline);
        Assert.True(resetMethod.Success, "ClearProfileState not found");
        Assert.Contains("CancelPendingFilterRequests();", resetMethod.Value, StringComparison.Ordinal);
        Assert.Contains("_suppressFilterRefresh = true;", resetMethod.Value, StringComparison.Ordinal);
        Assert.Contains("finally", resetMethod.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleFilterFailuresDoNotSurfaceAsUserVisibleErrors()
    {
        var viewModel = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "MainViewModel.cs"));

        var catchBlock = Regex.Match(
            viewModel,
            @"catch \(Exception ex\)\s*\{\s*if \(ex is OperationCanceledException \|\|\s*request\.Token\.IsCancellationRequested.*?return false;\s*\}",
            RegexOptions.Singleline);
        Assert.True(catchBlock.Success, "stale-request guard before FilterError not found");
        Assert.Contains(
            "!ReferenceEquals(Volatile.Read(ref _activeFilterRequest), request)",
            catchBlock.Value,
            StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
