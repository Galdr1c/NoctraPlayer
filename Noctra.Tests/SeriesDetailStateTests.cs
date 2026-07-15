namespace Noctra.Tests;

public class SeriesDetailStateTests
{
    [Fact]
    public void SeriesSelection_PublishesDetailShellBeforeBackgroundLoad()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var branchStart = source.IndexOf("else if (media is Series series)", StringComparison.Ordinal);
        var branchEnd = source.IndexOf("private void PublishSeriesDetailShell", branchStart, StringComparison.Ordinal);

        Assert.True(branchStart >= 0);
        Assert.True(branchEnd > branchStart);

        var branch = source[branchStart..branchEnd];
        var publishShell = branch.IndexOf("PublishSeriesDetailShell(series, isSyntheticDownloadSeries);", StringComparison.Ordinal);
        var startBackgroundLoad = branch.IndexOf("CompleteSeriesDetailSelectionAsync(", StringComparison.Ordinal);

        Assert.True(publishShell >= 0, "Series selection must publish its lightweight detail shell synchronously.");
        Assert.True(startBackgroundLoad > publishShell, "Provider/DB detail work must start only after the shell is published.");
        Assert.DoesNotContain("await LoadSeriesWithProfileProgressAsync", branch);
    }

    [Fact]
    public void SeriesDetailLoad_IsCancelledWhenDetailOrProfileStateCloses()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");

        AssertMethodContains(source, "private void CloseSeriesDetail()", "CancelSeriesDetailLoad();");
        AssertMethodContains(source, "private void ClearProfileState()", "CancelSeriesDetailLoad();");
        AssertMethodContains(source, "private void ResetUIForRefresh()", "CancelSeriesDetailLoad();");
    }

    [Fact]
    public void SeriesDetailCancellation_LeavesDisposalToTheRunningLoadOwner()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var cancelMethod = MethodSlice(source, "private void CancelSeriesDetailLoad()", "[ObservableProperty]");
        var completionMethod = MethodSlice(source, "private async Task CompleteSeriesDetailSelectionAsync", "private static string NormalizeSeriesQuery");

        Assert.DoesNotContain("previous.Dispose();", cancelMethod, StringComparison.Ordinal);
        Assert.Contains("loadCts.Dispose();", completionMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregationRefresh_ReusesCancellableSeriesSelectionPipeline()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var handler = MethodSlice(source, "_mediaService.OnAggregationCompleted +=", "public bool IsPremium");

        Assert.Contains("await SelectMedia(SelectedSeries);", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("await LoadSeriesWithProfileProgressAsync(SelectedSeries)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesProviderDetailLoad_PropagatesCancellationToDatabaseAndProviderCalls()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var completion = MethodSlice(source, "private async Task CompleteSeriesDetailSelectionAsync", "private static string NormalizeSeriesQuery");
        var providerLoad = MethodSlice(source, "private async Task<Series> LoadSeriesWithProfileProgressAsync", "private bool ShouldUseProviderOnlySeriesMetadata");
        var lazyLoad = MethodSlice(source, "private async Task LazyLoadProviderSeriesEpisodesAsync", "private static void SyncSeriesDetailState");

        Assert.Contains("LoadSeriesWithProfileProgressAsync(series, loadCts.Token)", completion, StringComparison.Ordinal);
        Assert.Contains("CancellationToken cancellationToken", providerLoad, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync", providerLoad, StringComparison.Ordinal);
        Assert.Contains("cancellationToken", lazyLoad, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesMetadataLoad_ClearsPreviousEpisodeStateBeforeRecomputing()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var methodStart = source.IndexOf("private Task LoadSelectedSeriesMetadataAsync", StringComparison.Ordinal);
        var episodeListStart = source.IndexOf("var allEpisodes = series.Seasons", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(episodeListStart > methodStart);

        var setupBlock = source[methodStart..episodeListStart];
        Assert.Contains("SelectedSeriesContinueEpisode = null;", setupBlock);
        Assert.Contains("SelectedSeriesContinueText = string.Empty;", setupBlock);
        Assert.Contains("SelectedSeason = null;", setupBlock);
    }

    [Fact]
    public void SeriesPlayCommands_DoNotExposeActionsWithoutPlayableEpisode()
    {
        var view = LoadProjectFile("Noctra.Avalonia", "MainWindow.axaml");

        Assert.Contains("HasSelectedSeriesPlayableEpisode", view);
        Assert.Contains(
            "Command=\"{Binding PlayEpisodeCommand}\" CommandParameter=\"{Binding SelectedSeriesContinueEpisode}\"",
            view);
        Assert.Contains("IsVisible=\"{Binding HasSelectedSeriesPlayableEpisode}\"", view);
    }

    [Fact]
    public void EpisodeLoadingState_IsSeriesDetailScopedAndHasEmptyProviderResult()
    {
        var view = LoadProjectFile("Noctra.Avalonia", "MainWindow.axaml");
        var loadingSectionStart = view.IndexOf("<!-- Episodes Loading Spinner -->", StringComparison.Ordinal);
        var episodesSectionStart = view.IndexOf("<!-- Seasons & Episodes -->", StringComparison.Ordinal);

        Assert.True(loadingSectionStart >= 0);
        Assert.True(episodesSectionStart > loadingSectionStart);

        var episodeStateSection = view[loadingSectionStart..episodesSectionStart];
        Assert.Contains("IsVisible=\"{Binding ShowSelectedSeriesEpisodesLoading}\"", episodeStateSection);
        Assert.Contains("IsVisible=\"{Binding ShowSelectedSeriesNoEpisodes}\"", episodeStateSection);
        Assert.Contains("Series.Episodes.Empty", episodeStateSection);
        Assert.DoesNotContain("IsChannelLoading", episodeStateSection);
    }

    [Fact]
    public void EpisodeEmptyState_HasTranslations()
    {
        foreach (var culture in new[] { "en-US", "tr-TR", "de-DE", "es-ES", "fr-FR" })
        {
            var translation = LoadProjectFile("Noctra.Core", "Localization", "Translations", $"{culture}.json");

            Assert.Contains("\"Series.Episodes.Empty\"", translation);
            Assert.Contains("\"Series.Episodes.EmptyDetail\"", translation);
        }
    }

    [Fact]
    public void PlayEpisodeCommand_AcceptsNullableEpisodeParameter()
    {
        var source = LoadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");

        Assert.Contains("private void PlayEpisode(Episode? episode)", source);
        Assert.Contains("if (episode == null)", source);
    }

    private static string LoadProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(relativeParts)}");
    }

    private static void AssertMethodContains(string source, string signature, string expected)
    {
        var methodStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find {signature}");

        var nextMethod = source.IndexOf("\n    private ", methodStart + signature.Length, StringComparison.Ordinal);
        if (nextMethod < 0)
        {
            nextMethod = source.Length;
        }

        Assert.Contains(expected, source[methodStart..nextMethod]);
    }

    private static string MethodSlice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not find {end}");
        return source[startIndex..endIndex];
    }
}
