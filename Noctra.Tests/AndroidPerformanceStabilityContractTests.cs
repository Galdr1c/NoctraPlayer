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
        Assert.DoesNotContain("DispatcherPriority.Render", grid, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", grid, StringComparison.Ordinal);
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
        Assert.Contains("SharedImageLoadCoordinator<string, Bitmap?>", image, StringComparison.Ordinal);
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
        Assert.Contains("SetSourceOnUiThread(null, normalizedUrl)", image, StringComparison.Ordinal);
        Assert.Contains("TrySetSource(url, null, cancellationToken)", image, StringComparison.Ordinal);
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
