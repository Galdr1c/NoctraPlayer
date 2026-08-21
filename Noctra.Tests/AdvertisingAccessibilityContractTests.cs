namespace Noctra.Tests;

public sealed class AdvertisingAccessibilityContractTests
{
    [Fact]
    public void BannerNativeHost_UsesNonInteractiveAutomationPeer()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));

        Assert.Contains("class BannerNativeControlHost : NativeControlHost", source, StringComparison.Ordinal);
        Assert.Contains("OnCreateAutomationPeer", source, StringComparison.Ordinal);
        Assert.Contains("new NoneAutomationPeer(this)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BannerHost_CreatesNativeViewFromParentAndLoadsAfterAttach()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));

        Assert.Contains("new BannerNativeControlHost(_bannerUnitId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Google.Android.Gms.Ads.AdView(_context)", source, StringComparison.Ordinal);
        Assert.Contains("AndroidViewControlHandle", source, StringComparison.Ordinal);
        Assert.Contains("new FrameLayout", source, StringComparison.Ordinal);
        Assert.Contains("GravityFlags.Center", source, StringComparison.Ordinal);

        var hostStart = source.IndexOf("internal sealed class BannerNativeControlHost", StringComparison.Ordinal);
        Assert.True(hostStart >= 0);
        var createIndex = source.IndexOf("CreateNativeControlCore", hostStart, StringComparison.Ordinal);
        var addIndex = source.IndexOf("AddView", createIndex, StringComparison.Ordinal);
        var loadIndex = source.IndexOf(".LoadAd(", createIndex, StringComparison.Ordinal);
        Assert.True(createIndex >= 0);
        Assert.True(addIndex > createIndex);
        Assert.True(loadIndex > addIndex);
    }

    [Fact]
    public void BannerHandle_DelegatesDestroyToHostOnly()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));
        var handleStart = source.IndexOf("internal sealed class BannerAdHandle", StringComparison.Ordinal);
        Assert.True(handleStart >= 0);
        var handle = source[handleStart..];

        Assert.Contains("_nativeHost.DestroyAd();", handle, StringComparison.Ordinal);
        Assert.DoesNotContain("_adView.Destroy();", handle, StringComparison.Ordinal);
    }

    [Fact]
    public void BannerControl_UsesLoadedStateAndIgnoresStaleGenerations()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Controls", "MobileBannerAdControl.cs"));

        Assert.Contains("BannerAdLoadState", source, StringComparison.Ordinal);
        Assert.Contains("BeginLoad", source, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = _adState.IsVisible", source, StringComparison.Ordinal);
        Assert.Contains("_adState.LoadState == BannerAdLoadState.Loading ? 0 : 1", source, StringComparison.Ordinal);
        Assert.Contains("BannerAdLoadState.Failed", source, StringComparison.Ordinal);
        Assert.Contains("generation", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsentRequestPermission_IsIndependentFromEntitlement()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));

        Assert.Contains("public bool CanRequestAds => _canRequestAds;", source, StringComparison.Ordinal);
        Assert.Contains("public bool CanServeAds => IsAdsEligible && _canRequestAds && _mobileAdsInitialized;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_StartsAdvertisingBootstrapAfterServicesAreReady()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Views", "MainView.axaml.cs"));

        Assert.Contains("MobileAdvertisingBootstrapper", source, StringComparison.Ordinal);
        Assert.Contains("bootstrapper.StartAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BannerHost_RecreatesNativeViewAcrossDetachWithoutRevivingOldCallbacks()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));

        Assert.Contains("DestroyNativeControlCore", source, StringComparison.Ordinal);
        Assert.Contains("_nativeGeneration", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _disposed)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_EligibilityChangeAlwaysSchedulesBannerRefresh()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Views", "MainView.axaml.cs"));
        var methodStart = source.IndexOf(
            "private void OnAdvertisingEligibilityChanged", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var method = source[methodStart..];
        var postIndex = method.IndexOf(
            "Dispatcher.UIThread.Post(LoadBannerAdIfEligible)", StringComparison.Ordinal);
        Assert.True(postIndex >= 0);
        Assert.DoesNotContain(
            "if (!_startupFlowCompleted)",
            method[..postIndex],
            StringComparison.Ordinal);
    }

    [Fact]
    public void BannerControl_AttachesWhileLoadingButRemainsNonInteractive()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Controls", "MobileBannerAdControl.cs"));

        Assert.Contains(
            "IsVisible = _adState.HasHandle && !_adState.IsSuppressed",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsHitTestVisible = _adState.IsVisible",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_adState.LoadState == BannerAdLoadState.Loading ? 0 : 1",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaSurface_OrderTracksShellAndPlayerCompositionModes()
    {
        var activity = File.ReadAllText(ProjectSource(
            "Noctra.Android", "MainActivity.cs"));
        var service = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Services", "AndroidPlayerWindowService.cs"));
        var contract = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "Interfaces", "IPlayerWindowService.cs"));

        Assert.Contains("SetPlayerOverlayActive(bool active)", contract, StringComparison.Ordinal);
        Assert.Contains("SetPlayerOverlayActive(bool active)", service, StringComparison.Ordinal);
        Assert.Contains("SetAvaloniaPlayerOverlayActive(active)", service, StringComparison.Ordinal);
        Assert.Contains("_playerOverlaySurfaceActive", activity, StringComparison.Ordinal);
        Assert.Contains(
            "surfaceView.SetZOrderOnTop(_playerOverlaySurfaceActive)",
            activity,
            StringComparison.Ordinal);
        Assert.Contains("BuildVersionCodes.R", activity, StringComparison.Ordinal);
        Assert.Contains("RecreateAvaloniaSurfaceForLegacyZOrder", activity, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "surfaceView.SetZOrderOnTop(true);",
            activity,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerLifecycle_EnablesOverlayBeforeShowingAndRestoresOnEveryExit()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Views", "MainView.axaml.cs"));

        var openStart = source.IndexOf(
            "private async Task PlaySelectedChannelAsync", StringComparison.Ordinal);
        var overlayOn = source.IndexOf(
            "SetPlayerOverlayActive(true)", openStart, StringComparison.Ordinal);
        var playerVisible = source.IndexOf(
            "PlayerHost.IsVisible = true;", openStart, StringComparison.Ordinal);
        Assert.True(overlayOn >= openStart);
        Assert.True(playerVisible > overlayOn);

        var cancelStart = source.IndexOf(
            "catch (OperationCanceledException)", playerVisible, StringComparison.Ordinal);
        var closeStart = source.IndexOf(
            "private async void PlayerViewModel_CloseRequested", cancelStart, StringComparison.Ordinal);
        var cancelBlock = source[cancelStart..closeStart];
        Assert.Contains("SetPlayerOverlayActive(false)", cancelBlock, StringComparison.Ordinal);

        var closeEnd = source.IndexOf(
            "private void PlayerViewModel_VideoPlayerServiceErrorOccurred",
            closeStart,
            StringComparison.Ordinal);
        if (closeEnd < 0)
        {
            closeEnd = source.IndexOf("private ", closeStart + 16, StringComparison.Ordinal);
        }

        var closeBlock = closeEnd > closeStart
            ? source[closeStart..closeEnd]
            : source[closeStart..];
        Assert.Contains("SetPlayerOverlayActive(false)", closeBlock, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
