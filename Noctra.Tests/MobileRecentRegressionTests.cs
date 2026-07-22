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
    public void MobileSeriesDetail_PreservesIntentionalContinueTintAndAvoidsInvalidActionColors()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml"));

        Assert.DoesNotContain("â€¢", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness=\"1,5\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"#1A8B5CF6\"", source, StringComparison.Ordinal);
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
    public void MobileCategorySelection_FiltersLocallyAndResetsOnEveryShow()
    {
        var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml"));
        var codeBehind = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml.cs"));
        var normalizedCodeBehind = codeBehind.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("x:Name=\"CategorySearchTextBox\"", view, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"{loc:Translate Shell.Search.Tooltip}\"", view, StringComparison.Ordinal);
        Assert.Contains("TextChanged=\"CategorySearchTextBox_TextChanged\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearCategorySearchButton\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"ClearCategorySearch_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Magnify\"", view, StringComparison.Ordinal);
        Assert.Contains("Kind=\"CloseCircleOutline\"", view, StringComparison.Ordinal);

        Assert.Contains("CategorySearchTextBox.Text?.Trim()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("StringComparison.CurrentCultureIgnoreCase", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ClearCategorySearchButton.IsVisible = _categorySearchQuery.Length > 0", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CategorySearchTextBox.Focus();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ResetCategorySearch();\n        RefreshCategories();", normalizedCodeBehind, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel CacheLength=\"1.5\"", view, StringComparison.Ordinal);
        Assert.Contains("Categories.ReplaceAll(items);", codeBehind, StringComparison.Ordinal);
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
    public void MobileRemoteImage_BoundsDecodeWorkWithoutSerializingDownloads()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs"));

        Assert.Contains("DownloadGate = new(6, 6)", source, StringComparison.Ordinal);
        Assert.Contains("DecodeGate = new(2, 2)", source, StringComparison.Ordinal);
        Assert.Contains("DecodeHttpBitmapAsync(memory, decodePixelWidth)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_DropsStaleChannelPagesAfterNavigation()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("SupersedingCancellationScope", source, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource", source, StringComparison.Ordinal);
        Assert.Contains("BeginIncrementalContentGeneration()", source, StringComparison.Ordinal);
        Assert.Contains("IsIncrementalContentRequestCurrent(", source, StringComparison.Ordinal);
        Assert.Contains("LoadMoreChannelsAsync(token, contentGeneration)", source, StringComparison.Ordinal);
        Assert.Contains("!IsIncrementalContentRequestCurrent(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCardMenus_HaveRemoveFromMyListTranslations()
    {
        foreach (var language in new[] { "de-DE", "en-US", "es-ES", "fr-FR", "tr-TR" })
        {
            var source = File.ReadAllText(
                ProjectFile("Noctra.Core", "Localization", "Translations", $"{language}.json"));

            Assert.Contains("\"MyList.Remove\":", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("MobileVodCard.axaml", "#VodCardControl.ShowRemoveMyListMenu")]
    [InlineData("MobileSeriesCard.axaml", "#SeriesCardControl.ShowRemoveMyListMenu")]
    public void MobileMyListCards_ShowExactlyOneMyListAction(
        string cardFile,
        string removeFlagBinding)
    {
        var document = XDocument.Load(ProjectFile("Noctra.Mobile", "Controls", cardFile));
        var menuItems = document
            .Descendants()
            .Where(element => element.Name.LocalName == "MenuItem")
            .ToList();
        var addItem = menuItems.Single(element =>
            element.Attribute("Header")?.Value.Contains("Context.MyList.Toggle", StringComparison.Ordinal) == true);
        var removeItem = menuItems.Single(element =>
            element.Attribute("Header")?.Value.Contains("MyList.Remove", StringComparison.Ordinal) == true);

        var addVisibility = addItem.Attribute("IsVisible")?.Value ?? string.Empty;
        var removeVisibility = removeItem.Attribute("IsVisible")?.Value ?? string.Empty;
        Assert.Contains(removeFlagBinding, addVisibility, StringComparison.Ordinal);
        Assert.Contains("InverseBoolConverter", addVisibility, StringComparison.Ordinal);
        Assert.Contains(removeFlagBinding, removeVisibility, StringComparison.Ordinal);
        Assert.DoesNotContain("InverseBoolConverter", removeVisibility, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileThemeLogos_AreRepositoryAssetsAndDecodedOncePerVariant()
    {
        var assetNames = new[]
        {
            "Square150x150LogoTPLight.png",
            "Square150x150LogoTPDark.png",
            "Square150x150LogoTPFullLight.png",
            "Square150x150LogoTPFullDark.png"
        };
        var gitIgnore = File.ReadAllText(ProjectFile(".gitignore"));
        var converter = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Converters", "LogoThemeConverter.cs"));

        foreach (var assetName in assetNames)
        {
            Assert.True(
                File.Exists(ProjectFile("Noctra.Mobile", "Assets", assetName)),
                $"Missing mobile logo asset: {assetName}");
            Assert.Contains(
                $"!Noctra.Mobile/Assets/{assetName}",
                gitIgnore,
                StringComparison.Ordinal);
            Assert.Contains(assetName, converter, StringComparison.Ordinal);
        }

        Assert.Contains("Lazy<Bitmap?>", converter, StringComparison.Ordinal);
        Assert.Contains("LoadBitmap", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("return new Bitmap(stream);", converter, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImages_UseApproximate64MiBByteBudget()
    {
        var source = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs"));

        Assert.Contains("64L * 1024L * 1024L", source, StringComparison.Ordinal);
        Assert.Contains("ByteBudgetLruCache<string, Bitmap>", source, StringComparison.Ordinal);
        Assert.Contains("EstimateBitmapBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ConcurrentDictionary<string, Bitmap> Cache",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePrimaryCardGrids_VirtualizeRecycledRows()
    {
        var control = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));

        Assert.Contains("VirtualizingStackPanel", control, StringComparison.Ordinal);
        Assert.Contains("IncrementalRowCollection<object, MobileCardGridRow>", control, StringComparison.Ordinal);
        Assert.Contains("OnDataContextChanged", control, StringComparison.Ordinal);

        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", viewName));
            Assert.Contains("<controls:MobileVirtualizingCardGrid", view, StringComparison.Ordinal);
            Assert.DoesNotContain("<WrapPanel HorizontalAlignment=\"Stretch\" />", view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MobilePrimaryCardGrids_AppendPagingRowsWithoutResettingExistingRows()
    {
        var control = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));

        Assert.Contains("IncrementalRowCollection<object, MobileCardGridRow>", control, StringComparison.Ordinal);
        Assert.Contains("NotifyCollectionChangedAction.Add", control, StringComparison.Ordinal);
        Assert.Contains("TryAppend(", control, StringComparison.Ordinal);
        Assert.DoesNotContain("_sourceSnapshot", control, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotSourceItems", control, StringComparison.Ordinal);
        Assert.DoesNotContain("for (var index = 0; index < _sourceSnapshot.Count", control, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePrimaryCardGrids_ReuseCardSlotsWhileRowsAreRecycled()
    {
        var control = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"))
                    + File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileCardRowPresenter.cs"));

        Assert.Contains("EnsureCardSlots", control, StringComparison.Ordinal);
        Assert.Contains("card.DataContext = item", control, StringComparison.Ordinal);
        Assert.Contains("card.IsVisible =", control, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCategorySelection_PreparesRowsAheadOfTheViewport()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml"));

        Assert.Contains("<VirtualizingStackPanel CacheLength=\"1.5\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePrimaryCardGrids_OwnAConstrainedScrollViewport()
    {
        var control = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));

        Assert.Contains("MobileVirtualizingCardGrid : ListBox", control, StringComparison.Ordinal);
        Assert.Contains("StyleKeyOverride => typeof(ListBox)", control, StringComparison.Ordinal);
        Assert.Contains("event EventHandler<ScrollChangedEventArgs>? ScrollChanged", control, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.ScrollChangedEvent", control, StringComparison.Ordinal);

        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", viewName));

            Assert.Contains("RowDefinitions=\"Auto,Auto,*\"", view, StringComparison.Ordinal);
            Assert.DoesNotContain("<ScrollViewer x:Name=", view, StringComparison.Ordinal);
            Assert.Contains("<controls:MobileVirtualizingCardGrid Grid.Row=\"2\"", view, StringComparison.Ordinal);
            Assert.Contains("ScrollChanged=", view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MobileVirtualizedActionLists_DoNotRetainDefaultBlueSelection()
    {
        var cardGrid = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));
        var categories = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml"));
        var categoriesCode = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml.cs"));
        var seriesDetail = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml"));
        var seriesDetailCode = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml.cs"));
        var app = File.ReadAllText(ProjectFile("Noctra.Mobile", "App.axaml"));
        var live = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileLiveView.axaml"));
        var movies = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileMoviesView.axaml"));
        var series = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesView.axaml"));

        Assert.Contains("SelectionChanged += ClearTransientSelection", cardGrid, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex = -1", cardGrid, StringComparison.Ordinal);
        Assert.Contains("ShouldTriggerSelection(Visual source, PointerEventArgs e)", cardGrid, StringComparison.Ordinal);
        Assert.Contains("ShouldTriggerSelection(Visual source, KeyEventArgs e)", cardGrid, StringComparison.Ordinal);
        Assert.Contains("=> false", cardGrid, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TransparentListBoxItemTheme\"", app, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", live, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", movies, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", series, StringComparison.Ordinal);

        Assert.Contains("SelectionChanged=\"ClearTransientSelection\"", categories, StringComparison.Ordinal);
        Assert.Contains("Focusable\" Value=\"False", categories, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", categories, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex = -1", categoriesCode, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"SeasonListBox\"", seriesDetail, StringComparison.Ordinal);
        Assert.Equal(
            2,
            seriesDetail.Split(
                "ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("SelectionChanged=\"ClearTransientSelection\"", seriesDetail, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex = -1", seriesDetailCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSectionedCardFeed_OwnsOneVirtualizedViewportAndSuppressesSelection()
    {
        var source = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileSectionedCardFeed.cs"));

        Assert.Contains("MobileSectionedCardFeed : ListBox", source, StringComparison.Ordinal);
        Assert.Contains(
            "SectionedIncrementalRowCollection<MobileCardSection, object>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("VirtualizingStackPanel", source, StringComparison.Ordinal);
        Assert.Contains("supportsRecycling: true", source, StringComparison.Ordinal);
        Assert.Contains("NotifyCollectionChangedAction.Add", source, StringComparison.Ordinal);
        Assert.Contains("TryAppend(", source, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged += ClearTransientSelection", source, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex = -1", source, StringComparison.Ordinal);
        Assert.Contains("ShouldTriggerSelection(Visual source, PointerEventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("ShouldTriggerSelection(Visual source, KeyEventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("event EventHandler<ScrollChangedEventArgs>? ScrollChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSavedHistoryAndSearchViews_UseOneSectionedVirtualizedFeed()
    {
        foreach (var viewName in new[]
                 {
                     "MobileMyListView.axaml",
                     "MobileFavoritesView.axaml",
                     "MobileHistoryView.axaml",
                     "MobileSearchView.axaml"
                 })
        {
            var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", viewName));

            Assert.Equal(
                1,
                source.Split("<controls:MobileSectionedCardFeed Grid.Row=", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding", source, StringComparison.Ordinal);
            Assert.DoesNotContain("<WrapPanel HorizontalAlignment=\"Stretch\" />", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MobileContinueWatching_RemainsExplicitlyBoundedInsteadOfJoiningTheLargeFeed()
    {
        var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileHomeView.axaml"));
        var viewModel = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("ItemsSource=\"{Binding ContinueWatching}\"", view, StringComparison.Ordinal);
        Assert.Contains(".Take(10)", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_HistoryPagingAppendsRangesWithoutResettingExistingBuckets()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var methodStart = source.IndexOf("public async Task LoadMoreHistoryAsync()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "public async Task LoadMoreHistoryIfNeededAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("HistoryChannels.AddRange(nextPage)", method, StringComparison.Ordinal);
        Assert.Contains("AppendHistoryChannelBuckets(nextPage)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryChannels.Add(item)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateHistoryBucketsAsync()", method, StringComparison.Ordinal);
        Assert.Contains("HistoryLiveChannels.AddRange(", source, StringComparison.Ordinal);
        Assert.Contains("HistoryVodChannels.AddRange(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileVirtualizedFeeds_ShareOneRecyclingCardRowRenderer()
    {
        var primary = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));
        var sectioned = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileSectionedCardFeed.cs"));
        var renderer = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileCardRowPresenter.cs"));

        Assert.Contains("MobileCardRowPresenter", primary, StringComparison.Ordinal);
        Assert.Contains("MobileCardRowPresenter", sectioned, StringComparison.Ordinal);
        Assert.Contains("EnsureCardSlots", renderer, StringComparison.Ordinal);
        Assert.Contains("card.DataContext = item", renderer, StringComparison.Ordinal);
        Assert.Contains("card.IsVisible = item != null", renderer, StringComparison.Ordinal);
        Assert.Contains("MobileCardPresentationMode", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerPlaybackDebugLog_DoesNotWriteRawStreamUrls()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "Player", "PlayerPlaybackController.cs"));

        Assert.DoesNotContain("StreamUrl={channel.StreamUrl}", source, StringComparison.Ordinal);
        Assert.Contains("HasStreamUrl=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileProfileSelection_ShowsLoadingWithoutRequeryingTheSelectedProfile()
    {
        var source = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Views", "ProfileListView.axaml.cs"));
        var methodStart = source.IndexOf(
            "private async void ViewModel_OnProfileSelected(Profile profile)",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "public bool TryHandleBack()",
            methodStart,
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.DoesNotContain("CreateDbContextAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("reloadedProfile", method, StringComparison.Ordinal);
        Assert.Contains("loadingViewModel.SetProfile(profile)", method, StringComparison.Ordinal);
        Assert.Contains("mainViewModel.LoadProfileAsync(profile)", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("ProfileLoadingHost.IsVisible = true", StringComparison.Ordinal) <
            method.IndexOf("mainViewModel.LoadProfileAsync(profile)", StringComparison.Ordinal));
        Assert.Contains("DispatcherPriority.Background", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("DispatcherPriority.Background", StringComparison.Ordinal) <
            method.IndexOf("mainViewModel.LoadProfileAsync(profile)", StringComparison.Ordinal));
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
