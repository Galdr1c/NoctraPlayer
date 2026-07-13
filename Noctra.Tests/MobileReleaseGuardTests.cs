namespace Noctra.Tests;

public class MobileReleaseGuardTests
{
    [Fact]
    public void AndroidPlayer_UsesExoPlayerInsteadOfLegacyMediaPlayer()
    {
        var serviceSource = ReadProjectFile("Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("AndroidX.Media3.ExoPlayer", serviceSource);
        Assert.Contains("ExoPlayerBuilder", serviceSource);
        Assert.DoesNotContain("Android.Media.MediaPlayer", serviceSource);
        Assert.DoesNotContain("new MediaPlayer", serviceSource);
        Assert.DoesNotContain("UpdateStreamQualityFromPreparedPlayer", serviceSource);
    }

    [Fact]
    public void AndroidNotificationPermission_IsNotDeclaredWithoutRuntimeRequestFlow()
    {
        var manifest = ReadProjectFile("Noctra.Android", "Properties", "AndroidManifest.xml");
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        var manifestDeclaresPermission = manifest.Contains("android.permission.POST_NOTIFICATIONS", StringComparison.Ordinal);
        var activityRequestsPermission =
            activity.Contains("RequestPermissions", StringComparison.Ordinal) &&
            activity.Contains("android.permission.POST_NOTIFICATIONS", StringComparison.Ordinal);

        Assert.Equal(manifestDeclaresPermission, activityRequestsPermission);
    }

    [Fact]
    public void DesktopApplicationIconAsset_Exists()
    {
        var iconPath = FindProjectFile("Noctra.Avalonia", "Assets", "Noctra.ico");

        Assert.True(File.Exists(iconPath), $"Missing desktop application icon: {iconPath}");
    }

    [Fact]
    public void MobilePrimaryContentGrids_UsePagedWrapPanelLayout()
    {
        foreach (var viewName in new[]
                 {
                     "MobileLiveView.axaml",
                     "MobileMoviesView.axaml",
                     "MobileSeriesView.axaml",
                     "MobileFavoritesView.axaml",
                     "MobileMyListView.axaml",
                     "MobileHistoryView.axaml",
                     "MobileSearchView.axaml"
                 })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);
            Assert.DoesNotContain("<controls:VirtualizedResponsiveGrid", view);
            Assert.Contains("<ScrollViewer", view);
            Assert.Contains("<WrapPanel HorizontalAlignment=\"Stretch\"", view);
        }
    }

    [Fact]
    public void ProfileSetupProviderSelector_UsesEqualWidthProviderColumns()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        Assert.Contains("ColumnDefinitions=\"*,*,*\"", view);
        Assert.Contains("Classes=\"ProviderTypeOption\"", view);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\"\r\n                          Spacing=\"12\"\r\n                          Margin=\"28,14,28,14\"", view);
    }

    [Fact]
    public void MobileComboBoxDropdown_DoesNotChainScrollIntoPage()
    {
        var styles = ReadProjectFile("Noctra.Mobile", "Resources", "Styles.axaml");

        Assert.Contains("MaxDropDownHeight\" Value=\"320\"", styles);
        Assert.Contains("ScrollViewer.IsScrollChainingEnabled=\"False\"", styles);
    }

    [Fact]
    public void MobileSettings_UsesOneSelectionSheetInsteadOfComboBoxes()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var settingsCodeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml.cs");
        var selectionSheet = ReadProjectFile("Noctra.Mobile", "Views", "MobileSelectionSheet.axaml");

        Assert.DoesNotContain("<ComboBox", settings);
        Assert.Contains("<views:MobileSelectionSheet", settings);
        Assert.Equal(9, CountOccurrences(settings, "Click=\"OpenSelectionSheet_Click\""));
        Assert.Contains("Background=\"{DynamicResource AccentSubtleBrush}\"", selectionSheet);
        Assert.Contains("IsVisible=\"{Binding IsSelected}\"", selectionSheet);
        Assert.Equal(2, CountOccurrences(settingsCodeBehind, "SelectionSheetHost.TryClose();"));
    }

    [Fact]
    public void MobileSelectionSheet_UsesDragHandleToDismiss()
    {
        var sheet = ReadProjectFile("Noctra.Mobile", "Views", "MobileSelectionSheet.axaml");
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MobileSelectionSheet.axaml.cs");

        Assert.Contains("x:Name=\"DragHandle\"", sheet);
        Assert.Contains("PointerPressed=\"DragHandle_PointerPressed\"", sheet);
        Assert.Contains("PointerMoved=\"DragHandle_PointerMoved\"", sheet);
        Assert.Contains("PointerReleased=\"DragHandle_PointerReleased\"", sheet);
        Assert.Contains("DismissDragThresholdRatio", codeBehind);
        Assert.Contains("SetSheetDragProgress", codeBehind);
        Assert.DoesNotContain("SheetSurface.Opacity", codeBehind);
    }

    [Fact]
    public void MobileDownloads_UsesSelectionSheetForSortOrder()
    {
        var downloads = ReadProjectFile("Noctra.Mobile", "Views", "MobileDownloadsView.axaml");
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MobileDownloadsView.axaml.cs");
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.DoesNotContain("<ComboBox", downloads);
        Assert.Contains("<views:MobileSelectionSheet", downloads);
        Assert.Contains("Click=\"OpenDownloadSortSheet_Click\"", downloads);
        Assert.Contains("DownloadSortOrder.Latest", codeBehind);
        Assert.Contains("SelectionSheetHost.TryClose()", codeBehind);
        Assert.Contains("MobileDownloadsContent.IsVisible && MobileDownloadsContent.TryHandleBack()", mainView);
        Assert.Contains("destination != \"Downloads\"", mainView);
    }

    [Fact]
    public void MobileContentGroupFilters_OpenSharedFullScreenCategoryPage()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");
        var mainViewCodeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);
            Assert.DoesNotContain("x:Name=\"GroupFilterComboBox\"", view);
            Assert.DoesNotContain("<ComboBox", view);
            Assert.Contains("x:Name=\"CategorySelectionButton\"", view);
            Assert.Contains("Click=\"OpenCategorySelection_Click\"", view);
        }

        Assert.Contains("<views:MobileCategorySelectionView", mainView);
        Assert.Contains("x:Name=\"CategorySelectionOverlay\"", mainView);
        Assert.Contains("CategorySelectionRequested", mainViewCodeBehind);
        Assert.Contains("CategorySelectionOverlay.TryClose()", mainViewCodeBehind);
    }

    [Fact]
    public void MobileContentSorts_UseSelectionSheetsWithoutLegacyGroupComboBoxes()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);

            Assert.DoesNotContain("ItemsSource=\"{Binding SortOptions}\"", view);
            Assert.Contains("Click=\"OpenSortSelectionSheet_Click\"", view);
            Assert.Contains("<views:MobileSelectionSheet", view);
            Assert.DoesNotContain("<ComboBox", view);
        }

        Assert.Contains("MobileLiveContent.IsVisible && MobileLiveContent.TryHandleBack()", mainView);
        Assert.Contains("MobileMoviesContent.IsVisible && MobileMoviesContent.TryHandleBack()", mainView);
        Assert.Contains("MobileSeriesContent.IsVisible && MobileSeriesContent.TryHandleBack()", mainView);
    }

    [Fact]
    public void MobileCategorySelectionPage_SeparatesSelectionFromHideAction()
    {
        Assert.True(
            TryFindProjectFile(out var pagePath, "Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml"),
            "Missing shared mobile category selection page.");
        Assert.True(
            TryFindProjectFile(out var codeBehindPath, "Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml.cs"),
            "Missing shared mobile category selection page code-behind.");

        var page = File.ReadAllText(pagePath!);
        var codeBehind = File.ReadAllText(codeBehindPath!);

        Assert.Contains("x:Name=\"AllCategoriesButton\"", page);
        Assert.Contains("Click=\"SelectCategory_Click\"", page);
        Assert.Contains("Click=\"HideCategory_Click\"", page);
        Assert.Contains("Kind=\"EyeOffOutline\"", page);
        Assert.Contains("TextWrapping=\"Wrap\"", page);
        Assert.Contains("MaxWidth=\"720\"", page);
        Assert.Contains("HideGroupCommand.ExecuteAsync", codeBehind);
        Assert.Contains("SelectedGroup = null", codeBehind);
    }

    [Fact]
    public void MobileCategorySelectionPage_DoesNotRebuildAfterPremiumPromptCloses()
    {
        var codeBehind = ReadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobileCategorySelectionView.axaml.cs");
        var normalizedCodeBehind = codeBehind.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("await _viewModel.HideGroupCommand.ExecuteAsync(item.Name);", normalizedCodeBehind);
        Assert.DoesNotContain(
            "await _viewModel.HideGroupCommand.ExecuteAsync(item.Name);\n        RefreshCategories();",
            normalizedCodeBehind);
        Assert.Contains("Groups_CollectionChanged", normalizedCodeBehind);
        Assert.Contains("_groupsCollection.CollectionChanged += Groups_CollectionChanged", normalizedCodeBehind);
    }

    [Fact]
    public void MobileSortTriggers_ShowOnlyTheSelectedSortIcon()
    {
        foreach (var viewName in new[] { "MobileLiveView.axaml", "MobileMoviesView.axaml", "MobileSeriesView.axaml" })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);

            Assert.Contains("x:Name=\"SortSelectionIcon\"", view);
            Assert.DoesNotContain("x:Name=\"SortSelectionValue\"", view);
            Assert.DoesNotContain("Kind=\"ChevronDown\"", view);
        }

        var downloads = ReadProjectFile("Noctra.Mobile", "Views", "MobileDownloadsView.axaml");
        Assert.Contains("x:Name=\"DownloadSortSelectionIcon\"", downloads);
        Assert.DoesNotContain("x:Name=\"DownloadSortSelectionValue\"", downloads);

        var mapper = ReadProjectFile("Noctra.Mobile", "Views", "MobileContentSortSelection.cs");
        Assert.Contains("ChannelSortOrder.NewestFirst => MaterialIconKind.SortCalendarDescending", mapper);
        Assert.Contains("ChannelSortOrder.OldestFirst => MaterialIconKind.SortCalendarAscending", mapper);
        Assert.Contains("ChannelSortOrder.NameAsc => MaterialIconKind.SortAlphabeticalAscending", mapper);
        Assert.Contains("ChannelSortOrder.NameDesc => MaterialIconKind.SortAlphabeticalDescending", mapper);
        Assert.Contains("DownloadSortOrder.Latest => MaterialIconKind.SortCalendarDescending", mapper);
        Assert.Contains("DownloadSortOrder.NameAZ => MaterialIconKind.SortAlphabeticalAscending", mapper);
        Assert.Contains("DownloadSortOrder.SizeLarge => MaterialIconKind.SortNumericDescending", mapper);
    }

    [Fact]
    public void PlaylistRefresh_UsesStagingCommitInsteadOfPublicDeleteAllRefreshPath()
    {
        var playlistService = ReadProjectFile("Noctra.Core", "Services", "PlaylistService.cs");
        var playlistInterface = ReadProjectFile("Noctra.Core", "Services", "Interfaces", "IPlaylistService.cs");
        var mainViewModel = ReadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");

        Assert.DoesNotContain("DeleteAllChannelsForRefreshAsync", playlistInterface);
        Assert.DoesNotContain("DeleteAllChannelsForRefreshAsync", mainViewModel);
        Assert.DoesNotContain("public async Task DeleteAllChannelsForRefreshAsync", playlistService);
        Assert.Contains("CreateRefreshStagingPlaylistAsync", playlistInterface);
        Assert.Contains("CommitRefreshStagingPlaylistAsync", playlistInterface);
        Assert.Contains("CommitRefreshStagingPlaylistAsync", mainViewModel);
        Assert.Contains("MoveStagedChannelsToPlaylistAsync", playlistService);
    }

    [Fact]
    public void XtreamLargePayloads_AreNotBufferedAsStrings()
    {
        var xtreamService = ReadProjectFile("Noctra.Core", "Services", "XtreamCodesService.cs");

        Assert.Contains("DeserializeAsyncEnumerable", xtreamService);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", xtreamService);
        Assert.DoesNotContain("ReadAsStringAsync", xtreamService);
        Assert.DoesNotContain("Task<string> GetStringAsync", xtreamService);
    }

    private static string ReadProjectFile(params string[] relativeParts)
        => File.ReadAllText(FindProjectFile(relativeParts));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(relativeParts)}");
    }

    private static bool TryFindProjectFile(out string? path, params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            directory = directory.Parent;
        }

        path = null;
        return false;
    }
}
