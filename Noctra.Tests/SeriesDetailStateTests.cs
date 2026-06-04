namespace Noctra.Tests;

public class SeriesDetailStateTests
{
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
}
