using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Avalonia.Android;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Platform;
using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Initialization;
using Google.Android.Gms.Ads.Interstitial;
using Noctra.Android.Services;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;
using Noctra.Services;
using Xamarin.Google.UserMesssagingPlatform;

namespace Noctra.Android.Advertising;

/// <summary>
/// Production Android advertising provider: UMP consent + Mobile Ads SDK +
/// persistent banner + playback-exit interstitials.
///
/// Consent flow (runs once, off the UI thread, gated by <see cref="StartupPrivacyCoordinator"/>):
/// wait for Noctra's own legal consent → requestConsentInfoUpdate → load/show the
/// UMP consent form when required → MobileAds.Initialize when CanRequestAds.
/// The consent/privacy part runs for every user — premium included — because
/// UMP consent info must be refreshed on every launch (Google requirement) and
/// the privacy-options entry point applies regardless of entitlement; only the
/// ad stack (SDK init, banner, interstitial) is gated by free entitlement.
/// A consent-info-update failure does NOT fail-closed: previously granted consent
/// still allows requesting ads.
///
/// Ad unit ids come from AssemblyMetadata ("Noctra.AdMob.*") so a single release
/// binary can be repointed via csproj properties. A placement without a dedicated
/// unit id is fail-closed: it is never served from another placement's id.
///
/// Test devices (AssemblyMetadata "Noctra.AdMob.TestDeviceIds") are registered
/// both in UMP's ConsentDebugSettings and the Mobile Ads RequestConfiguration,
/// so emulators/dev machines receive test creatives from the real unit ids in
/// every build configuration (Debug and Release alike).
/// </summary>
public sealed class AdMobMobileAdvertisingService : IMobileAdvertisingService
{
    private static readonly string MetadataPrefix = "Noctra.AdMob.";

    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly ILicenseService _licenseService;
    private readonly StartupPrivacyCoordinator _privacyCoordinator;
    private readonly InterstitialAdPolicyCoordinator _interstitialPolicy;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly string _bannerUnitId;
    private readonly string _interstitialUnitId;
    private readonly string _adMobAppId;
    private readonly string[] _testDeviceIds;
    private readonly int _debugGeography;

    private int _initializationState; // 0 = not started, 1 = in progress, 2 = completed
    private volatile bool _mobileAdsInitialized;
    private volatile bool _canRequestAds;
    private volatile bool _privacyOptionsRequired;

    private InterstitialAd? _interstitial;
    private bool _interstitialLoading;

    public AdMobMobileAdvertisingService(
        Context context,
        AndroidActivityProvider activityProvider,
        ILicenseService licenseService,
        StartupPrivacyCoordinator privacyCoordinator,
        InterstitialAdPolicyCoordinator interstitialPolicy)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _privacyCoordinator = privacyCoordinator ?? throw new ArgumentNullException(nameof(privacyCoordinator));
        _interstitialPolicy = interstitialPolicy ?? throw new ArgumentNullException(nameof(interstitialPolicy));

        _bannerUnitId = ReadMetadata("BannerAdUnitId");
        _interstitialUnitId = ReadMetadata("InterstitialAdUnitId");
        _adMobAppId = ReadMetadata("AppId");
        _testDeviceIds = ReadMetadata("TestDeviceIds")
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _debugGeography = ParseDebugGeography(ReadMetadata("DebugGeography"));

