using Noctra.Mobile.Services;

namespace Noctra.Tests;

public sealed class AndroidPerformanceStabilityContractTests
{
    [Fact]
    public void MobileLifecycle_TracksForegroundState()
    {
        MobileAppLifecycle.NotifyPaused();
        var generation = MobileAppLifecycle.BeginResume();

        Assert.True(MobileAppLifecycle.TryNotifyResumed(generation));
        Assert.True(MobileAppLifecycle.IsForeground);

        MobileAppLifecycle.NotifyPaused();
        Assert.False(MobileAppLifecycle.IsForeground);
    }

    [Fact]
    public void MobileLifecycle_RejectsResumeQueuedBeforeLaterPause()
    {
        var staleGeneration = MobileAppLifecycle.BeginResume();

        MobileAppLifecycle.NotifyPaused();

        Assert.False(MobileAppLifecycle.TryNotifyResumed(staleGeneration));
        Assert.False(MobileAppLifecycle.IsForeground);
    }

    [Fact]
    public void MobileLifecycle_IsolatesThrowingHandlersSoLaterSubscribersStillRun()
    {
        var firstCalled = false;
        var secondCalled = false;

        EventHandler first = (_, _) => firstCalled = true;
        EventHandler throwing = (_, _) => throw new InvalidOperationException("boom");
        EventHandler second = (_, _) => secondCalled = true;

        MobileAppLifecycle.Paused += first;
        MobileAppLifecycle.Paused += throwing;
        MobileAppLifecycle.Paused += second;
        try
        {
            MobileAppLifecycle.NotifyPaused();
        }
        finally
        {
            MobileAppLifecycle.Paused -= first;
            MobileAppLifecycle.Paused -= throwing;
            MobileAppLifecycle.Paused -= second;
        }

        // Grids, the banner control, and the native ad hosts all listen on the
        // same event: one throwing handler must not silence the others.
        Assert.True(firstCalled);
        Assert.True(secondCalled);
        Assert.False(MobileAppLifecycle.IsForeground);
    }

    [Fact]
    public void MobileLifecycle_NotifiesPausedSubscribers()
    {
        var lifecycleType = typeof(MobileAppLifecycle);
        var paused = lifecycleType.GetEvent("Paused");
        var notifyPaused = lifecycleType.GetMethod("NotifyPaused");
        var notifications = 0;
        EventHandler handler = (_, _) => notifications++;

        Assert.NotNull(paused);
        Assert.NotNull(notifyPaused);

        paused.AddEventHandler(null, handler);
        try
        {
            notifyPaused.Invoke(null, null);
        }
        finally
        {
            paused.RemoveEventHandler(null, handler);
        }

        Assert.Equal(1, notifications);
    }

    [Fact]
    public void AndroidActivity_MarksLifecyclePaused()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        Assert.Contains("protected override void OnPause()", activity, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.NotifyPaused();", activity, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.BeginResume()", activity, StringComparison.Ordinal);
        Assert.Contains("QueueVisualTreeRecovery(resumeGeneration)", activity, StringComparison.Ordinal);
        Assert.Contains("TryNotifyResumed(resumeGeneration)", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidActivity_ConfiguresPerformanceProbeAfterBaseOnCreate()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");
        var onCreateStart = activity.IndexOf(
            "protected override void OnCreate",
            StringComparison.Ordinal);
        var onCreateEnd = activity.IndexOf(
            "protected override void OnStart",
            onCreateStart,
            StringComparison.Ordinal);
        var onCreate = activity[onCreateStart..onCreateEnd];

        var baseCreate = onCreate.IndexOf("base.OnCreate(savedInstanceState)", StringComparison.Ordinal);
        var configureProbe = onCreate.IndexOf("ConfigurePerformanceProbe()", StringComparison.Ordinal);

        Assert.True(baseCreate >= 0 && configureProbe > baseCreate);
    }

