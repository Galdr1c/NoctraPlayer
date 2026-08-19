using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Avalonia.Android;
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
    private readonly SemaphoreSlim _initLock = new(1, 1);
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
        StartupPrivacyCoordinator privacyCoordinator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _privacyCoordinator = privacyCoordinator ?? throw new ArgumentNullException(nameof(privacyCoordinator));

        _interstitialUnitId = ReadMetadata("InterstitialAdUnitId");
        _adMobAppId = ReadMetadata("AppId");
        _testDeviceIds = ReadMetadata("TestDeviceIds")
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _debugGeography = ParseDebugGeography(ReadMetadata("DebugGeography"));

        _licenseService.SubscriptionChanged += OnSubscriptionChanged;
    }

    public AdvertisingOptions Options => AdvertisingOptions.ConservativeDefault;

    /// <summary>
    /// Free entitlement + interstitial unit id present. Independent of UMP
    /// consent so the bootstrap can run the consent flow before the SDK is
    /// initialized.
    /// </summary>
    public bool IsAdsEligible => !_licenseService.IsPremium &&
        (Options.PlaybackExit.Enabled && !string.IsNullOrWhiteSpace(_interstitialUnitId));

    public bool CanRequestAds => _canRequestAds && IsAdsEligible;

    public bool CanShowPrivacyOptions => _privacyOptionsRequired;

    public bool CanServeAds => IsAdsEligible && _canRequestAds && _mobileAdsInitialized;

    public event EventHandler? EligibilityChanged;

    public event EventHandler? ConsentStatusChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAdsEligible)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Interlocked.CompareExchange(ref _initializationState, 1, 0) != 0)
            {
                return; // already running or completed
            }

            await RunConsentAndInitializationAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _initializationState, 2);
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

    public IDisposable? CreateBannerAd(Control host)
    {
        if (!CanServeAds || host is null)
        {
            return null;
        }

        var bannerUnitId = ReadMetadata("BannerAdUnitId");
        if (string.IsNullOrWhiteSpace(bannerUnitId))
        {
            return null;
        }

        var adView = new Google.Android.Gms.Ads.AdView(_context)
        {
            AdSize = Google.Android.Gms.Ads.AdSize.Fluid,
            AdUnitId = bannerUnitId
        };

        var request = new AdRequest.Builder().Build();
        DispatchOnMainThread(() => adView.LoadAd(request));

        var nativeHost = new BannerNativeControlHost(adView)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };

        if (host is ContentControl cc)
        {
            cc.Content = nativeHost;
        }

        return new BannerAdHandle(adView, nativeHost);
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

        _interstitial = null; // single-use: never present the same ad twice
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new InterstitialFullScreenCallback(
            () => completion.TrySetResult(true),       // shown
            () => completion.TrySetResult(true),       // dismissed
            () => completion.TrySetResult(false));     // failed to show

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
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            try
            {
                UserMessagingPlatform.ShowPrivacyOptionsForm(
                    activity,
                    new ConsentFormDismissedListener(_ => completion.TrySetResult(true)));
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("NoctraAds", $"privacy options form failed: {ex.Message}");
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
        if (!IsAdsEligible || cancellationToken.IsCancellationRequested)
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

        if (!IsAdsEligible || !_canRequestAds || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // 4. Mobile Ads SDK initialization (once per process).
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
        else if (Volatile.Read(ref _initializationState) == 0)
        {
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
        return value.Trim().ToUpperInvariant() switch
        {
            "EEA" or "EU" => ConsentDebugSettings.DebugGeography.DebugGeographyEea,
            "NOT_EEA" or "NOTEEA" or "NONEEA" => ConsentDebugSettings.DebugGeography.DebugGeographyNotEea,
            "REGULATED_US" or "US" => ConsentDebugSettings.DebugGeography.DebugGeographyRegulatedUsState,
            "OTHER" => ConsentDebugSettings.DebugGeography.DebugGeographyOther,
            _ => ConsentDebugSettings.DebugGeography.DebugGeographyDisabled
        };
    }

    private void DispatchOnMainThread(Action action)
    {
        // Do not depend on the ActivityProvider reference: it is a WeakReference
        // that can be collected or destroyed mid-consent, and Mobile Ads SDK calls
        // (Initialize/AdView/Interstitial) require the main thread. Posting to the
        // main looper is safe from any thread and needs no activity.
        new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper).Post(action);
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
        private int _completed;

        public InterstitialFullScreenCallback(Action shown, Action dismissed, Action failedToShow)
        {
            _shown = shown;
            _dismissed = dismissed;
            _failedToShow = failedToShow;
        }

        public override void OnAdShowedFullScreenContent()
            => Complete(_shown);

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
    private readonly Google.Android.Gms.Ads.AdView _adView;
    private int _destroyed;

    public BannerNativeControlHost(Google.Android.Gms.Ads.AdView adView)
    {
        _adView = adView;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        => new AndroidViewControlHandle(_adView);

    public void DestroyAd()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) == 0)
        {
            _adView.Destroy();
        }
    }
}

internal sealed class BannerAdHandle : IDisposable
{
    private readonly Google.Android.Gms.Ads.AdView _adView;
    private readonly BannerNativeControlHost _nativeHost;
    private int _disposed;

    public BannerAdHandle(Google.Android.Gms.Ads.AdView adView, BannerNativeControlHost nativeHost)
    {
        _adView = adView;
        _nativeHost = nativeHost;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _nativeHost.DestroyAd();
        _adView.Destroy();
    }
}