        _licenseService.SubscriptionChanged += OnSubscriptionChanged;
    }

    public AdvertisingOptions Options => AdvertisingOptions.ConservativeDefault;

    /// <summary>
    /// Free entitlement + at least one configured ad unit. Independent of UMP
    /// consent so the bootstrap can run the consent flow before the SDK is
    /// initialized.
    /// </summary>
    public bool IsAdsEligible => !_licenseService.IsPremium &&
        (!string.IsNullOrWhiteSpace(_bannerUnitId) ||
         (Options.PlaybackExit.Enabled && !string.IsNullOrWhiteSpace(_interstitialUnitId)));

    public bool CanRequestAds => _canRequestAds;

    public bool CanShowPrivacyOptions => _privacyOptionsRequired;

    public bool CanServeAds => IsAdsEligible && _canRequestAds && _mobileAdsInitialized;

    public event EventHandler? EligibilityChanged;

    public event EventHandler? ConsentStatusChanged;

    /// <summary>
    /// Used by the Android provider factory before resolving the AdMob service.
    /// Keeping the capability check at the boundary prevents a Huawei-only or
    /// otherwise unsupported device from touching the Google SDK at runtime.
    /// </summary>
    public static bool IsGmsAvailable(Context context)
    {
        try
        {
            var packageInfo = context.PackageManager?.GetPackageInfo(
                "com.google.android.gms",
                global::Android.Content.PM.PackageInfoFlags.MetaData |
                global::Android.Content.PM.PackageInfoFlags.Signatures);
            if (packageInfo?.ApplicationInfo?.Enabled != true)
            {
                return false;
            }

            var availability = global::Android.Gms.Common.GoogleApiAvailabilityLight
                .Instance
                .IsGooglePlayServicesAvailable(context);
            if (availability != global::Android.Gms.Common.ConnectionResult.Success)
            {
                return false;
            }

            // Huawei tablets can contain a microG/GBox compatibility package
            // with the same package name. AdMob requires Google-signed Play
            // Services; the verifier distinguishes that package from genuine
            // GMS without relying on a version-number heuristic.
            return global::Android.Gms.Common.GoogleSignatureVerifier
                .GetInstance(context)
                .IsGooglePublicSignedPackage(packageInfo);
        }
        catch
        {
            return false;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        global::Android.Util.Log.Info("NoctraAds", "consent/init start");
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var firstRun = Interlocked.CompareExchange(ref _initializationState, 1, 0) == 0;
            if (firstRun)
            {
                await RunConsentAndInitializationAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref _initializationState, 2);
            }
            else if (!_mobileAdsInitialized)
            {
                // Consent was already refreshed on a previous run; the ad stack
                // may still be missing (e.g. premium at launch, expired
                // mid-session). Bring ads up without re-running the consent flow.
                await InitializeAdsIfEligibleAsync(cancellationToken).ConfigureAwait(false);
            }

            global::Android.Util.Log.Info(
                "NoctraAds",
                $"consent/init end canRequest={_canRequestAds} privacyRequired={_privacyOptionsRequired} " +
                $"eligible={IsAdsEligible} sdkInitialized={_mobileAdsInitialized}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            global::Android.Util.Log.Warn("NoctraAds", $"consent/init failed: {ex.Message}");
            Interlocked.Exchange(ref _initializationState, 0); // allow a later retry
        }
        finally
        {
            _initLock.Release();
        }
    }

    public IDisposable? CreateBannerAd(
        Control host,
        Action<BannerAdLoadState>? stateChanged = null)
    {
        if (!CanServeAds || host is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_bannerUnitId))
        {
            return null;
        }

        var nativeHost = new BannerNativeControlHost(_bannerUnitId, stateChanged)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };

        if (host is ContentControl cc)
        {
            cc.Content = nativeHost;
        }

        return new BannerAdHandle(nativeHost);
    }

    public void PrimeInterstitial()
    {
        if (!CanServeAds || _interstitial is not null || _interstitialLoading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_interstitialUnitId))
        {
            return;
        }

        _interstitialLoading = true;
        var callback = new InterstitialLoadCallbackImpl(
            ad =>
            {
                _interstitial = ad;
                _interstitialLoading = false;
            },
            error =>
            {
                _interstitialLoading = false;
                global::Android.Util.Log.Warn("NoctraAds",
                    $"interstitial no-fill: code={error.Code} msg={error.Message}");
            });

        var request = new AdRequest.Builder().Build();
        DispatchOnMainThread(() =>
        {
            try
            {
                InterstitialAd.Load(_context, _interstitialUnitId, request, callback);
            }
            catch (Exception ex)
            {
                _interstitialLoading = false;
                global::Android.Util.Log.Warn("NoctraAds", $"interstitial load threw: {ex.Message}");
            }
        });
    }

    public Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ad = _interstitial;
        if (!CanServeAds || ad is null || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return Task.FromResult(false);
        }

        var decision = _interstitialPolicy.Evaluate(
            context,
            new AdRuntimeEligibility(
                IsPremium: _licenseService.IsPremium,
                CanRequestAds: _canRequestAds,
                AdReady: true),
            Options.PlaybackExit);
        if (!decision.ShouldShow)
        {
            global::Android.Util.Log.Info(
                "NoctraAds",
                $"interstitial denied: {decision.Reason}");
            return Task.FromResult(false);
        }

        _interstitial = null; // single-use: never present the same ad twice
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new InterstitialFullScreenCallback(
            shown: () => _interstitialPolicy.RecordImpression(DateTimeOffset.UtcNow),
            dismissed: () => completion.TrySetResult(true),
            failedToShow: () => completion.TrySetResult(false));

        ad.FullScreenContentCallback = callback;
        DispatchOnMainThread(() =>
        {
            try
            {
                ad.Show(activity);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("NoctraAds", $"interstitial show threw: {ex.Message}");
                completion.TrySetResult(false);
            }
        });

        // Preload the next one while this is on screen.
        PrimeInterstitial();
        return completion.Task;
    }

    public Task<bool> ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_privacyOptionsRequired || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            global::Android.Util.Log.Warn("NoctraAds", "privacy options skipped: no foreground activity");
            return Task.FromResult(false);
        }

        var consentInformation = UserMessagingPlatform.GetConsentInformation(_context);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            try
            {
                global::Android.Util.Log.Info("NoctraAds", "privacy options form requested");
                UserMessagingPlatform.ShowPrivacyOptionsForm(
                    activity,
                    new ConsentFormDismissedListener(error =>
                    {
                        // The user may have changed/withdrawn consent in the
                        // form; refresh cached state so ads and the Settings
                        // entry point reflect the new choice immediately.
                        RefreshConsentState(consentInformation);

                        if (error is not null)
                        {
                            global::Android.Util.Log.Warn("NoctraAds",
                                $"privacy options failed: code={error.ErrorCodeData()} msg={error.Message}");
                            completion.TrySetResult(false);
                            return;
                        }

                        global::Android.Util.Log.Info("NoctraAds", "privacy options dismissed");
                        completion.TrySetResult(true);
                    }));
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("NoctraAds", $"privacy options form threw: {ex.Message}");
                completion.TrySetResult(false);
            }
        });

        return completion.Task;
    }

    private async Task RunConsentAndInitializationAsync(CancellationToken cancellationToken)
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            throw new InvalidOperationException("Consent flow requires a foreground activity.");
        }

        // 1. Noctra's own legal consent screen first — the two modals must never
        //    stack. The timeout only covers abnormal startup paths.
        await _privacyCoordinator.WaitForLegalConsentAsync(cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var consentInformation = UserMessagingPlatform.GetConsentInformation(_context);

        // 2. Consent info update. Failure does NOT fail-closed: consent granted in
        //    a previous session remains usable.
        var updateError = await RequestConsentInfoUpdateAsync(activity, consentInformation, cancellationToken)
            .ConfigureAwait(false);
        if (updateError is not null)
        {
            global::Android.Util.Log.Warn("NoctraAds",
                $"consent info update failed: code={updateError.ErrorCodeData()} msg={updateError.Message}");
        }

        RefreshConsentState(consentInformation);

        // 3. Consent form — only when UMP requires it (EEA/UK etc.).
        if (updateError is null && !cancellationToken.IsCancellationRequested)
        {
            var formError = await LoadAndShowConsentFormIfRequiredAsync(activity, cancellationToken)
                .ConfigureAwait(false);
            if (formError is not null)
            {
                global::Android.Util.Log.Warn("NoctraAds",
                    $"consent form failed: code={formError.ErrorCodeData()} msg={formError.Message}");
            }

            RefreshConsentState(consentInformation);
        }

        // 4. Mobile Ads SDK initialization (once per process) — only for
        //    ad-serving users; the consent/privacy lifecycle above applies to
        //    everyone (premium included).
        await InitializeAdsIfEligibleAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeAdsIfEligibleAsync(CancellationToken cancellationToken)
    {
        if (!IsAdsEligible || !_canRequestAds || _mobileAdsInitialized || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await MobileAdsInitializeAsync(cancellationToken).ConfigureAwait(false);
        _mobileAdsInitialized = true;
        EligibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<FormError?> RequestConsentInfoUpdateAsync(
        Activity activity,
        IConsentInformation consentInformation,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<FormError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var builder = new ConsentRequestParameters.Builder();
        if (!string.IsNullOrWhiteSpace(_adMobAppId))
        {
            builder.SetAdMobAppId(_adMobAppId);
        }

        if (_testDeviceIds.Length > 0)
        {
            // Test devices receive TEST ads from the real unit ids and (when
            // configured) a forced debug geography, so the full consent flow is
            // exercisable on an emulator without touching production serving.
            var debugBuilder = new ConsentDebugSettings.Builder(activity);
            foreach (var deviceId in _testDeviceIds)
            {
                debugBuilder.AddTestDeviceHashedId(deviceId);
            }

            if (_debugGeography != ConsentDebugSettings.DebugGeography.DebugGeographyDisabled)
            {
                debugBuilder.SetDebugGeography(_debugGeography);
            }

            builder.SetConsentDebugSettings(debugBuilder.Build());
        }

        var parameters = builder.Build();
        DispatchOnMainThread(() =>
        {
            try
            {
                consentInformation.RequestConsentInfoUpdate(
                    activity,
                    parameters,
                    new ConsentUpdateSuccessListener(completion),
                    new ConsentUpdateFailureListener(completion));
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new FormError(FormError.ErrorCode.InternalError, ex.Message));
            }
        });

        return completion.Task;
    }

    private Task<FormError?> LoadAndShowConsentFormIfRequiredAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<FormError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        DispatchOnMainThread(() =>
        {
            try
            {
                UserMessagingPlatform.LoadAndShowConsentFormIfRequired(
                    activity,
                    new ConsentFormDismissedListener(error => completion.TrySetResult(error)));
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new FormError(FormError.ErrorCode.InternalError, ex.Message));
            }
        });

        return completion.Task;
    }

    private Task MobileAdsInitializeAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        DispatchOnMainThread(() =>
        {
            try
            {
                if (_testDeviceIds.Length > 0)
                {
                    // Test devices are set globally via RequestConfiguration; ads
                    // then serve test creatives on those devices in every build.
                    MobileAds.RequestConfiguration = new RequestConfiguration.Builder()
                        .SetTestDeviceIds(_testDeviceIds)
                        .Build();
                }

                MobileAds.Initialize(_context, new InitializationListener(
                    () => completion.TrySetResult(true)));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private void RefreshConsentState(IConsentInformation consentInformation)
    {
        var canRequest = consentInformation.CanRequestAds();
        var privacyRequired = consentInformation.PrivacyOptionsRequirementStatus == ConsentInformationPrivacyOptionsRequirementStatus.Required;
        var changed = canRequest != _canRequestAds || privacyRequired != _privacyOptionsRequired;
        _canRequestAds = canRequest;
        _privacyOptionsRequired = privacyRequired;

        if (!canRequest)
        {
            // Consent withdrawn: never serve an ad that was preloaded under the
            // previous consent state.
            _interstitial = null;
        }

        if (changed)
        {
            ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
            EligibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSubscriptionChanged()
    {
        if (_licenseService.IsPremium)
        {
            DisposeAllAds();
        }
        else if (Volatile.Read(ref _initializationState) != 1)
        {
            // Either consent never ran (state 0) or it ran while the user was
            // premium (state 2 without SDK init): bring the ad stack up in
            // both cases. A flow already in progress (state 1) will complete
            // on its own and gate ad creation on the now-free entitlement.
            _ = Task.Run(async () =>
            {
                try
                {
                    await InitializeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    global::Android.Util.Log.Warn("NoctraAds", $"post-expiry re-init failed: {ex.Message}");
                }
            });
        }

        EligibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeAllAds()
    {
        _interstitial = null;
        _interstitialLoading = false;
    }

    private string ReadMetadata(string keySuffix)
        => Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, MetadataPrefix + keySuffix, StringComparison.Ordinal))
            ?.Value ?? string.Empty;

    private static int ParseDebugGeography(string value)
    {
#pragma warning disable CS0618 // UMP binding exposes NOT_EEA only through this legacy enum member.
        return value.Trim().ToUpperInvariant() switch
        {
            "EEA" or "EU" => ConsentDebugSettings.DebugGeography.DebugGeographyEea,
            "NOT_EEA" or "NOTEEA" or "NONEEA" => ConsentDebugSettings.DebugGeography.DebugGeographyNotEea,
            "REGULATED_US" or "US" => ConsentDebugSettings.DebugGeography.DebugGeographyRegulatedUsState,
            "OTHER" => ConsentDebugSettings.DebugGeography.DebugGeographyOther,
            _ => ConsentDebugSettings.DebugGeography.DebugGeographyDisabled
        };
#pragma warning restore CS0618
    }

    private void DispatchOnMainThread(Action action)
    {
        // Do not depend on the ActivityProvider reference: it is a WeakReference
        // that can be collected or destroyed mid-consent, and Mobile Ads SDK calls
        // (Initialize/AdView/Interstitial) require the main thread. Posting to the
        // main looper is safe from any thread and needs no activity.
        var mainLooper = global::Android.OS.Looper.MainLooper;
        if (mainLooper is not null)
        {
            new global::Android.OS.Handler(mainLooper).Post(action);
        }
    }

    // --- Java callback adapters -------------------------------------------------

    private sealed class ConsentUpdateSuccessListener : Java.Lang.Object, IConsentInformationOnConsentInfoUpdateSuccessListener
    {
        private readonly TaskCompletionSource<FormError?> _completion;

        public ConsentUpdateSuccessListener(TaskCompletionSource<FormError?> completion)
            => _completion = completion;

        public void OnConsentInfoUpdateSuccess()
            => _completion.TrySetResult(null);
    }

    private sealed class ConsentUpdateFailureListener : Java.Lang.Object, IConsentInformationOnConsentInfoUpdateFailureListener
    {
        private readonly TaskCompletionSource<FormError?> _completion;

        public ConsentUpdateFailureListener(TaskCompletionSource<FormError?> completion)
            => _completion = completion;

        public void OnConsentInfoUpdateFailure(FormError? error)
            => _completion.TrySetResult(error);
    }

    private sealed class ConsentFormDismissedListener : Java.Lang.Object, IConsentFormOnConsentFormDismissedListener
    {
        private readonly Action<FormError?> _dismissed;

        public ConsentFormDismissedListener(Action<FormError?> dismissed)
            => _dismissed = dismissed;

        public void OnConsentFormDismissed(FormError? error)
            => _dismissed(error);
    }

    private sealed class InitializationListener : Java.Lang.Object, IOnInitializationCompleteListener
    {
        private readonly Action _completed;

        public InitializationListener(Action completed)
            => _completed = completed;

        public void OnInitializationComplete(IInitializationStatus initializationStatus)
            => _completed();
    }

    internal sealed class BannerAdListener : AdListener
    {
        private readonly Google.Android.Gms.Ads.AdView _adView;
        private readonly Action<BannerAdLoadState>? _stateChanged;
        private readonly Func<bool> _isInvalid;

        public BannerAdListener(
            Google.Android.Gms.Ads.AdView adView,
            Action<BannerAdLoadState>? stateChanged,
            Func<bool> isInvalid)
        {
            _adView = adView;
            _stateChanged = stateChanged;
            _isInvalid = isInvalid;
        }

        public override void OnAdLoaded()
        {
            if (_isInvalid())
            {
                return;
            }

            global::Android.Util.Log.Info("NoctraAds", "banner loaded");
            _stateChanged?.Invoke(BannerAdLoadState.Loaded);
            var mainLooper = global::Android.OS.Looper.MainLooper;
            if (mainLooper is null)
            {
                return;
            }

            new global::Android.OS.Handler(mainLooper).Post(() =>
            {
                if (_isInvalid())
                {
                    return;
                }

                var loc = new int[2];
                _adView.GetLocationOnScreen(loc);
                global::Android.Util.Log.Info("NoctraAds",
                    $"banner metrics: w={_adView.Width} h={_adView.Height} x={loc[0]} y={loc[1]} vis={_adView.Visibility} parent={_adView.Parent}");
            });
        }

        public override void OnAdFailedToLoad(LoadAdError error)
        {
            if (_isInvalid())
            {
                return;
            }

            _stateChanged?.Invoke(BannerAdLoadState.Failed);
            global::Android.Util.Log.Warn("NoctraAds",
                $"banner load failed: code={error.Code} domain={error.Domain} msg={error.Message}");
        }

        public override void OnAdImpression()
            => global::Android.Util.Log.Info("NoctraAds", "banner impression");

        public override void OnAdClicked()
            => global::Android.Util.Log.Info("NoctraAds", "banner clicked");
    }

    private sealed class InterstitialLoadCallbackImpl : InterstitialAdLoadCallback
    {
        private readonly Action<InterstitialAd> _loaded;
        private readonly Action<LoadAdError> _failed;

        public InterstitialLoadCallbackImpl(Action<InterstitialAd> loaded, Action<LoadAdError> failed)
        {
            _loaded = loaded;
            _failed = failed;
        }

        public override void OnAdLoaded(InterstitialAd ad)
            => _loaded(ad);

        public override void OnAdFailedToLoad(LoadAdError error)
            => _failed(error);
    }

    private sealed class InterstitialFullScreenCallback : FullScreenContentCallback
    {
        private readonly Action _shown;
        private readonly Action _dismissed;
        private readonly Action _failedToShow;
        private int _shownInvoked;
        private int _completed;

        public InterstitialFullScreenCallback(Action shown, Action dismissed, Action failedToShow)
        {
            _shown = shown;
            _dismissed = dismissed;
            _failedToShow = failedToShow;
        }

        public override void OnAdShowedFullScreenContent()
        {
            if (Interlocked.Exchange(ref _shownInvoked, 1) == 0)
            {
                _shown();
            }
        }

        public override void OnAdDismissedFullScreenContent()
            => Complete(_dismissed);

        public override void OnAdFailedToShowFullScreenContent(AdError error)
            => Complete(_failedToShow);

        private void Complete(Action action)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                action();
            }
        }
    }
}

/// <summary>
/// NativeControlHost subclass that embeds a Google AdView into the Avalonia
/// visual tree. Avalonia 12 hosts native views via CreateNativeControlCore,
/// which wraps the Android view in an AndroidViewControlHandle. The AdView is
/// destroyed when the host control is destroyed (or when the handle is
/// disposed while detached).
/// </summary>
internal sealed class BannerNativeControlHost : NativeControlHost
{
    private static readonly global::Android.OS.Handler MainHandler =
        new(global::Android.OS.Looper.MainLooper!);
    private readonly string _adUnitId;
    private readonly Action<BannerAdLoadState>? _stateChanged;
    private FrameLayout? _container;
    private Google.Android.Gms.Ads.AdView? _adView;
    private int _disposed;
    private int _nativeGeneration;
    private int _lifecycleSubscribed;

    public BannerNativeControlHost(
        string adUnitId,
        Action<BannerAdLoadState>? stateChanged)
    {
        _adUnitId = adUnitId;
        _stateChanged = stateChanged;
    }

    // Avalonia Android 12.1's interop peer throws NotImplementedException when
    // an accessibility service asks a NativeControlHost for child peers. The
    // embedded AdView owns its native accessibility tree, so expose this host
    // as a non-interactive leaf and let Android handle the native view itself.
    protected override AutomationPeer OnCreateAutomationPeer()
        => new NoneAutomationPeer(this);

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;

        if (Volatile.Read(ref _disposed) != 0)
        {
            return new AndroidViewControlHandle(new FrameLayout(context));
        }

        DestroyCurrentAd();
        var nativeGeneration = Interlocked.Increment(ref _nativeGeneration);
        var container = new FrameLayout(context);

        SubscribeToLifecycle();
        CreateAndLoadAd(context, container, nativeGeneration);
        return new AndroidViewControlHandle(container);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DestroyCurrentAd();
        base.DestroyNativeControlCore(control);
    }

    public void DestroyAd()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DestroyCurrentAd();
        }
    }

    private void DestroyCurrentAd()
    {
        UnsubscribeFromLifecycle();
        Interlocked.Increment(ref _nativeGeneration);
        DestroyAdView();

        _container?.RemoveAllViews();
        _container = null;
    }

    private void CreateAndLoadAd(
        Context context,
        FrameLayout container,
        int generation)
    {
        var adView = new Google.Android.Gms.Ads.AdView(context)
        {
            AdSize = Google.Android.Gms.Ads.AdSize.Banner,
            AdUnitId = _adUnitId
        };
        container.AddView(adView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Center));

        _container = container;
        _adView = adView;
        adView.AdListener = new AdMobMobileAdvertisingService.BannerAdListener(
            adView,
            _stateChanged,
            () => Volatile.Read(ref _disposed) != 0 ||
                  Volatile.Read(ref _nativeGeneration) != generation);
        _stateChanged?.Invoke(BannerAdLoadState.Loading);

        // The view is now in Avalonia's native hierarchy, so start the request
        // only after the host/container relationship exists.
        adView.LoadAd(new AdRequest.Builder().Build());
    }

    private void SubscribeToLifecycle()
    {
        if (Interlocked.Exchange(ref _lifecycleSubscribed, 1) != 0)
        {
            return;
        }

        MobileAppLifecycle.Paused += OnAppPaused;
        MobileAppLifecycle.Resumed += OnAppResumed;
    }

    private void UnsubscribeFromLifecycle()
    {
        if (Interlocked.Exchange(ref _lifecycleSubscribed, 0) == 0)
        {
            return;
        }

        MobileAppLifecycle.Paused -= OnAppPaused;
        MobileAppLifecycle.Resumed -= OnAppResumed;
    }

    private void OnAppPaused(object? sender, EventArgs e)
    {
        MainHandler.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                _adView?.Pause();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(
                    "NoctraAds", $"AdMob banner pause failed: {ex.Message}");
            }
        });
    }

    private void OnAppResumed(object? sender, EventArgs e)
    {
        MainHandler.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var adView = _adView;
            try
            {
                adView?.Resume();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(
                    "NoctraAds", $"AdMob banner resume failed: {ex.Message}");
            }

            // A suspended renderer can come back detached from its container;
            // such a view cannot render or route clicks. A dead creative keeps
            // reporting Loaded, so nothing else heals it — rebuild here.
            if (adView is null || adView.Parent is null)
            {
                RecreateAdInContainer();
            }
        });
    }

    private void RecreateAdInContainer()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _container is not { } container)
        {
            return;
        }

        Interlocked.Increment(ref _nativeGeneration);
        DestroyAdView();
        container.RemoveAllViews();
        var nextGeneration = Volatile.Read(ref _nativeGeneration);
        CreateAndLoadAd(
            container.Context ?? global::Android.App.Application.Context,
            container,
            nextGeneration);
    }

    private void DestroyAdView()
    {
        var adView = _adView;
        _adView = null;
        if (adView is not null)
        {
            _container?.RemoveView(adView);
            adView.Destroy();
        }
    }
}

internal sealed class BannerAdHandle : IDisposable
{
    private readonly BannerNativeControlHost _nativeHost;
    private int _disposed;

    public BannerAdHandle(BannerNativeControlHost nativeHost)
    {
        _nativeHost = nativeHost;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _nativeHost.DestroyAd();
    }
}
