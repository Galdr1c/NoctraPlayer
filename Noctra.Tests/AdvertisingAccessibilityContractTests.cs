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

    [Fact]
    public void HuaweiProvider_UsesHmsFallbackAndOfficialTestSlots()
    {
        var project = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Noctra.Android.csproj"));
        var manifest = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Properties", "AndroidManifest.xml"));
        var di = File.ReadAllText(ProjectSource(
            "Noctra.Android", "DependencyInjection", "AndroidServiceCollectionExtensions.cs"));
        var providerPath = ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs");

        Assert.Contains("Huawei.Hms.Ads", project, StringComparison.Ordinal);
        Assert.Contains("com.huawei.hwid", manifest, StringComparison.Ordinal);
        Assert.Contains("com.google.android.gms", manifest, StringComparison.Ordinal);
        Assert.Contains("HuaweiMobileAdvertisingService", di, StringComparison.Ordinal);
        Assert.True(File.Exists(providerPath));

        var provider = File.ReadAllText(providerPath);
        Assert.Contains("testw6vs28auh3", provider, StringComparison.Ordinal);
        Assert.Contains("testb4znbuh3n2", provider, StringComparison.Ordinal);
        Assert.Contains("teste9ih9j0rc3", provider, StringComparison.Ordinal);
        Assert.Contains("HwAds.Init", provider, StringComparison.Ordinal);
        Assert.Contains("ShowPrivacyOptionsAsync", provider, StringComparison.Ordinal);
        Assert.Contains("Build.Manufacturer", provider, StringComparison.Ordinal);
        Assert.Contains("SetView", provider, StringComparison.Ordinal);
        Assert.Contains("LinkMovementMethod", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackExitProviders_UseSharedPolicyAndCompleteOnlyAfterDismissal()
    {
        var adMob = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));
        var huawei = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        foreach (var provider in new[] { adMob, huawei })
        {
            Assert.Contains("InterstitialAdPolicyCoordinator", provider, StringComparison.Ordinal);
            Assert.Contains("_interstitialPolicy.Evaluate(", provider, StringComparison.Ordinal);
            Assert.Contains(
                "_interstitialPolicy.RecordImpression(DateTimeOffset.UtcNow)",
                provider,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "_interstitialPolicy.RecordImpression(context.Now)",
                provider,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "dismissed: () => completion.TrySetResult(true)",
            adMob,
            StringComparison.Ordinal);
        Assert.Contains(
            "closed: () => completion.TrySetResult(true)",
            huawei,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "shown: () => completion.TrySetResult(true)",
            adMob,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "opened: () => completion.TrySetResult(true)",
            huawei,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_TracksDownloadedPlaybackAndRepairsGridAfterInterstitialDismissal()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Views", "MainView.axaml.cs"));

        Assert.Contains("_adPlaybackWasDownloaded", source, StringComparison.Ordinal);
        Assert.Contains("IsDownloadedContent: _adPlaybackWasDownloaded", source, StringComparison.Ordinal);
        Assert.Contains("if (!_adPlaybackIsLive && !_adPlaybackWasDownloaded)", source, StringComparison.Ordinal);
        Assert.Contains("var shown = await ads.TryShowInterstitialAsync(adContext);", source, StringComparison.Ordinal);
        Assert.Contains("RecoverActivePageAfterInterstitial", source, StringComparison.Ordinal);
        Assert.Contains("_interstitialRecoveryPending", source, StringComparison.Ordinal);
        Assert.Contains(
            "Interlocked.Exchange(ref _interstitialRecoveryPending, 1)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("TryRecoverActivePageAfterInterstitial", source, StringComparison.Ordinal);
        Assert.Contains(
            "GetVisualDescendants().OfType<MobileVirtualizingCardGrid>()",
            source,
            StringComparison.Ordinal);
        Assert.Contains("grid.RefreshAfterResume();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InterstitialHistoryStore_UsesDurableCommitAndMainViewDetachesPlayerHandlers()
    {
        var store = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AndroidInterstitialAdHistoryStore.cs"));
        Assert.Contains(".Commit()", store, StringComparison.Ordinal);
        Assert.DoesNotContain("editor.Apply();", store, StringComparison.Ordinal);
        Assert.Contains("history could not be committed", store, StringComparison.OrdinalIgnoreCase);

        var mainView = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Views", "MainView.axaml.cs"));
        var detachStart = mainView.IndexOf(
            "protected override void OnDetachedFromVisualTree", StringComparison.Ordinal);
        var detachEnd = mainView.IndexOf(
            "private void RegisterBackHandler", detachStart, StringComparison.Ordinal);
        Assert.True(detachStart >= 0 && detachEnd > detachStart);
        var detach = mainView[detachStart..detachEnd];

        Assert.Contains("UnwirePlayerViewModelEvents();", detach, StringComparison.Ordinal);
        Assert.Contains("_interstitialRecoveryPending", detach, StringComparison.Ordinal);
        Assert.Contains("if (!_isAttachedToVisualTree)", mainView, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidProviderFactory_FailsClosedWhenNeitherPlayServicesIsPresent()
    {
        var adMob = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));
        var di = File.ReadAllText(ProjectSource(
            "Noctra.Android", "DependencyInjection", "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("IsGmsAvailable", adMob, StringComparison.Ordinal);
        Assert.Contains("HuaweiMobileAdvertisingService.IsHmsOnlyDevice(context)", di, StringComparison.Ordinal);
        Assert.Contains("AdMobMobileAdvertisingService.IsGmsAvailable(context)", di, StringComparison.Ordinal);
        Assert.Contains("new NoOpMobileAdvertisingService()", di, StringComparison.Ordinal);
        Assert.Contains("ForceProviderForDebug", di, StringComparison.Ordinal);
        Assert.Contains("Noctra.Huawei.ForceProvider", providerSourceForMetadata(), StringComparison.Ordinal);
        Assert.Contains("GoogleSignatureVerifier", adMob, StringComparison.Ordinal);
        Assert.Contains("GoogleApiAvailabilityLight", adMob, StringComparison.Ordinal);
        Assert.Contains("IsGooglePlayServicesAvailable", adMob, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiConsent_PreservesStatusProvidersAndUnknownStateRule()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("ConsentStatus Status", provider, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<AdProvider> Providers", provider, StringComparison.Ordinal);
        Assert.Contains("Status == ConsentStatus.Unknown", provider, StringComparison.Ordinal);
        Assert.Contains("providers", provider, StringComparison.Ordinal);
        Assert.Contains("Status == ConsentStatus.Unknown", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiConsent_AppliesExplicitRequestOptionsAndCanInitializeAfterSettingsChoice()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("HwAds.RequestOptions", provider, StringComparison.Ordinal);
        Assert.Contains("SetNonPersonalizedAd", provider, StringComparison.Ordinal);
        Assert.Contains("NonPersonalizedAd.AllowNonPersonalized", provider, StringComparison.Ordinal);
        Assert.Contains("EnsureHuaweiAdsInitializedIfEligibleAsync", provider, StringComparison.Ordinal);
        Assert.Contains("ShowPrivacyOptionsAsync", provider, StringComparison.Ordinal);
        Assert.Contains("RequestConsentUpdateAsync", provider, StringComparison.Ordinal);
        Assert.Contains("OnSubscriptionChanged", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiConsent_NetworkFailureRemainsRetryableAndUsesFallbackMode()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("AllowNonPersonalized", provider, StringComparison.Ordinal);
        Assert.Contains("retry", provider, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_initializationState, 0", provider, StringComparison.Ordinal);
        Assert.Contains("EnsureHuaweiAdsInitializedIfEligibleAsync", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiAds_InitializesFromApplicationLifecycleAndUsesFixedBanner()
    {
        var application = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Application.cs"));
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("InitializeSdkIfSupported", application, StringComparison.Ordinal);
        Assert.Contains("HwAds.Init", provider, StringComparison.Ordinal);
        Assert.Contains("BannerSize32050", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiAds_IncludesNetworkCompatibilityShims()
    {
        var project = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Noctra.Android.csproj"));
        var assetsUtil = File.ReadAllText(ProjectSource(
            "Noctra.Android", "HuaweiCompat", "AssetsUtil.java"));
        var networkUtil = File.ReadAllText(ProjectSource(
            "Noctra.Android", "HuaweiCompat", "NetworkUtil.java"));

        Assert.Contains("AndroidJavaSource", project, StringComparison.Ordinal);
        Assert.Contains("AssetsUtil.java", project, StringComparison.Ordinal);
        Assert.Contains("static String[] list", assetsUtil, StringComparison.Ordinal);
        Assert.Contains("isNetworkAvailable", networkUtil, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiBanner_RefreshesOnceAfterAdClosed()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("OnAdClosed", provider, StringComparison.Ordinal);
        Assert.Contains("ScheduleBannerReload", provider, StringComparison.Ordinal);
        Assert.Contains("PostDelayed", provider, StringComparison.Ordinal);
        Assert.Contains("OnBannerClosed", provider, StringComparison.Ordinal);
        Assert.Contains("_reloadScheduled", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiBanner_UsesLifecycleInsteadOfRecursivePolling()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("BannerSize32050", provider, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Paused", provider, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Resumed", provider, StringComparison.Ordinal);
        Assert.Contains("banner.Pause()", provider, StringComparison.Ordinal);
        Assert.Contains("banner.Resume()", provider, StringComparison.Ordinal);
        Assert.Contains("MainHandler", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitorBannerVisibility(", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void HuaweiBanner_LeaveDefersReplacementUntilForegroundResume()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("_reloadOnResume", provider, StringComparison.Ordinal);
        Assert.Contains("MarkBannerReloadPending", provider, StringComparison.Ordinal);
        Assert.Contains("OnBannerClosed", provider, StringComparison.Ordinal);
        Assert.Contains("OnBannerLeft", provider, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.IsForeground", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void AdMobBanner_FollowsAndroidLifecyclePauseResume()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));
        var hostStart = source.IndexOf(
            "internal sealed class BannerNativeControlHost", StringComparison.Ordinal);
        Assert.True(hostStart >= 0);
        var host = source[hostStart..];

        Assert.Contains("MobileAppLifecycle.Paused += OnAppPaused", host, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Resumed += OnAppResumed", host, StringComparison.Ordinal);
        Assert.Contains("adView?.Pause()", host, StringComparison.Ordinal);
        Assert.Contains("adView?.Resume()", host, StringComparison.Ordinal);
        // Lifecycle subscription must be released with the native view.
        Assert.Contains("UnsubscribeFromLifecycle();", host, StringComparison.Ordinal);
    }

    [Fact]
    public void AdMobBanner_RecreatesDetachedNativeViewOnResume()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));
        var hostStart = source.IndexOf(
            "internal sealed class BannerNativeControlHost", StringComparison.Ordinal);
        Assert.True(hostStart >= 0);
        var host = source[hostStart..];
        var resumeIndex = host.IndexOf(
            "private void OnAppResumed", StringComparison.Ordinal);
        Assert.True(resumeIndex >= 0);
        var recreateIndex = host.IndexOf(
            "RecreateAdInContainer()", resumeIndex, StringComparison.Ordinal);

        // A suspended AdView can come back detached; a dead creative never
        // raises OnAdFailedToLoad, so resume must heal unattached views.
        Assert.True(recreateIndex > resumeIndex);
        Assert.Contains("adView.Parent is null", host, StringComparison.Ordinal);
    }

    [Fact]
    public void BannerControl_RecreatesStaleCreativeAfterLongBackground()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Controls", "MobileBannerAdControl.cs"));

        Assert.Contains("MobileAppLifecycle.Paused", source, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.Resumed", source, StringComparison.Ordinal);
        Assert.Contains("_backgroundedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("BannerStaleAfterBackground", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(2)", source, StringComparison.Ordinal);
        // Stale path must fully rebuild: the LoadAd early-return keeps a dead
        // handle alive otherwise.
        var staleIndex = source.IndexOf(
            "banner stale after", StringComparison.OrdinalIgnoreCase);
        Assert.True(staleIndex >= 0);
        var clearIndex = source.IndexOf("ClearAd();", staleIndex, StringComparison.Ordinal);
        var loadIndex = source.IndexOf("LoadAd();", clearIndex, StringComparison.Ordinal);
        Assert.True(clearIndex > staleIndex);
        Assert.True(loadIndex > clearIndex);
    }

    [Fact]
    public void HuaweiConsent_PrivacyChoicesRequireVerifiedProviderMetadata()
    {
        var provider = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "HuaweiMobileAdvertisingService.cs"));

        Assert.Contains("RefreshConsentForPrivacyOptionsAsync", provider, StringComparison.Ordinal);
        Assert.Contains("ShowNpaOnlyPrivacyDialogAsync", provider, StringComparison.Ordinal);
        Assert.Contains("_adProviders.Count == 0", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("production remains fail-closed", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void AdMobTestDeviceDefault_IsDebugOnly()
    {
        var project = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Noctra.Android.csproj"));
        var marker = "<NoctraAdMobTestDeviceIds";
        var markerStart = project.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0);
        var markerEnd = project.IndexOf("</NoctraAdMobTestDeviceIds>", markerStart, StringComparison.Ordinal);
        Assert.True(markerEnd > markerStart);

        var declaration = project[markerStart..(markerEnd + "</NoctraAdMobTestDeviceIds>".Length)];
        Assert.Contains("'$(Configuration)' == 'Debug'", declaration, StringComparison.Ordinal);
    }

    private static string providerSourceForMetadata()
        => File.ReadAllText(ProjectSource("Noctra.Android", "Noctra.Android.csproj"));

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
