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
    public void AndroidPlayer_ReferencesMatchingMedia3SessionPackage()
    {
        var project = ReadProjectFile("Noctra.Android", "Noctra.Android.csproj");

        Assert.Contains(
            "<PackageReference Include=\"Xamarin.AndroidX.Media3.Session\" Version=\"1.4.1.1\" />",
            project);
    }

    [Fact]
    public void AndroidMediaNotification_HasDedicatedSmallIcon()
    {
        Assert.True(
            TryFindProjectFile(
                out var iconPath,
                "Noctra.Android", "Resources", "drawable", "ic_notification_noctra.xml"),
            "Missing dedicated Android media-notification small icon.");

        var icon = File.ReadAllText(iconPath!);
        Assert.Contains("android:width=\"24dp\"", icon);
        Assert.Contains("android:height=\"24dp\"", icon);
        Assert.Contains("android:fillColor=\"#FFFFFFFF\"", icon);
        Assert.Contains("android:scaleX=\"1.20\"", icon);
        Assert.DoesNotContain("<gradient", icon);
    }

    [Fact]
    public void AndroidPlayback_HostsPlayerInMediaSessionService()
    {
        Assert.True(
            TryFindProjectFile(
                out var servicePath,
                "Noctra.Android", "Services", "NoctraPlaybackService.cs"),
            "Missing Media3 playback service.");

        var service = File.ReadAllText(servicePath!);
        Assert.Contains("MediaSessionService", service);
        Assert.Contains("ForegroundServiceType = ForegroundService.TypeMediaPlayback", service);
        Assert.Contains("Exported = true", service);
        Assert.Contains("SessionService", service);
        Assert.Contains("new MediaSession.Builder", service);
        Assert.Contains("SetSmallIcon(Resource.Drawable.ic_notification_noctra)", service);
        Assert.Contains("override MediaSession? OnGetSession", service);
        Assert.Contains("override void OnDestroy", service);
    }

    [Fact]
    public void AndroidPlaybackService_UsesSharedApplicationPlayer()
    {
        var application = ReadProjectFile("Noctra.Android", "Application.cs");
        var registrations = ReadProjectFile(
            "Noctra.Android", "DependencyInjection", "AndroidServiceCollectionExtensions.cs");
        var service = ReadProjectFile(
            "Noctra.Android", "Services", "NoctraPlaybackService.cs");
        var player = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("public IServiceProvider Services", application);
        Assert.Contains("() => Services", application);
        Assert.Contains("AddSingleton<AndroidVideoPlayerService>()", registrations);
        Assert.Contains("GetRequiredService<AndroidVideoPlayerService>()", service);
        Assert.Contains("AttachPlaybackHost", service);
        Assert.DoesNotContain("new ExoPlayerBuilder(this)", service);
        Assert.DoesNotContain("Initialize ExoPlayer on the Main Thread", player);
        Assert.Contains("EnsureStartedAsync", player);
    }

    [Fact]
    public void AndroidPlayer_AppliesPlaybackMetadataToMediaItem()
    {
        var player = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("public void UpdateMediaMetadata", player);
        Assert.Contains("new MediaMetadata.Builder()", player);
        Assert.Contains("SetTitle(_mediaMetadata.Title)", player);
        Assert.Contains("SetArtworkUri", player);
        Assert.Contains("SetMediaMetadata", player);
    }

    [Fact]
    public void AndroidPlaybackService_RespectsBackgroundSettingWhenTaskIsRemoved()
    {
        var service = ReadProjectFile(
            "Noctra.Android", "Services", "NoctraPlaybackService.cs");

        Assert.Contains("override void OnTaskRemoved", service);
        Assert.Contains("AllowBackgroundPlayback", service);
        Assert.Contains("PauseAllPlayersAndStopSelf()", service);
        Assert.Contains("base.OnTaskRemoved(rootIntent)", service);
    }

    [Fact]
    public void AndroidNotificationPermission_HasRuntimeRequestFlow()
    {
        var manifest = ReadProjectFile("Noctra.Android", "Properties", "AndroidManifest.xml");
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");
        var permissionService = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidNotificationPermissionService.cs");

        Assert.Contains("android.permission.POST_NOTIFICATIONS", manifest, StringComparison.Ordinal);
        Assert.Contains("RequestPermissions", permissionService, StringComparison.Ordinal);
        Assert.Contains("android.permission.POST_NOTIFICATIONS", permissionService, StringComparison.Ordinal);
        Assert.Contains("OnRequestPermissionsResult", activity, StringComparison.Ordinal);
        Assert.Contains("TryHandleRequestPermissionsResult", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidLaunch_UsesOneSplashScreenThemeAndNoActivityIconOverride()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");
        var project = ReadProjectFile("Noctra.Android", "Noctra.Android.csproj");
        var styles = ReadProjectFile("Noctra.Android", "Resources", "values", "styles.xml");

        Assert.Contains("Theme = \"@style/MyTheme.Splash\"", activity);
        Assert.DoesNotContain("Icon =", activity);
        Assert.DoesNotContain("<AndroidResource Include=\"Icon.png\">", project);
        Assert.Contains("SplashScreen.InstallSplashScreen(this);", activity);
        Assert.True(
            activity.IndexOf("SplashScreen.InstallSplashScreen(this);", StringComparison.Ordinal) <
            activity.IndexOf("base.OnCreate(savedInstanceState);", StringComparison.Ordinal),
            "InstallSplashScreen must run before base.OnCreate().");

        Assert.Contains("style name=\"MyTheme.Splash\" parent=\"Theme.SplashScreen\"", styles);
        Assert.Contains("name=\"postSplashScreenTheme\">@style/MyTheme.Main", styles);
        Assert.Contains(
            "style name=\"MyTheme.Main\" parent=\"@style/Theme.AppCompat.DayNight.NoActionBar\"",
            styles);
        Assert.Contains("name=\"android:windowBackground\">@color/splash_background", styles);
        Assert.DoesNotContain("windowIsTranslucent", styles);
        Assert.False(
            TryFindProjectFile(out _, "Noctra.Android", "Resources", "values-v31", "styles.xml"));
    }

    [Fact]
    public void AndroidBranding_UsesVectorAdaptiveAndThemedIcons()
    {
        var launcher = ReadProjectFile(
            "Noctra.Android", "Resources", "mipmap-anydpi-v26", "ic_launcher.xml");
        var launcherRound = ReadProjectFile(
            "Noctra.Android", "Resources", "mipmap-anydpi-v26", "ic_launcher_round.xml");
        var themedLauncher = ReadProjectFile(
            "Noctra.Android", "Resources", "mipmap-anydpi-v33", "ic_launcher.xml");
        var themedLauncherRound = ReadProjectFile(
            "Noctra.Android", "Resources", "mipmap-anydpi-v33", "ic_launcher_round.xml");
        var foreground = ReadProjectFile(
            "Noctra.Android", "Resources", "drawable", "ic_launcher_foreground.xml");
        var monochrome = ReadProjectFile(
            "Noctra.Android", "Resources", "drawable", "ic_launcher_monochrome.xml");
        var splash = ReadProjectFile(
            "Noctra.Android", "Resources", "drawable", "ic_noctra_splash.xml");

        Assert.Contains("@drawable/ic_launcher_foreground", launcher);
        Assert.DoesNotContain("@mipmap/ic_launcher_foreground", launcher);
        Assert.Contains("@drawable/ic_launcher_foreground", launcherRound);
        Assert.DoesNotContain("@mipmap/ic_launcher_foreground", launcherRound);
        Assert.Contains("@drawable/ic_launcher_foreground", themedLauncher);
        Assert.DoesNotContain("@mipmap/ic_launcher_foreground", themedLauncher);
        Assert.Contains("<monochrome android:drawable=\"@drawable/ic_launcher_monochrome\"", themedLauncher);
        Assert.Contains("@drawable/ic_launcher_foreground", themedLauncherRound);
        Assert.DoesNotContain("@mipmap/ic_launcher_foreground", themedLauncherRound);
        Assert.Contains("<monochrome android:drawable=\"@drawable/ic_launcher_monochrome\"", themedLauncherRound);

        Assert.Contains("<vector", foreground);
        Assert.Contains("android:viewportWidth=\"108\"", foreground);
        Assert.Contains("<vector", monochrome);
        Assert.Contains("android:fillColor=\"#FFFFFFFF\"", monochrome);
        Assert.Contains("<vector", splash);

        AssertVectorSafeZone(foreground, expectedCenter: 54, sourceMaxRadius: 33, safeRadius: 33,
            expectedPathHash: "567F1825552C99988442A08E4112BEFAA7FCF1C1FA450ABCB08C4106CC93D7F4");
        AssertVectorSafeZone(monochrome, expectedCenter: 54, sourceMaxRadius: 33, safeRadius: 33,
            expectedPathHash: "567F1825552C99988442A08E4112BEFAA7FCF1C1FA450ABCB08C4106CC93D7F4");
        AssertVectorSafeZone(splash, expectedCenter: 144, sourceMaxRadius: 221, safeRadius: 96,
            expectedPathHash: "75F86A3EDDE547D3589AE4DB512595312DFD38F04E92A566D2D80423767E2A16");
    }

    [Fact]
    public void AndroidSplash_MatchesTheFirstMobileFrameAndHasNoTemplateResources()
    {
        var colors = ReadProjectFile("Noctra.Android", "Resources", "values", "colors.xml");

        Assert.Contains("<color name=\"splash_background\">#0A0A0A</color>", colors);
        Assert.False(
            TryFindProjectFile(out _, "Noctra.Android", "Resources", "drawable-v31", "avalonia_anim.xml"));
        Assert.False(
            TryFindProjectFile(out _, "Noctra.Android", "Resources", "drawable-night-v31", "avalonia_anim.xml"));

        foreach (var obsoleteResource in new[]
                 {
                     new[] { "Noctra.Android", "Resources", "drawable", "splash_logo.png" },
                     new[] { "Noctra.Android", "Resources", "drawable", "splash_screen.xml" },
                     new[] { "Noctra.Android", "Resources", "AboutResources.txt" }
                 })
        {
            Assert.False(TryFindProjectFile(out _, obsoleteResource));
        }

        foreach (var density in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
        {
            Assert.False(TryFindProjectFile(
                out _, "Noctra.Android", "Resources", $"mipmap-{density}", "ic_launcher.png"));
            Assert.False(TryFindProjectFile(
                out _, "Noctra.Android", "Resources", $"mipmap-{density}", "ic_launcher_round.png"));
            Assert.False(TryFindProjectFile(
                out _, "Noctra.Android", "Resources", $"mipmap-{density}", "ic_launcher_foreground.png"));
        }
    }

    [Fact]
    public void DesktopApplicationIconAsset_Exists()
    {
        var iconPath = FindProjectFile("Noctra.Avalonia", "Assets", "Noctra.ico");

        Assert.True(File.Exists(iconPath), $"Missing desktop application icon: {iconPath}");
    }

    [Fact]
    public void MobileContentGrids_VirtualizePrimaryAndSectionedSurfaces()
    {
        foreach (var viewName in new[]
                 {
                     "MobileLiveView.axaml",
                     "MobileMoviesView.axaml",
                     "MobileSeriesView.axaml"
                 })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);
            Assert.Contains("<controls:MobileVirtualizingCardGrid", view);
            Assert.DoesNotContain("<ScrollViewer x:Name=", view);
            Assert.DoesNotContain("<WrapPanel HorizontalAlignment=\"Stretch\"", view);
        }

        foreach (var viewName in new[]
                 {
                     "MobileFavoritesView.axaml",
                     "MobileMyListView.axaml",
                     "MobileHistoryView.axaml",
                     "MobileSearchView.axaml"
                 })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);
            Assert.Contains("<controls:MobileSectionedCardFeed", view);
            Assert.DoesNotContain("<ScrollViewer", view);
            Assert.DoesNotContain("<WrapPanel HorizontalAlignment=\"Stretch\"", view);
        }
    }

    [Fact]
    public void ProfileSetupProviderSelector_UsesEqualWidthProviderColumns()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        Assert.Contains("ColumnDefinitions=\"*,*,*\"", view);
        Assert.Equal(3, CountOccurrences(view, "Theme=\"{StaticResource SegmentedRadioButtonTransparent}\""));
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\"\r\n                          Spacing=\"12\"\r\n                          Margin=\"28,14,28,14\"", view);
    }

    [Fact]
    public void ProfileSetupTextBoxes_UseAvaloniaNativeMobileContextFlyout()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");
        var app = ReadProjectFile("Noctra.Mobile", "App.axaml");
        var buildProps = ReadProjectFile("Directory.Build.props");

        Assert.DoesNotContain("MobileTextBoxMenuBehavior", view);
        Assert.DoesNotContain("xmlns:behaviors=", view);
        Assert.False(TryFindProjectFile(
            out _, "Noctra.Mobile", "Behaviors", "MobileTextBoxMenuBehavior.cs"));
        Assert.Contains("<FluentTheme", app);
        Assert.Contains("<AvaloniaVersion>12.1.0</AvaloniaVersion>", buildProps);
    }

    [Fact]
    public void MobileTextBoxes_ExposePurposeSpecificKeyboardHints()
    {
        var app = ReadProjectFile("Noctra.Mobile", "App.axaml");
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");
        var search = ReadProjectFile("Noctra.Mobile", "Views", "MobileSearchView.axaml");
        var categories = ReadProjectFile("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml");
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        Assert.Contains("<Style Selector=\"TextBox.url\">", app);
        Assert.Contains("TextInputOptions.ContentType\" Value=\"Url\"", app);
        Assert.Contains("<Style Selector=\"TextBox.search\">", app);
        Assert.Contains("TextInputOptions.ContentType\" Value=\"Search\"", app);
        Assert.Contains("TextInputOptions.ReturnKeyType\" Value=\"Search\"", app);
        Assert.Contains("<Style Selector=\"TextBox.password\">", app);
        Assert.Contains("TextInputOptions.IsSensitive\" Value=\"True\"", app);
        Assert.Contains("<Style Selector=\"TextBox.pin\">", app);
        Assert.Contains("TextInputOptions.ContentType\" Value=\"Digits\"", app);

        Assert.Equal(4, CountOccurrences(profile, "TextInputOptions.ReturnKeyType=\"Next\""));
        Assert.Equal(2, CountOccurrences(profile, "TextInputOptions.ReturnKeyType=\"Done\""));
        Assert.Contains("Classes=\"url\"", profile);
        Assert.Contains("Classes=\"password\"", profile);
        Assert.Contains("Classes=\"pin\"", profile);
        Assert.Equal(1, CountOccurrences(profile, "TextInputOptions.ContentType=\"Password\""));

        foreach (var searchView in new[] { search, categories })
        {
            Assert.Contains("Classes=\"search\"", searchView);
        }

        Assert.Contains("Classes=\"url\"", settings);
        Assert.Equal(3, CountOccurrences(settings, "TextInputOptions.ReturnKeyType=\"Done\""));
    }

    [Fact]
    public void MobileTextBoxes_UseAccessibleTouchTargetsAndNoctraSelectionColors()
    {
        var app = ReadProjectFile("Noctra.Mobile", "App.axaml");
        var styles = ReadProjectFile("Noctra.Mobile", "Resources", "Styles.axaml");
        var darkTheme = ReadProjectFile("Noctra.Mobile", "Resources", "Themes", "DarkTheme.axaml");
        var lightTheme = ReadProjectFile("Noctra.Mobile", "Resources", "Themes", "LightTheme.axaml");

        Assert.Contains("<Style Selector=\"TextBox\">", app);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"48\"", app);
        Assert.Contains("<Setter Property=\"CaretBrush\" Value=\"{DynamicResource AccentBrush}\"", app);
        Assert.Contains("<Setter Property=\"SelectionBrush\" Value=\"{DynamicResource TextSelectionBrush}\"", app);
        Assert.Contains("<Setter Property=\"SelectionForegroundBrush\" Value=\"{DynamicResource TextPrimaryBrush}\"", app);
        Assert.DoesNotContain("<ControlTheme x:Key=\"NoctraTextBox\" TargetType=\"TextBox\">", styles);

        foreach (var theme in new[] { darkTheme, lightTheme })
        {
            Assert.Contains("<SolidColorBrush x:Key=\"TextSelectionBrush\" Color=\"#997C3AED\" />", theme);
        }

        foreach (var viewName in new[]
                 {
                     "MobileSearchView.axaml",
                     "MobileCategorySelectionView.axaml",
                     "MobileSettingsView.axaml"
                 })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);
            var textBoxes = System.Text.RegularExpressions.Regex.Matches(
                view,
                "<TextBox\\b.*?/>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            Assert.NotEmpty(textBoxes);
            Assert.All(textBoxes.Cast<System.Text.RegularExpressions.Match>(), match =>
            {
                Assert.DoesNotContain("MinHeight=\"44\"", match.Value);
                Assert.DoesNotContain("MinHeight=\"46\"", match.Value);
            });
        }
    }

    [Fact]
    public void AndroidActivity_ResizesProfileFormAboveSoftwareKeyboard()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        Assert.Contains("using Android.Views;", activity);
        Assert.Contains("WindowSoftInputMode = SoftInput.AdjustResize,", activity);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", profile);
        Assert.DoesNotContain("BringIntoViewOnFocusChange=\"False\"", profile);
        Assert.False(TryFindProjectFile(
            out _, "Noctra.Mobile", "Behaviors", "MobileKeyboardAvoidanceBehavior.cs"));
    }

    [Fact]
    public void MobileSelectionSheets_DoNotChainScrollIntoPage()
    {
        var categorySelection = ReadProjectFile(
            "Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml");
        var settingsSelection = ReadProjectFile(
            "Noctra.Mobile", "Views", "MobileSelectionSheet.axaml");

        Assert.Contains("ScrollViewer.IsScrollChainingEnabled=\"False\"", categorySelection);
        Assert.Contains("ScrollViewer.IsScrollChainingEnabled=\"False\"", settingsSelection);
    }

    [Fact]
    public void MobileSettings_UsesOneSelectionSheetInsteadOfComboBoxes()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var settingsCodeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml.cs");
        var selectionSheet = ReadProjectFile("Noctra.Mobile", "Views", "MobileSelectionSheet.axaml");

        Assert.DoesNotContain("<ComboBox", settings);
        Assert.Contains("<views:MobileSelectionSheet", settings);
        Assert.Equal(8, CountOccurrences(settings, "Click=\"OpenSelectionSheet_Click\""));
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
    public void AndroidPlayer_UsesTripledTimePrioritizedBufferTargets()
    {
        var player = ReadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs");

        Assert.Contains(
            "BufferSize.Small => (6_000, 24_000, 750, 1_500)",
            player);
        Assert.Contains(
            "BufferSize.Large => (30_000, 180_000, 1_500, 5_000)",
            player);
        Assert.Contains(
            "_ => (15_000, 90_000, 1_000, 2_500)",
            player);
        Assert.Contains(
            ".SetPrioritizeTimeOverSizeThresholds(true)",
            player);
    }

    [Fact]
    public void MobileSettings_HiddenCategoryLists_StartCollapsedAndStayHeightBounded()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        Assert.Equal(
            3,
            System.Text.RegularExpressions.Regex.Matches(
                settings,
                "<Expander\\s+Classes=\"HiddenCategoryExpander\"\\s+IsExpanded=\"False\"")
                .Count);
        Assert.Equal(
            3,
            System.Text.RegularExpressions.Regex.Matches(
                settings,
                "<ScrollViewer MaxHeight=\"260\"")
                .Count);

        Assert.Contains("ItemsSource=\"{Binding HiddenLiveGroups}\"", settings);
        Assert.Contains("ItemsSource=\"{Binding HiddenMovieGroups}\"", settings);
        Assert.Contains("ItemsSource=\"{Binding HiddenSeriesGroups}\"", settings);
        Assert.Equal(
            3,
            System.Text.RegularExpressions.Regex.Matches(settings, "UnhideGroupCommand")
                .Count);
    }

    [Fact]
    public void MobileSettings_HiddenCategoryExpanders_ResetWheneverSettingsViewModelIsAssigned()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml.cs");

        Assert.Contains("x:Name=\"HiddenLiveCategoriesExpander\"", settings);
        Assert.Contains("x:Name=\"HiddenMovieCategoriesExpander\"", settings);
        Assert.Contains("x:Name=\"HiddenSeriesCategoriesExpander\"", settings);
        Assert.Contains("ResetHiddenCategoryExpanders();", codeBehind);
        Assert.Contains("HiddenLiveCategoriesExpander.IsExpanded = false;", codeBehind);
        Assert.Contains("HiddenMovieCategoriesExpander.IsExpanded = false;", codeBehind);
        Assert.Contains("HiddenSeriesCategoriesExpander.IsExpanded = false;", codeBehind);
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

    private static void AssertVectorSafeZone(
        string vectorXml,
        double expectedCenter,
        double sourceMaxRadius,
        double safeRadius,
        string expectedPathHash)
    {
        var document = System.Xml.Linq.XDocument.Parse(vectorXml);
        var android = System.Xml.Linq.XNamespace.Get("http://schemas.android.com/apk/res/android");
        var group = document.Root?.Elements("group").Single();
        Assert.NotNull(group);

        var scaleX = double.Parse(
            group.Attribute(android + "scaleX")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var scaleY = double.Parse(
            group.Attribute(android + "scaleY")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(scaleX, scaleY, precision: 6);
        Assert.True(scaleX > 0 && scaleY > 0, "Brand vector scale must remain positive.");

        var pivotX = group.Attribute(android + "pivotX")?.Value;
        var pivotY = group.Attribute(android + "pivotY")?.Value;
        if (pivotX is not null && pivotY is not null)
        {
            Assert.Equal(expectedCenter, double.Parse(pivotX, System.Globalization.CultureInfo.InvariantCulture), precision: 6);
            Assert.Equal(expectedCenter, double.Parse(pivotY, System.Globalization.CultureInfo.InvariantCulture), precision: 6);
        }
        else
        {
            var translateX = double.Parse(
                group.Attribute(android + "translateX")!.Value,
                System.Globalization.CultureInfo.InvariantCulture);
            var translateY = double.Parse(
                group.Attribute(android + "translateY")!.Value,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.InRange(Math.Abs(translateX + (180 * scaleX) - expectedCenter), 0, 0.01);
            Assert.InRange(Math.Abs(translateY + (180 * scaleY) - expectedCenter), 0, 0.01);
        }
        Assert.True(
            sourceMaxRadius * Math.Abs(scaleX) <= safeRadius,
            $"Scaled mark radius {sourceMaxRadius * Math.Abs(scaleX):F2} exceeds safe radius {safeRadius:F2}.");

        var pathData = string.Join(
            "\n",
            document.Descendants("path")
                .Select(path => path.Attribute(android + "pathData")?.Value ?? string.Empty));
        var pathHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pathData)));
        Assert.Equal(expectedPathHash, pathHash);
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
