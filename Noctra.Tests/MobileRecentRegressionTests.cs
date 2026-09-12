using System.Xml.Linq;
using System.Text.Json;

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
    public void MobileSeriesDetail_DownloadActionsExposeStatusToast()
    {
        var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml"));
        var codeBehind = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml.cs"));
        var mainViewModel = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var zIndex = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileZIndex.cs"));

        Assert.Contains("Command=\"{Binding DownloadSelectedSeasonCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("DownloadEpisodeCommand", view, StringComparison.Ordinal);
        Assert.Contains("IsStatusToastVisible", view, StringComparison.Ordinal);
        Assert.Contains("protected override void OnDataContextChanged(EventArgs e)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContextChanged += OnDataContextChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.DownloadStatusMessage)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.IsDownloadInProgress)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DownloadStatusMessage", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("DownloadStatusMessage = result.Message", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("PlayerSheet = 100", zIndex, StringComparison.Ordinal);
        Assert.Contains("PlayerDownloadToast = 110", zIndex, StringComparison.Ordinal);

        var rootTagEnd = view.IndexOf('>');
        Assert.True(rootTagEnd > 0, "Series detail root element could not be located.");
        var rootTag = view[..(rootTagEnd + 1)];
        Assert.Contains("x:Name=\"SeriesDetailRoot\"", rootTag, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid x:Name=\"SeriesDetailRoot\"", view, StringComparison.Ordinal);
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
    public void DesktopSeriesDetail_VirtualizesEpisodesInsideBoundedViewport()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Avalonia", "MainWindow.axaml"));

        Assert.DoesNotContain(
            "<ItemsControl Grid.Row=\"1\" ItemsSource=\"{Binding SelectedSeason.Episodes}\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DesktopEpisodeListBox\"", source, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=", source, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", source, StringComparison.Ordinal);
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
        Assert.Contains(
            "DecodeHttpBitmapAsync(memory, decodePixelWidth, cancellationToken)",
            source,
            StringComparison.Ordinal);
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
    public void MobileCardActionSheet_HasUnambiguousLocalizedAddAndRemoveLabels()
    {
        foreach (var language in new[] { "de-DE", "en-US", "es-ES", "fr-FR", "tr-TR" })
        {
            var source = File.ReadAllText(
                ProjectFile("Noctra.Core", "Localization", "Translations", $"{language}.json"));

            Assert.Contains("\"MyList.Add\":", source, StringComparison.Ordinal);
            Assert.Contains("\"MyList.Remove\":", source, StringComparison.Ordinal);
            Assert.Contains("\"Favorites.Remove\":", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MobileMyListCards_GetExactlyOneRemoveActionFromTheSharedPolicy()
    {
        var policy = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileCardActions.cs"));
        var presenter = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileCardRowPresenter.cs"));

        foreach (var cardFile in new[] { "MobileVodCard.axaml", "MobileSeriesCard.axaml" })
        {
            var card = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", cardFile));
            Assert.DoesNotContain("<MenuItem", card, StringComparison.Ordinal);
            Assert.DoesNotContain("ContextFlyout", card, StringComparison.Ordinal);
        }

        Assert.Equal(
            2,
            policy.Split("MobileCardPresentationMode.MyList", StringSplitOptions.None).Length - 1);
        Assert.Contains("MobileCardActionKind.RemoveFromMyList", policy, StringComparison.Ordinal);
        Assert.Contains("PresentationMode = mode", presenter, StringComparison.Ordinal);
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
        Assert.Contains(
            "ByteBudgetLruCache<string, SharedImageResource<Bitmap>>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("resource => resource.Dispose()", source, StringComparison.Ordinal);
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
        var sharedCatalog = File.ReadAllText(
            ProjectFile("Noctra.UI", "Views", "AdaptiveCatalogView.axaml"));
        Assert.Contains("RowDefinitions=\"Auto,Auto,*\"", sharedCatalog, StringComparison.Ordinal);
        Assert.Contains("<ContentControl Grid.Row=\"2\"", sharedCatalog, StringComparison.Ordinal);

        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", viewName));

            Assert.DoesNotContain("<ScrollViewer x:Name=", view, StringComparison.Ordinal);
            Assert.Contains("<controls:MobileVirtualizingCardGrid", view, StringComparison.Ordinal);
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
        var sharedStyles = File.ReadAllText(ProjectFile("Noctra.UI", "Resources", "Styles.axaml"));
        var live = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileLiveView.axaml"));
        var movies = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileMoviesView.axaml"));
        var series = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSeriesView.axaml"));

        Assert.Contains("SelectionChanged += ClearTransientSelection", cardGrid, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex = -1", cardGrid, StringComparison.Ordinal);
        Assert.Contains("ShouldTriggerSelection(Visual source, PointerEventArgs e)", cardGrid, StringComparison.Ordinal);
        Assert.Contains("ShouldTriggerSelection(Visual source, KeyEventArgs e)", cardGrid, StringComparison.Ordinal);
        Assert.Contains("=> false", cardGrid, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TransparentListBoxItemTheme\"", sharedStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TransparentListBoxItemTheme\"", app, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", live, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", movies, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", series, StringComparison.Ordinal);

        Assert.Contains("SelectionChanged=\"ClearTransientSelection\"", categories, StringComparison.Ordinal);
        Assert.Contains("Focusable\" Value=\"False", categories, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"", categories, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex = -1", categoriesCode, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"SeasonListBox\"", seriesDetail, StringComparison.Ordinal);
        Assert.Equal(
            1,
            seriesDetail.Split(
                "ItemContainerTheme=\"{StaticResource TransparentListBoxItemTheme}\"",
                StringSplitOptions.None).Length - 1);
        // The season tabs intentionally use a custom container theme for the
        // active underline; only the episode list should use the transparent
        // selection theme counted above.
        Assert.Contains("ListBox.ItemContainerTheme", seriesDetail, StringComparison.Ordinal);
        Assert.Contains("SeasonUnderline", seriesDetail, StringComparison.Ordinal);
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
    public void MobileHistory_OwnsTheOnlyClearHistoryAction()
    {
        var history = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileHistoryView.axaml"));
        var settings = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml"));
        var viewModel = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("Command=\"{Binding ClearHistoryCommand}\"", history, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Privacy.ClearAllNow", settings, StringComparison.Ordinal);
        Assert.Contains("private async Task ClearHistoryAsync()", viewModel, StringComparison.Ordinal);
        Assert.Contains(
            "await _watchHistoryService.DeleteProfileHistoryAsync(profileId.Value)",
            viewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobileHistoryAndVodProgressBars_AreClippedToTheirCards()
    {
        var liveCard = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileLiveTvCard.axaml"));
        var vodCard = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVodCard.axaml"));

        Assert.Contains("<ProgressBar Grid.Row=\"2\"\r\n                     ClipToBounds=\"True\"", liveCard, StringComparison.Ordinal);
        Assert.Contains("<Border Background=\"Transparent\"\r\n            ClipToBounds=\"True\">", vodCard, StringComparison.Ordinal);
        Assert.Contains("<ProgressBar Height=\"4\"\r\n                       HorizontalAlignment=\"Stretch\"\r\n                       ClipToBounds=\"True\"", vodCard, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"#VodCardControl.ShowHistoryMenu\" />", vodCard, StringComparison.Ordinal);
        Assert.Contains(
            "<Binding Path=\".\" Converter=\"{StaticResource WatchedProgressVisibilityConverter}\" />",
            vodCard,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MobileVodCard.axaml")]
    [InlineData("MobileSeriesCard.axaml")]
    [InlineData("MobileLiveTvCard.axaml")]
    [InlineData("MobileContinueWatchingCard.axaml")]
    public void PrimaryMediaCards_UseARealPressedState(string cardFile)
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", cardFile));

        Assert.Contains(
            "<controls:MobilePressableCard Classes=\"CardContainer\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "controls|MobilePressableCard.CardContainer:pressed",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePressableCard_CancelsFeedbackWhenTheGestureBecomesScrolling()
    {
        var source = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobilePressableCard.cs"));

        Assert.Contains("ScrollCancellationDistance", source, StringComparison.Ordinal);
        Assert.Contains("PseudoClasses.Set(\":pressed\", true)", source, StringComparison.Ordinal);
        Assert.Contains("OriginatesFromNestedButton(e.Source)", source, StringComparison.Ordinal);
        Assert.Contains("ResetPressedState();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Pointer.Capture(", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MobileVodCard.axaml")]
    [InlineData("MobileSeriesCard.axaml")]
    [InlineData("MobileLiveTvCard.axaml")]
    [InlineData("MobileContinueWatchingCard.axaml")]
    public void PrimaryMediaCards_UseOnlyTheSharedLongPressActionSheet(string cardFile)
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", cardFile));

        Assert.Contains("LongPressed=\"CardContainer_LongPressed\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuFlyout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextFlyout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePressableCard_LongPressIsCancellableAndSuppressesTheFollowingTap()
    {
        var source = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobilePressableCard.cs"));

        Assert.Contains("TimeSpan.FromMilliseconds(500)", source, StringComparison.Ordinal);
        Assert.Contains("_longPressTimer.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("ScrollCancellationDistance", source, StringComparison.Ordinal);
        Assert.Contains("_suppressNextTap = true", source, StringComparison.Ordinal);
        Assert.Contains("ConsumeLongPressTapSuppression()", source, StringComparison.Ordinal);
        Assert.Contains("longPressed?.Invoke(this, EventArgs.Empty)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCardActionPolicy_UsesModeAndMediaSpecificNonDuplicatedActions()
    {
        var source = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Controls", "MobileCardActions.cs"));

        Assert.Contains("MobileCardGridKind.ContinueWatching", source, StringComparison.Ordinal);
        Assert.Contains("MobileCardGridKind.Live", source, StringComparison.Ordinal);
        Assert.Contains("MobileCardPresentationMode.MyList", source, StringComparison.Ordinal);
        Assert.Contains("MobileCardPresentationMode.Favorites", source, StringComparison.Ordinal);
        Assert.Contains("MobileCardPresentationMode.History", source, StringComparison.Ordinal);
        Assert.Contains("BuildHistoryActions", source, StringComparison.Ordinal);
        Assert.Contains("IsDestructive", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileCardActionKind.ToggleFavorite,\r\n                    MobileCardActionKind.RemoveFromFavorites", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_HostsOneRootCardActionSheetWithFirstNavigationBackPriority()
    {
        var xaml = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MainView.axaml"));
        var codeBehind = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs"));
        var sheet = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Views", "MobileCardActionsSheet.axaml"));

        Assert.Equal(
            1,
            xaml.Split("<views:MobileCardActionsSheet ", StringSplitOptions.None).Length - 1);
        Assert.Contains("x:Name=\"CardActionsSheet\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MobileZIndex.ShellCardActions", xaml, StringComparison.Ordinal);
        Assert.True(
            codeBehind.IndexOf("if (CardActionsSheet.TryClose())", StringComparison.Ordinal) <
            codeBehind.IndexOf("if (CategorySelectionOverlay.TryClose())", StringComparison.Ordinal));
        Assert.Contains("MobileCardActions.RequestedEvent", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CanExecuteCardAction", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource OverlayDimBrush}\"", sheet, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Bottom\"", sheet, StringComparison.Ordinal);
        Assert.Contains("Classes.destructive=\"{Binding IsDestructive}\"", sheet, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Label}\"", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_CoreContentHostIsClippedAboveTheNativeBannerRow()
    {
        var xaml = XDocument.Load(ProjectFile("Noctra.Mobile", "Views", "MainView.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var coreHost = xaml.Descendants()
            .First(element => (string?)element.Attribute(x + "Name") == "CoreContentHost");
        var banner = xaml.Descendants()
            .First(element => (string?)element.Attribute(x + "Name") == "BannerAd");

        Assert.Equal("0", (string?)coreHost.Attribute("Grid.Row"));
        Assert.Equal("True", (string?)coreHost.Attribute("ClipToBounds"));
        Assert.Equal("1", (string?)banner.Attribute("Grid.Row"));
    }

    [Fact]
    public void MobileUpsell_CloseButtonHasDedicatedHeaderRowAboveScrollViewer()
    {
        var xaml = XDocument.Load(ProjectFile("Noctra.Mobile", "Views", "MobileUpsellView.axaml"));
        var closeButton = xaml.Descendants()
            .First(element => (string?)element.Attribute("Click") == "Close_Click" &&
                              (string?)element.Attribute("Width") == "44");
        var scrollViewer = xaml.Descendants().First(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("0", (string?)closeButton.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)scrollViewer.Attribute("Grid.Row"));
        Assert.Equal("Grid", closeButton.Parent?.Name.LocalName);
    }

    [Fact]
    public void MobilePlayerSheet_StretchesAcrossTheViewportWhileOnlyItsSurfaceAlignsToTheBottom()
    {
        var playerView = XDocument.Load(
            ProjectFile("Noctra.Mobile", "Views", "MobilePlayerView.axaml"));
        var sheetView = XDocument.Load(
            ProjectFile("Noctra.Mobile", "Views", "MobilePlayerSheets.axaml"));

        var sheetHost = playerView
            .Descendants()
            .Single(element => element.Name.LocalName == "MobilePlayerSheets");
        var scrim = sheetView
            .Descendants()
            .Single(element => element.Attributes().Any(
                attribute => attribute.Name.LocalName == "Name" &&
                             attribute.Value == "ScrimLayer"));
        var sheetSurface = sheetView
            .Descendants()
            .Single(element => element.Attributes().Any(
                attribute => attribute.Name.LocalName == "Name" &&
                             attribute.Value == "SheetSurface"));

        Assert.Null(sheetHost.Attribute("VerticalAlignment"));
        Assert.Null(scrim.Attribute("VerticalAlignment"));
        Assert.Equal("Bottom", sheetSurface.Attribute("VerticalAlignment")?.Value);
    }

    [Fact]
    public void MobileNextEpisodePrompt_BindsCountdownCancelAndExistingProfileSetting()
    {
        var playerView = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Views", "MobilePlayerView.axaml"));
        var playerSheet = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Views", "MobilePlayerSheets.axaml"));
        var settingsView = File.ReadAllText(
            ProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml"));

        Assert.Contains("Text=\"{Binding NextEpisodeCountdownText}\"", playerView, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsNextEpisodeCountdownActive}\"", playerView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PlayNextEpisodeCommand}\"", playerView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelNextEpisodeCommand}\"", playerView, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", playerView, StringComparison.Ordinal);
        Assert.DoesNotContain("IsNextEpisodePromptVisible", playerSheet, StringComparison.Ordinal);
        Assert.Contains("Player.NextEpisode.Cancel", playerView, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding AutoPlayNext}\"", settingsView, StringComparison.Ordinal);

        foreach (var locale in new[] { "de-DE", "en-US", "es-ES", "fr-FR", "tr-TR" })
        {
            using var translations = JsonDocument.Parse(
                File.ReadAllText(
                    ProjectFile("Noctra.Core", "Localization", "Translations", $"{locale}.json")));
            var root = translations.RootElement;

            Assert.True(root.TryGetProperty("Player.NextEpisode.Cancel", out _), locale);
            Assert.True(root.TryGetProperty("Player.NextEpisode.Countdown.One", out _), locale);
            Assert.True(root.TryGetProperty("Player.NextEpisode.Countdown.Many", out _), locale);
        }
    }

    [Fact]
    public void MobileContinueWatching_RemainsExplicitlyBoundedInsteadOfJoiningTheLargeFeed()
    {
        var view = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileHomeView.axaml"));
        var viewModel = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var control = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));
        var presenter = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileCardRowPresenter.cs"));

        Assert.Contains("<controls:MobileVirtualizingCardGrid", view, StringComparison.Ordinal);
        Assert.Contains("SourceItems=\"{Binding ContinueWatching}\"", view, StringComparison.Ordinal);
        Assert.Contains("CardKind=\"ContinueWatching\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding ContinueWatching}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel", view, StringComparison.Ordinal);
        Assert.Contains(".Take(10)", viewModel, StringComparison.Ordinal);
        Assert.Contains("MobileCardGridKind.ContinueWatching", control, StringComparison.Ordinal);
        Assert.Contains("MobileCardGridKind.ContinueWatching", presenter, StringComparison.Ordinal);
        Assert.Contains("MobileContinueWatchingCard", presenter, StringComparison.Ordinal);
        Assert.Contains("0.56", presenter, StringComparison.Ordinal);
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
            "private async void ViewModel_OnProfileSelected(Profile profile, ProfileAccessGrant? grant)",
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
        // LoadProfileAsync artık merkezî erişim yetkisini (grant) alır — PIN kapısı
        // ViewModel'de doğrulanmadan yükleme başlatılamaz.
        Assert.Contains("mainViewModel.LoadProfileAsync(profile, grant)", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("ProfileLoadingHost.IsVisible = true", StringComparison.Ordinal) <
            method.IndexOf("mainViewModel.LoadProfileAsync(profile, grant)", StringComparison.Ordinal));
        Assert.Contains("DispatcherPriority.Background", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("DispatcherPriority.Background", StringComparison.Ordinal) <
            method.IndexOf("mainViewModel.LoadProfileAsync(profile, grant)", StringComparison.Ordinal));
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
