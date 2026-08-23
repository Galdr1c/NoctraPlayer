namespace Noctra.Tests;

public sealed class MobileLazyPageHostContractTests
{
    private static readonly string[] EagerPageElements =
    [
        "<views:MobileHomeView",
        "<views:MobileLiveView",
        "<views:MobileMoviesView",
        "<views:MobileSeriesView",
        "<views:MobileSearchView",
        "<views:MobileFavoritesView",
        "<views:MobileMyListView",
        "<views:MobileHistoryView",
        "<views:MobileDownloadsView",
        "<views:MobileSettingsView",
        "<views:MobileSeriesDetailView"
    ];

    [Fact]
    public void MainView_UsesEmptyBaseAndDetailHostsInsteadOfEagerPages()
    {
        var xaml = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml");

        Assert.Contains("x:Name=\"ActiveCorePageHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SeriesDetailHost\"", xaml, StringComparison.Ordinal);
        foreach (var element in EagerPageElements)
        {
            Assert.DoesNotContain(element, xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Factory_ExplicitlyCoversEveryCoreDestinationWithFreshViews()
    {
        var factory = ReadProjectFile(
            "Noctra.Mobile", "Navigation", "MobileCorePageFactory.cs");

        var mappings = new Dictionary<string, string>
        {
            ["Home"] = "MobileHomeView",
            ["Live"] = "MobileLiveView",
            ["Movies"] = "MobileMoviesView",
            ["Series"] = "MobileSeriesView",
            ["Search"] = "MobileSearchView",
            ["Favorites"] = "MobileFavoritesView",
            ["MyList"] = "MobileMyListView",
            ["History"] = "MobileHistoryView",
            ["Downloads"] = "MobileDownloadsView",
            ["Settings"] = "MobileSettingsView"
        };

        foreach (var (destination, view) in mappings)
        {
            Assert.Contains($"\"{destination}\" => new {view}()", factory, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Activator.CreateInstance", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewLocator", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCorePage_DeclaresOneExplicitPrimaryScrollOwner()
    {
        var pages = new Dictionary<string, string>
        {
            ["MobileHomeView"] = "ContinueWatchingGrid",
            ["MobileLiveView"] = "PrimaryScrollContent",
            ["MobileMoviesView"] = "PrimaryScrollContent",
            ["MobileSeriesView"] = "PrimaryScrollContent",
            ["MobileSearchView"] = "PrimaryScrollContent",
            ["MobileFavoritesView"] = "PrimaryScrollContent",
            ["MobileMyListView"] = "PrimaryScrollContent",
            ["MobileHistoryView"] = "PrimaryScrollContent",
            ["MobileDownloadsView"] = "PrimaryScrollContent",
            ["MobileSettingsView"] = "SettingsScrollViewer"
        };

        foreach (var (view, scrollOwner) in pages)
        {
            var xaml = ReadProjectFile("Noctra.Mobile", "Views", $"{view}.axaml");
            var code = ReadProjectFile("Noctra.Mobile", "Views", $"{view}.axaml.cs");

            Assert.Equal(1, CountOccurrences(xaml, $"x:Name=\"{scrollOwner}\""));
            Assert.Contains("IMobileNavigationStateParticipant", code, StringComparison.Ordinal);
            Assert.Contains("TryCaptureNavigationState", code, StringComparison.Ordinal);
            Assert.Contains("TryRestoreNavigationState", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ScrollAccessor_SearchesOnlyTheExplicitOwnerAndClampsOffsets()
    {
        var code = ReadProjectFile(
            "Noctra.Mobile", "Navigation", "IMobileNavigationStateParticipant.cs");

        Assert.Contains("Control primaryScrollOwner", code, StringComparison.Ordinal);
        Assert.Contains("GetVisualDescendants", code, StringComparison.Ordinal);
        Assert.Contains("MobilePageScrollState", code, StringComparison.Ordinal);
        Assert.Contains("Clamp", code, StringComparison.Ordinal);
        Assert.Contains("bool allowClamping", code, StringComparison.Ordinal);
        Assert.Contains("!allowClamping", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TopLevel", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_OwnsAndReleasesOnlyTheCommittedActivePage()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("private ActiveCorePage? _activeCorePage", code, StringComparison.Ordinal);
        Assert.Contains("ActiveCorePageHost.Content = prepared", code, StringComparison.Ordinal);
        Assert.Contains("ActiveCorePageHost.Content = null", code, StringComparison.Ordinal);
        Assert.Contains("active.Page.DataContext = null", code, StringComparison.Ordinal);
        Assert.Contains("CaptureNavigationState", code, StringComparison.Ordinal);
        Assert.Contains("UnwireActivePageEvents", code, StringComparison.Ordinal);

        foreach (var oldName in new[]
                 {
                     "MobileHomeContent", "MobileLiveContent", "MobileMoviesContent",
                     "MobileSeriesContent", "MobileSearchContent", "MobileFavoritesContent",
                     "MobileMyListContent", "MobileHistoryContent", "MobileDownloadsContent",
                     "MobileSettingsContent"
                 })
        {
            Assert.DoesNotContain(oldName, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Restore_IsGenerationBoundedAndImagesActivateOnlyAtTerminalResult()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("PageRestoreAttempts = 3", code, StringComparison.Ordinal);
        Assert.Contains("RestoreActivePageStateAsync", code, StringComparison.Ordinal);
        Assert.Contains("_pageNavigationState.IsCurrent(generation)", code, StringComparison.Ordinal);
        Assert.Contains("allowClamping: attempt == PageRestoreAttempts - 1", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Render", code, StringComparison.Ordinal);

        var restoreStart = code.IndexOf("private async Task RestoreActivePageStateAsync", StringComparison.Ordinal);
        Assert.True(restoreStart >= 0);
        var restoreEnd = code.IndexOf("private ", restoreStart + 20, StringComparison.Ordinal);
        var restoreMethod = restoreEnd > restoreStart ? code[restoreStart..restoreEnd] : code[restoreStart..];
        Assert.Contains("TryActivatePageImagesIfCurrent", restoreMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_RechecksPageIdentityBeforeActivationAndResumeRestartsTheRestore()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var restore = ExtractMethod(code, "RestoreActivePageStateAsync");
        var resume = ExtractMethod(code, "OnAppResumed");
        var pause = ExtractMethod(code, "OnAppPaused");
        var restart = ExtractMethod(code, "RestartActivePageRestore");

        Assert.Contains("TryActivatePageImagesIfCurrent", restore, StringComparison.Ordinal);
        Assert.DoesNotContain("() => SetActivePageImageLoadsActive(true)", restore, StringComparison.Ordinal);
        Assert.Contains("RestartActivePageRestore", resume, StringComparison.Ordinal);
        Assert.DoesNotContain("SetActivePageImageLoadsActive(true)", resume, StringComparison.Ordinal);
        Assert.Contains("CaptureActivePageNavigationState", pause, StringComparison.Ordinal);
        var deactivate = restart.IndexOf(
            "SetActivePageImageLoadsActive(false)",
            StringComparison.Ordinal);
        var beginRestore = restart.IndexOf(
            "_pageNavigationState.BeginNavigation()",
            StringComparison.Ordinal);
        Assert.True(deactivate >= 0 && beginRestore > deactivate);
    }

    [Fact]
    public void Resume_HealsMissedImageActivationAfterLongBackground()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        var resume = ExtractMethod(code, "OnAppResumed");
        Assert.Contains("ScheduleLateImageLoadActivation", resume, StringComparison.Ordinal);

        // The delayed heal must be eligibility-gated and must no-op when the
        // resume-time activation already flipped the page flag (the flag read
        // prevents restarting every image load on every resume).
        var verify = ExtractMethod(code, "VerifyActivePageImageLoadsActive");
        Assert.Contains("MobileAppLifecycle.IsForeground", verify, StringComparison.Ordinal);
        Assert.Contains("SurfaceLoadsActiveProperty", verify, StringComparison.Ordinal);
        Assert.Contains("SetActivePageImageLoadsActive(true)", verify, StringComparison.Ordinal);
    }

    [Fact]
    public void DetachReattach_UsesAnAttachmentGenerationAndCanRestartTheHost()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var attach = ExtractMethod(code, "OnAttachedToVisualTree");
        var detach = ExtractMethod(code, "OnDetachedFromVisualTree");
        var startup = ExtractMethod(code, "StartStartupFlow");
        var runStartup = ExtractMethod(code, "RunStartupFlowAsync");

        Assert.Contains("_attachmentGeneration", attach, StringComparison.Ordinal);
        Assert.Contains("_startupFlowCompleted", attach, StringComparison.Ordinal);
        Assert.Contains("RestoreShellAfterReattach", attach, StringComparison.Ordinal);
        Assert.Contains("_isAttachedToVisualTree = true", attach, StringComparison.Ordinal);
        Assert.Contains("_isAttachedToVisualTree = false", detach, StringComparison.Ordinal);
        Assert.Contains("_startupFlowStarted = false", detach, StringComparison.Ordinal);
        Assert.Contains("IsAttachmentCurrent", startup, StringComparison.Ordinal);
        Assert.Contains("long attachmentGeneration", runStartup, StringComparison.Ordinal);
        Assert.Contains("IsAttachmentCurrent(attachmentGeneration)", runStartup, StringComparison.Ordinal);
    }

    [Fact]
    public void SameDestination_IsHandledBeforeAFactoryPageIsCreated()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var navigationStart = code.IndexOf(
            "private async Task NavigateToDestinationCoreAsync",
            StringComparison.Ordinal);
        var navigationEnd = code.IndexOf("private void RollBackFailedNavigation", navigationStart, StringComparison.Ordinal);
        var navigation = code[navigationStart..navigationEnd];

        var sameDestination = navigation.IndexOf(
            "string.Equals(_activeCorePage?.Destination, destination",
            StringComparison.Ordinal);
        var factoryCreate = navigation.IndexOf(
            "MobileCorePageFactory.Create(destination)",
            StringComparison.Ordinal);

        Assert.True(sameDestination >= 0 && factoryCreate > sameDestination);
        Assert.Contains("return;", navigation[sameDestination..factoryCreate], StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileAndPlayerCovers_ReleaseTheActiveBasePage()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var profile = ExtractMethod(code, "ShowProfileSelection");
        var playback = ExtractMethod(code, "PlaySelectedChannelAsync");

        Assert.Contains("ReleaseActiveCorePage(captureState: true)", profile, StringComparison.Ordinal);
        Assert.Contains("ProfilesOverlay.IsVisible = true", profile, StringComparison.Ordinal);
        Assert.Contains("ReleaseActiveCorePage(captureState: true)", playback, StringComparison.Ordinal);
        Assert.Contains("RestoreCurrentDestinationAfterCover", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_IsDisconnectedBeforeItsLeaseIsQueuedForRelease()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var release = ExtractMethod(code, "ReleaseActiveCorePage");
        var clearContext = release.IndexOf("active.Page.DataContext = null", StringComparison.Ordinal);
        var queueRelease = release.IndexOf("QueueSettingsRelease()", StringComparison.Ordinal);

        Assert.True(clearContext >= 0 && queueRelease > clearContext);

        var navigation = ExtractMethod(code, "NavigateToDestinationCoreAsync");
        Assert.Contains(
            "if (string.Equals(destination, \"Settings\", StringComparison.Ordinal))",
            navigation,
            StringComparison.Ordinal);
        Assert.Contains("QueueSettingsRelease();", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellDetach_InvalidatesAndReleasesAllLazySurfaces()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var detach = ExtractMethod(code, "OnDetachedFromVisualTree");

        Assert.Contains("InvalidatePageRestore()", detach, StringComparison.Ordinal);
        Assert.Contains("ReleaseSeriesDetailView()", detach, StringComparison.Ordinal);
        Assert.Contains("ReleaseActiveCorePage(captureState: false)", detach, StringComparison.Ordinal);
        Assert.Contains("DetachCoreMainViewModel()", detach, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesDetail_IsCreatedAndReleasedLazily()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("CoreMainViewModel_PropertyChanged", code, StringComparison.Ordinal);
        Assert.Contains("nameof(CoreMainViewModel.IsSeriesDetailVisible)", code, StringComparison.Ordinal);
        Assert.Contains("new MobileSeriesDetailView", code, StringComparison.Ordinal);
        Assert.Contains("SeriesDetailHost.Content = detail", code, StringComparison.Ordinal);
        Assert.Contains("SeriesDetailHost.Content = null", code, StringComparison.Ordinal);
        Assert.Contains("detail.DataContext = null", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceDiagnostics_ExposeLazyHostLifecycle()
    {
        var code = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        foreach (var name in new[]
                 {
                     "CorePageActive", "CorePageCreated", "CorePageReleased",
                     "CorePageRestoreRequested", "CorePageRestoreCompleted",
                     "CorePageRestoreCancelled", "CorePageRestoreFailed",
                     "CorePageStaleCommit", "SeriesDetailCreated", "SeriesDetailReleased"
                 })
        {
            Assert.Contains(name, code, StringComparison.Ordinal);
        }

        Assert.Contains("CorePageStaleNavigationDropped", code, StringComparison.Ordinal);
        Assert.Contains("ref _corePageStaleCommit", code, StringComparison.Ordinal);
        Assert.DoesNotContain("const long CorePageStaleCommitGauge", code, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindSolutionRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var signature = new[]
            {
                $"private void {methodName}(",
                $"private async Task {methodName}(",
                $"protected override void {methodName}("
            }
            .Select(candidate => source.IndexOf(candidate, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        Assert.True(signature >= 0, $"Method {methodName} was not found.");
        var openBrace = source.IndexOf('{', signature);
        Assert.True(openBrace >= 0);

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[signature..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method {methodName} has no closing brace.");
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "NoctraPlayer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate NoctraPlayer.sln.");
    }
}