    [Fact]
    public void MobileGrid_ResumeRecoveryIsInactiveAware()
    {
        var grid = ReadProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs");
        var recoveryStart = grid.IndexOf("private async Task RecoverAfterResumeAsync", StringComparison.Ordinal);
        var repairStart = grid.IndexOf("private Task<bool> TryRepairAfterResumeAsync", StringComparison.Ordinal);
        var repairEnd = grid.IndexOf("private void OnSourceItemsChanged", StringComparison.Ordinal);

        Assert.True(recoveryStart >= 0 && repairStart > recoveryStart && repairEnd > repairStart);
        var backgroundRecovery = grid[recoveryStart..repairStart];
        var uiRepair = grid[repairStart..repairEnd];

        Assert.Contains("IsResumeRecoveryEligibleOnUiThread", grid, StringComparison.Ordinal);
        Assert.Contains("!IsEffectivelyVisible", grid, StringComparison.Ordinal);
        Assert.Contains("!MobileAppLifecycle.IsForeground", grid, StringComparison.Ordinal);
        Assert.Contains("IsResumeRecoveryGenerationCurrent", backgroundRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEffectivelyVisible", backgroundRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualRoot", backgroundRecovery, StringComparison.Ordinal);
        Assert.Contains("IsResumeRecoveryEligibleOnUiThread", uiRepair, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileGrid_ResumeRecoveryIsBoundedAndAvoidsRenderPriority()
    {
        var grid = ReadProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs");

        Assert.Contains("ResumeRecoveryAttempts = 3", grid, StringComparison.Ordinal);
        // A long background stay can leave the render surface detached for far
        // longer than the original 3 x 50 ms window; recovery must stay patient
        // (deadline-bounded) and heal on the next real layout pass.
        Assert.Contains("ResumeRecoveryDeadline = TimeSpan.FromSeconds(2)", grid, StringComparison.Ordinal);
        Assert.Contains("ArmLayoutUpdatedRetry", grid, StringComparison.Ordinal);
        Assert.Contains("DisarmLayoutUpdatedRetry", grid, StringComparison.Ordinal);
        // Arming mutates a control event: it must happen on the UI thread, not
        // on the ConfigureAwait(false) continuation.
        var timeoutIndex = grid.IndexOf(
            "arming a final layout-driven attempt", StringComparison.Ordinal);
        var postIndex = grid.IndexOf(
            "Dispatcher.UIThread.Post", timeoutIndex, StringComparison.Ordinal);
        var armCallIndex = grid.IndexOf(
            "ArmLayoutUpdatedRetry(version);", postIndex, StringComparison.Ordinal);
        Assert.True(timeoutIndex >= 0 && postIndex > timeoutIndex && armCallIndex > postIndex);
        var armMethodIndex = grid.IndexOf(
            "private void ArmLayoutUpdatedRetry", StringComparison.Ordinal);
        var uiThreadAssertIndex = grid.IndexOf(
            "Debug.Assert(Dispatcher.UIThread.CheckAccess());",
            armMethodIndex,
            StringComparison.Ordinal);
        Assert.True(armMethodIndex >= 0 && uiThreadAssertIndex > armMethodIndex);
        Assert.DoesNotContain("DispatcherPriority.Render", grid, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileGrid_DoesNotLoseResumeRecoveryWhenVisibilityIsTemporarilyUnavailable()
    {
        var grid = ReadProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs");
        var refreshStart = grid.IndexOf("public void RefreshAfterResume", StringComparison.Ordinal);
        var skipStart = grid.IndexOf("if (!IsResumeRecoveryEligibleOnUiThread())", refreshStart, StringComparison.Ordinal);
        var skipReturn = grid.IndexOf("return;", skipStart, StringComparison.Ordinal);
        var recoveryStart = grid.IndexOf("RecoverAfterResumeAsync", skipStart, StringComparison.Ordinal);

        Assert.True(refreshStart >= 0 && skipStart > refreshStart && skipReturn > skipStart);
        Assert.True(
            recoveryStart >= 0 && recoveryStart < skipReturn,
            "A temporarily inactive resume must schedule bounded recovery before returning.");
    }

    [Fact]
    public void MobileSectionedFeed_RequestsFullRebuildOnResume()
    {
        var feed = ReadProjectFile("Noctra.Mobile", "Controls", "MobileSectionedCardFeed.cs");

        Assert.Contains("MobileAppLifecycle.Resumed", feed, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Paused", feed, StringComparison.Ordinal);
        Assert.Contains("RefreshAfterResume", feed, StringComparison.Ordinal);
        Assert.Contains("QueueFullRebuild", feed, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileGrid_ResumeRecoveryDoesNotInvalidateParentChain()
    {
        var grid = ReadProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs");

        Assert.DoesNotContain("InvalidateLayoutChain", grid, StringComparison.Ordinal);
        Assert.DoesNotContain("control = control.Parent as Control", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImage_UsesBoundedSharedCoordinator()
    {
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");

        Assert.Contains("MaxDistinctImageLoads = 48", image, StringComparison.Ordinal);
        Assert.Contains(
            "SharedImageLoadCoordinator<string, SharedImageResource<Bitmap>?>",
            image,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary<string, Task<Bitmap?>> InFlightLoads", image, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImage_PropagatesCancellationThroughIoAndDecodeAdmission()
    {
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");

        Assert.Contains("DownloadGate.WaitAsync(cancellationToken)", image, StringComparison.Ordinal);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead, cancellationToken", image, StringComparison.Ordinal);
        Assert.Contains("ReadAsStreamAsync(cancellationToken)", image, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource(cancellationToken)", image, StringComparison.Ordinal);
        Assert.Contains("DecodeGate.WaitAsync(cancellationToken)", image, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(delay, cancellationToken)", image, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_ActivatesOnlyCurrentSurfaceImageLoads()
    {
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var attachStart = mainView.IndexOf(
            "protected override void OnAttachedToVisualTree",
            StringComparison.Ordinal);
        var attachEnd = mainView.IndexOf(
            "private void OnLoaded",
            attachStart,
            StringComparison.Ordinal);

        Assert.True(attachStart >= 0 && attachEnd > attachStart);
        var attachMethod = mainView[attachStart..attachEnd];

        Assert.Contains("SetDescendantLoadsActive", image, StringComparison.Ordinal);
        Assert.Contains("RemoteImage.SetDescendantLoadsActive(active.Page, false)", mainView, StringComparison.Ordinal);
        Assert.Contains("RemoteImage.SetDescendantLoadsActive(_activeCorePage?.Page, isActive)", mainView, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Paused += OnAppPaused", attachMethod, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Resumed += OnAppResumed", attachMethod, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", mainView, StringComparison.Ordinal);
        Assert.Contains("SetActivePageImageLoadsActive(false)", mainView, StringComparison.Ordinal);
        Assert.Contains("SetActivePageImageLoadsActive(true)", mainView, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImage_GuardsUiCommitAndAvoidsRenderPriority()
    {
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");

        Assert.DoesNotContain("DispatcherPriority.Render", image, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", image, StringComparison.Ordinal);
        Assert.Contains("MobileImageLoadPolicy.CanStart", image, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImage_RequiresPersistentActivityAndClearsStalePlaceholder()
    {
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var profileStart = mainView.IndexOf("private void ShowProfileSelection", StringComparison.Ordinal);
        var profileEnd = mainView.IndexOf(
            "private void OverlayProfileList_ProfileLoaded",
            profileStart,
            StringComparison.Ordinal);

        Assert.True(profileStart >= 0 && profileEnd > profileStart);
        var profileMethod = mainView[profileStart..profileEnd];

        Assert.Contains("SurfaceLoadsActiveProperty", image, StringComparison.Ordinal);
        Assert.Contains("inherits: true", image, StringComparison.Ordinal);
        Assert.Contains("MobileImageLoadPolicy.CanStart", image, StringComparison.Ordinal);
        Assert.Contains("SetSurfaceLoadsActive", image, StringComparison.Ordinal);
        Assert.Contains("ClearSourceAndReleaseLease", image, StringComparison.Ordinal);
        Assert.Contains("ClearFailedLoadIfCurrentAsync", image, StringComparison.Ordinal);
        Assert.Contains("ReleaseActiveCorePage(captureState: true)", profileMethod, StringComparison.Ordinal);
        Assert.Contains("ReleaseSeriesDetailView()", profileMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileRemoteImage_RetriesAdmissionAtMostOnce()
    {
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");

        Assert.Contains("ImageAdmissionAttempts = 2", image, StringComparison.Ordinal);
        Assert.Contains("ImageAdmissionRetryDelayMilliseconds = 100", image, StringComparison.Ordinal);
        Assert.Contains("attempt < ImageAdmissionAttempts", image, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidPlayer_StopsPositionPollingWhenLoadedMediaIsPaused()
    {
        var player = ReadProjectFile("Noctra.Android", "Services", "AndroidVideoPlayerService.cs");
        var start = player.IndexOf("private void UpdatePositionPollingForLoadedMedia", StringComparison.Ordinal);
        var end = player.IndexOf("private void QueuePositionUpdate", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var method = player[start..end];

        Assert.Contains("_hasLoadedMedia", method, StringComparison.Ordinal);
        Assert.Contains("_isPlaying", method, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePerformanceWork_ExposesRequiredLowAllocationCounters()
    {
        var grid = ReadProjectFile("Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs");
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");
        var coordinator = ReadProjectFile("Noctra.Mobile", "Services", "SharedImageLoadCoordinator.cs");
        var combined = grid + image + coordinator;

        foreach (var counterName in new[]
                 {
                     "GridResumeRequested",
                     "GridResumeSkippedInactive",
                     "GridResumeAttempted",
                     "GridResumeCompleted",
                     "GridResumeGenerationCancelled",
                     "ImageDistinctActive",
                     "ImageDistinctQueued",
                     "ImageConsumerCancelled",
                     "ImageUnderlyingCancelled",
                     "ImageOverflowRejected",
                     "ImageStaleCommitDropped"
                 })
        {
            Assert.Contains(counterName, combined, StringComparison.Ordinal);
        }

        Assert.Contains("PerformanceTrace.Mark", combined, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        return File.ReadAllText(path);
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
