using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class MobileRecentRegressionTests
{
    [Fact]
    public void MobileReviewPrompt_UsesStackedResponsiveActions()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileReviewPromptView.axaml"));

        Assert.DoesNotContain("Width\" Value=\"400", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Width\" Value=\"180", source, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,Auto,Auto\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"0\" Classes=\"ReviewSecondary\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\" Classes=\"ReviewLater\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\" Background=\"Transparent\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSeriesDetail_DoesNotReintroduceBrokenGlyphsOrHardcodedActionColors()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml"));

        Assert.DoesNotContain("â€¢", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness=\"1,5\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#1A8B5CF6\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#1A000000\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#E50914\"", source, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource ErrorBrush}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCategorySelection_VirtualizesRowsAndRefreshesInOneBatch()
    {
        var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml"));
        var codeBehind = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml.cs"));

        Assert.Contains("<VirtualizingStackPanel", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding Categories}\"", view, StringComparison.Ordinal);
        Assert.Contains("BatchObservableCollection<MobileCategorySelectionItem>", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Categories.ReplaceAll(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Categories.Add(", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSeriesDetail_VirtualizesEpisodesInsideBoundedViewport()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml"));

        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding SelectedSeason.Episodes}\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EpisodeListBox\"", source, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImage_DecodesBitmapsToBoundedDisplaySize()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs"));

        Assert.Contains("DecodePixelWidthProperty", source, StringComparison.Ordinal);
        Assert.Contains("Bitmap.DecodeToWidth", source, StringComparison.Ordinal);
        Assert.Contains("DefaultDecodePixelWidth", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var bitmap = new Bitmap(memory);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxCacheEntries = 500", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePrimaryCardGrids_VirtualizeRecycledRows()
    {
        var control = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));

        Assert.Contains("VirtualizingStackPanel", control, StringComparison.Ordinal);
        Assert.Contains("BatchObservableCollection<MobileCardGridRow>", control, StringComparison.Ordinal);
        Assert.Contains("OnDataContextChanged", control, StringComparison.Ordinal);

        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", viewName));
            Assert.Contains("<controls:MobileVirtualizingCardGrid", view, StringComparison.Ordinal);
            Assert.DoesNotContain("<WrapPanel HorizontalAlignment=\"Stretch\" />", view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PlayerPlaybackDebugLog_DoesNotWriteRawStreamUrls()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "Player", "PlayerPlaybackController.cs"));

        Assert.DoesNotContain("StreamUrl={channel.StreamUrl}", source, StringComparison.Ordinal);
        Assert.Contains("HasStreamUrl=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileHotReload_DoesNotForceOverlayOrHeaderState()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs"));
        var methodStart = source.IndexOf("private void OnHotReload()", StringComparison.Ordinal);
        var constructorStart = source.IndexOf("public MainView()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(constructorStart > methodStart);
        var method = source[methodStart..constructorStart];

        Assert.DoesNotContain("ProfilesOverlay.IsVisible = false", method, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderBar.IsVisible = true", method, StringComparison.Ordinal);
        Assert.Contains("UpdatePlayerChromeState()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SqliteBundle_UsesPatchedPackageVersion()
    {
        var coreProject = XDocument.Load(ProjectFile("Noctra.Core", "Noctra.Core.csproj"));
        var sqliteBundleReference = coreProject
            .Descendants("PackageReference")
            .Single(reference =>
                reference.Attribute("Include")?.Value == "SQLitePCLRaw.bundle_e_sqlite3");

        Assert.NotEqual("2.1.11", sqliteBundleReference.Attribute("Version")?.Value);
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
