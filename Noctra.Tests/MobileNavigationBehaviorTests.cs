using System.Text.RegularExpressions;

namespace Noctra.Tests;

/// <summary>
/// Behavioral tests for the mobile landscape collapsible navigation rail,
/// scroll edge feedback controller, and navigation race condition fixes.
/// Uses source-code verification to ensure the intended behavior is preserved.
/// </summary>
public sealed class MobileNavigationBehaviorTests
{
    // ──────────────────────────────────────────────────────────────
    //  1. NavigateToDestination version-based serialization
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NavigateToDestination_IsTaskBasedNotAsyncVoid()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must be a Task-returning method, not async void
        Assert.Contains("internal void NavigateToDestination(string destination)", mainView);
        Assert.Contains("private async Task NavigateToDestinationAsync(string destination)", mainView);
        Assert.Contains("_ = NavigateToDestinationAsync(destination)", mainView);
    }

    [Fact]
    public void NavigateToDestination_HasVersionCounterForSerialization()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("private long _navigationVersion", mainView);
        Assert.Contains("Interlocked.Increment(ref _navigationVersion)", mainView);
        Assert.Contains("Volatile.Read(ref _navigationVersion)", mainView);
    }

    [Fact]
    public void NavigateToDestination_ChecksVersionAfterEachAwait()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must check version after releasing settings and after awaiting
        var versionChecks = Regex.Matches(mainView,
            @"if \(version != Volatile\.Read\(ref _navigationVersion\)\)").Count;
        Assert.True(versionChecks >= 2,
            $"Expected at least 2 version checks after awaits, found {versionChecks}");
    }

    [Fact]
    public void NavigateToDestination_SettingsAwaitsQueuedRelease()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("_pendingSettingsRelease", mainView);
        Assert.Contains("var settingsRelease = QueueSettingsRelease();", mainView);
        Assert.Contains("await settingsRelease;", mainView);
    }

    [Fact]
    public void SettingsRelease_SnapshotsLeaseBeforeQueueingAsyncWork()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        var queueMethod = ExtractMethod(mainView, "QueueSettingsRelease");
        var exchangePos = queueMethod.IndexOf("Interlocked.Exchange", StringComparison.Ordinal);
        var chainPos = queueMethod.IndexOf("ReleaseSettingsLeaseAfterAsync", StringComparison.Ordinal);

        Assert.True(exchangePos >= 0, "QueueSettingsRelease must use Interlocked.Exchange");
        Assert.True(chainPos >= 0, "QueueSettingsRelease must append the captured lease to the release chain");
        Assert.True(exchangePos < chainPos,
            "The Settings lease must be captured before asynchronous release work is queued");
    }

    // ──────────────────────────────────────────────────────────────
    //  2. Settings → Home → Settings — last navigation wins
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NavigateToDestination_SerializesSettingsVsNonSettings()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("var settingsRelease = QueueSettingsRelease();", mainView);
        Assert.Contains("await settingsRelease;", mainView);
        Assert.Contains("_pendingSettingsRelease = ReleaseSettingsLeaseAfterAsync(", mainView);
        Assert.Contains("await previousRelease.ConfigureAwait(false);", mainView);
        Assert.Contains("resolver.CreateSettingsViewModelScope()", mainView);
        Assert.DoesNotContain("_ = ReleaseSettingsViewModelAsync();", mainView);
    }

    // ──────────────────────────────────────────────────────────────
    //  3. Status message reschedule handles all panel areas
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RescheduleExpiredStatusMessages_HandlesAllStatusAreas()
    {
        var settingsVm = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        // Must iterate all enum values, not just Channel and EPG
        Assert.Contains("Enum.GetValues<SettingsStatusArea>()", settingsVm);
        Assert.Contains("GetPanelStatus(area)", settingsVm);
    }

    [Fact]
    public void GetPanelStatus_CoversAllEnumValues()
    {
        var settingsVm = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        // GetPanelStatus must handle all 9 enum values
        var areas = new[] { "Appearance", "Playback", "Audio", "Download", "Channel", "Epg", "Privacy", "Cache", "Reset" };
        foreach (var area in areas)
        {
            Assert.Contains($"SettingsStatusArea.{area} => {area}StatusMessage", settingsVm);
        }
    }

    [Fact]
    public void RescheduleExpiredStatusMessages_ClearsOldTokensBeforeRescheduling()
    {
        var settingsVm = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        // Must cancel all existing tokens before rescheduling
        Assert.Contains("CancelAllStatusAutoClears();", settingsVm);
        Assert.Contains("_statusAutoClearTokens.Clear()", settingsVm);
    }

    // ──────────────────────────────────────────────────────────────
    //  4. Scroll edge feedback — Hide() resets gesture state
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ScrollEdgeFeedback_Hide_ResetsGestureState()
    {
        var controller = ReadProjectFile("Noctra.Mobile", "Behaviors", "MobileScrollEdgeFeedbackController.cs");

        // Hide() must reset _isScrollGestureActive and _lastFeedbackUtc
        var hideMethod = ExtractMethod(controller, "Hide");
        Assert.Contains("_isScrollGestureActive = false", hideMethod);
        Assert.Contains("_lastFeedbackUtc = DateTime.MinValue", hideMethod);
    }

    [Fact]
    public void ScrollEdgeFeedback_TracksScrollGestureLifecycle()
    {
        var controller = ReadProjectFile("Noctra.Mobile", "Behaviors", "MobileScrollEdgeFeedbackController.cs");

        // Must subscribe to ScrollGestureEndedEvent
        Assert.Contains("ScrollGestureEndedEvent", controller);
        Assert.Contains("_isScrollGestureActive = true", controller);
        Assert.Contains("_isScrollGestureActive = false", controller);
    }

    [Fact]
    public void ScrollEdgeFeedback_FadeSkipsDuringActiveGesture()
    {
        var controller = ReadProjectFile("Noctra.Mobile", "Behaviors", "MobileScrollEdgeFeedbackController.cs");

        // OnFadeTimerTick must check _isScrollGestureActive before fading
        var fadeMethod = ExtractMethod(controller, "OnFadeTimerTick");
        Assert.Contains("if (_isScrollGestureActive)", fadeMethod);
    }

    // ──────────────────────────────────────────────────────────────
    //  5. Navigation rail — compact mode uses CSS class
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NavigationRail_CompactMode_UsesCssClass()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        // Must use Classes.Set instead of hardcoded Width
        Assert.Contains("Classes.Set(\"compact\", !IsExpanded)", rail);
        Assert.DoesNotContain("_navigationRail.Width =", rail);
    }

    [Fact]
    public void NavigationRail_ExpandedMode_UsesDynamicResource()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        // Expanded mode must use DynamicResource NavRailWidth
        Assert.Contains("Border#NavigationRail:not(.compact)", mainView);
        Assert.Contains(
            "<Setter Property=\"Width\" Value=\"{DynamicResource NavRailWidth}\"",
            mainView);
    }

    [Fact]
    public void NavigationRail_CompactMode_SetsCorrectWidth()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        // Compact mode must set Width=56
        Assert.Contains("Border#NavigationRail.compact", mainView);
        Assert.Contains("<Setter Property=\"Width\" Value=\"56\"", mainView);
    }

    [Fact]
    public void NavigationRail_ToggleClick_TogglesCompactClass()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        // OnToggleClick must toggle IsExpanded and call ApplyCurrentState
        Assert.Contains("IsExpanded = !IsExpanded", rail);
        Assert.Contains("ApplyCurrentState()", rail);
    }

    [Fact]
    public void NavigationRail_ApplyCurrentState_TogglesCompactClassOnBorder()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        // ApplyCurrentState must set compact class based on IsExpanded
        Assert.Contains("Classes.Set(\"compact\", !IsExpanded)", rail);
    }

    [Fact]
    public void NavigationRail_DoesNotOverrideDynamicResourceWithLocalValue()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        // NavigationRail Border must NOT have Width as a direct attribute
        // (only in style selectors)
        var railBorder = Regex.Match(mainView,
            @"<Border x:Name=""NavigationRail""[\s\S]*?>" );
        Assert.True(railBorder.Success);
        Assert.DoesNotMatch(@"(?<!Min)Width=", railBorder.Value);
    }

    [Fact]
    public void NavigationRail_HasMinWidth()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        Assert.Contains("MinWidth=\"48\"", mainView);
    }

    // ──────────────────────────────────────────────────────────────
    //  6. Portrait mode — More button hidden in bottom nav
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void BottomNav_HasVisibleMoreDestination()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        Assert.Contains("ColumnDefinitions=\"*,*,*,*,*\"", mainView);
        Assert.Contains("Tag=\"More\"", mainView);
    }

    [Fact]
    public void UpdateNavigationMode_ShowsBottomNavigationInPortrait()
    {
        var mainViewCs = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains(
            "BottomNavigation.IsVisible = !useNavigationRail && canShowNavigation;",
            mainViewCs);
    }

    [Fact]
    public void NavigationRail_HasMoreButton()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        // Landscape rail must have its own More button
        Assert.Contains("Tag=\"More\"", mainView);
    }

    // ──────────────────────────────────────────────────────────────
    //  7. Navigation rail toggle button uses MaterialIcon
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NavigationRail_ToggleButton_UsesMaterialIcon()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        Assert.Contains("new MaterialIcon", rail);
        Assert.Contains("MaterialIconKind.Menu", rail);
        Assert.Contains("MaterialIconKind.MenuOpen", rail);
    }

    [Fact]
    public void NavigationRail_ToggleButton_HasRotationTransition()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        Assert.Contains("RotateTransform", rail);
        Assert.Contains("DoubleTransition", rail);
        Assert.Contains("RotateTransform.AngleProperty", rail);
    }

    [Fact]
    public void NavigationRail_ToggleButton_Rotates180Degrees()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        Assert.Contains("rotate.Angle = IsExpanded ? 0 : 180", rail);
    }

    // ──────────────────────────────────────────────────────────────
    //  8. Desktop async scope disposal
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DesktopSettingsWindow_UsesAsyncScope()
    {
        var mainWindow = ReadProjectFile("Noctra.Avalonia", "MainWindow.axaml.cs");

        Assert.Contains("CreateAsyncScope()", mainWindow);
        Assert.Contains("await using var scope", mainWindow);
    }

    [Fact]
    public void DesktopSettingsWindow_DoesNotUseSyncScope()
    {
        var mainWindow = ReadProjectFile("Noctra.Avalonia", "MainWindow.axaml.cs");

        // Should not use synchronous CreateScope for Settings
        Assert.DoesNotContain("using var scope = services.CreateScope()", mainWindow);
    }

    // ──────────────────────────────────────────────────────────────
    //  9. ActiveImportJob dead state removed from ProfileLoadingViewModel
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ProfileLoadingViewModel_HasNoActiveImportJobProperties()
    {
        var viewModel = ReadProjectFile("Noctra.Core", "ViewModels", "ProfileLoadingViewModel.cs");

        Assert.DoesNotContain("HasActiveImportJob", viewModel);
        Assert.DoesNotContain("ActiveImportJobStage", viewModel);
        Assert.DoesNotContain("ActiveImportJobLiveCount", viewModel);
        Assert.DoesNotContain("ActiveImportJobVodCount", viewModel);
        Assert.DoesNotContain("ActiveImportJobSeriesCount", viewModel);
        Assert.DoesNotContain("ActiveImportJobFailedCategoryCount", viewModel);
    }

    [Fact]
    public void ProfileListView_DoesNotCopyImportJobStatus()
    {
        var profileList = ReadProjectFile("Noctra.Mobile", "Views", "ProfileListView.axaml.cs");

        Assert.DoesNotContain("CopyImportJobStatus", profileList);
        Assert.DoesNotContain("ActiveImportJobStage", profileList);
    }

    // ──────────────────────────────────────────────────────────────
    //  10. Tooltip localization — compact tooltip reads from source
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NavigationRail_CompactTooltip_UsesLabelText()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        // Tooltip text must be captured from the label.Text, not hardcoded
        Assert.Contains("label.Text", rail);
        Assert.Contains("ToolTipText", rail);
    }

    [Fact]
    public void NavigationRail_UpdateToggleIcon_SetsTooltipBasedOnState()
    {
        var rail = ReadProjectFile("Noctra.Mobile", "Controls", "MobileCollapsibleNavigationRail.cs");

        // UpdateToggleIcon must set tooltip based on IsExpanded state
        Assert.Contains("ToolTip.SetTip", rail);
        Assert.Contains("IsExpanded ? null : item.ToolTipText", rail);
    }

    // ──────────────────────────────────────────────────────────────
    //  11. Menu button toggle changes width in portrait
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NavigationRail_CompactStyleOverridesExpandedWidth()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        // Both styles must exist and be mutually exclusive
        Assert.Contains("Border#NavigationRail:not(.compact)", mainView);
        Assert.Contains("Border#NavigationRail.compact", mainView);
    }

    // ──────────────────────────────────────────────────────────────
    //  12. Personal-state actions do not restart visible content grids
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleFavorite_RebuildsVisibleContentOnlyForFavoritesFilter()
    {
        var mainViewModel = ReadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var toggleMethod = ExtractMethod(mainViewModel, "ToggleFavoriteAsync");
        var refreshMethod = ExtractMethod(mainViewModel, "RefreshVisibleContentAfterFavoriteChange");

        Assert.Contains("RefreshVisibleContentAfterFavoriteChange();", toggleMethod);
        Assert.DoesNotContain("ScheduleImmediateFilter();", toggleMethod);
        Assert.Contains("if (ShowOnlyFavorites)", refreshMethod);
        Assert.Contains("favorite-filter-membership", refreshMethod);
    }

    [Fact]
    public void AddToMyList_DoesNotRebuildVisibleContentGrid()
    {
        var mainViewModel = ReadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var addToMyListMethod = ExtractMethod(mainViewModel, "AddToMyList");

        Assert.DoesNotContain("ScheduleImmediateFilter", addToMyListMethod);
        Assert.Contains("RefreshPersonalListsFromDatabaseAsync", addToMyListMethod);
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────

    private static string ExtractMethod(string source, string methodName)
    {
        // Find the method and return its body (up to the next method or end)
        var pattern = @$"(private|public|internal|protected).*\b{Regex.Escape(methodName)}\b";
        var match = Regex.Match(source, pattern);
        if (!match.Success)
        {
            return string.Empty;
        }

        var start = source.LastIndexOf('\n', match.Index);
        if (start < 0) start = 0;

        // Find the opening brace
        var braceStart = source.IndexOf('{', match.Index);
        if (braceStart < 0)
        {
            return source[start..];
        }

        // Count braces to find the matching closing brace
        var depth = 0;
        var i = braceStart;
        while (i < source.Length)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            if (depth == 0) break;
            i++;
        }

        return source[start..(i + 1)];
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
