using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Avalonia.Android;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Platform;
using Huawei.Hms.Ads;
using Huawei.Hms.Ads.Banner;
using Huawei.Hms.Ads.Consent.Bean;
using Huawei.Hms.Ads.Consent.Constant;
using Huawei.Hms.Ads.Consent.Inter;
using Noctra.Android.Services;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;
using Noctra.Services;

namespace Noctra.Android.Advertising;

/// <summary>
/// Huawei Petal Ads fallback for devices that have HMS Core but no Google
/// Mobile Services. It deliberately implements the same provider contract as
/// AdMob so banner/player lifetime and entitlement policy stay shared.
/// </summary>
public sealed class HuaweiMobileAdvertisingService : IMobileAdvertisingService
{
    private static readonly string MetadataPrefix = "Noctra.Huawei.";

    // Official Huawei test slots. Debug builds inject these through the
    // AssemblyMetadata below; the constants keep the provider contract explicit
    // and make it difficult to accidentally substitute an AdMob test unit.
    private const string HuaweiTestBannerAdUnitId = "testw6vs28auh3";
    private const string HuaweiTestVideoInterstitialAdUnitId = "testb4znbuh3n2";
    private const string HuaweiTestImageInterstitialAdUnitId = "teste9ih9j0rc3";

    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly ILicenseService _licenseService;
    private readonly StartupPrivacyCoordinator _privacyCoordinator;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly string _bannerUnitId;
    private readonly string _interstitialUnitId;
    private readonly string _imageInterstitialUnitId;
    private readonly bool _forceConsent;

    private int _initializationState;
    private volatile bool _sdkInitialized;
    private volatile bool _canRequestAds;
    private volatile bool _privacyOptionsRequired;
    private Huawei.Hms.Ads.Consent.Inter.Consent? _consent;
    private Huawei.Hms.Ads.InterstitialAd? _interstitial;
    private bool _interstitialLoading;

    public HuaweiMobileAdvertisingService(
        Context context,
        AndroidActivityProvider activityProvider,
        ILicenseService licenseService,
        StartupPrivacyCoordinator privacyCoordinator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _privacyCoordinator = privacyCoordinator ?? throw new ArgumentNullException(nameof(privacyCoordinator));

        _bannerUnitId = ReadMetadata("BannerAdUnitId");
        _interstitialUnitId = ReadMetadata("InterstitialAdUnitId");
        _imageInterstitialUnitId = ReadMetadata("ImageInterstitialAdUnitId");
        _forceConsent = bool.TryParse(ReadMetadata("ForceConsent"), out var force) && force;
        _licenseService.SubscriptionChanged += OnSubscriptionChanged;
    }

    public AdvertisingOptions Options => AdvertisingOptions.ConservativeDefault;

    public bool IsAdsEligible => !_licenseService.IsPremium &&
        (!string.IsNullOrWhiteSpace(_bannerUnitId) ||
         (Options.PlaybackExit.Enabled && !string.IsNullOrWhiteSpace(_interstitialUnitId)));

    public bool CanRequestAds => _canRequestAds;

    public bool CanShowPrivacyOptions => _privacyOptionsRequired;

    public bool CanServeAds => IsAdsEligible && _canRequestAds && _sdkInitialized;

    public event EventHandler? EligibilityChanged;

    public event EventHandler? ConsentStatusChanged;

    public static bool IsHmsOnlyDevice(Context context)
    {
        var hasHmsPackage = HasPackage(context, "com.huawei.hwid") ||
            HasPackage(context, "com.huawei.hms");
        var isHuaweiFamily = string.Equals(
                Build.Manufacturer,
                "HUAWEI",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Build.Brand, "HUAWEI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Build.Brand, "HONOR", StringComparison.OrdinalIgnoreCase);

        // The manufacturer fallback covers Huawei's split/runtime HMS Core
        // packages when Android package visibility still omits their record.
        // Genuine Google Play Services wins on Huawei devices that ship it.
        return (hasHmsPackage || isHuaweiFamily) &&
            !AdMobMobileAdvertisingService.IsGmsAvailable(context);
    }

    /// <summary>
    /// Debug-only switch used to exercise the HMS path on a lab device that
    /// ships a compatibility GMS package. Release binaries always return false.
    /// </summary>
    public static bool ForceProviderForDebug
    {
        get
        {
#if DEBUG
            var value = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(
                    a.Key,
                    MetadataPrefix + "ForceProvider",
                    StringComparison.Ordinal))
                ?.Value;
            return bool.TryParse(value, out var enabled) && enabled;
#else
            return false;
#endif
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var firstRun = Interlocked.CompareExchange(ref _initializationState, 1, 0) == 0;
            if (!firstRun)
            {
                return;
            }

            await _privacyCoordinator.WaitForLegalConsentAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _consent = Huawei.Hms.Ads.Consent.Inter.Consent.GetInstance(_context);
            if (_forceConsent)
            {
                _consent.SetDebugNeedConsent(DebugNeedConsent.DebugNeedConsentField);
            }

            var consentResult = await RequestConsentUpdateAsync(_consent, cancellationToken)
                .ConfigureAwait(false);
            if (!consentResult.Succeeded)
            {
                Log.Warn("HMS consent update failed: " + consentResult.ErrorMessage);
                if (_forceConsent)
                {
                    // Debug QA must still be able to exercise the Huawei
                    // privacy dialog when the lab device is offline. This
                    // branch is never enabled by Release metadata; production
                    // remains fail-closed on a consent-service outage.
                    Log.Warn("HMS consent debug fallback: showing local choice dialog");
                    _privacyOptionsRequired = true;
                    _canRequestAds = await ShowConsentDialogAsync(
                        _consent,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _canRequestAds = false;
                    _privacyOptionsRequired = false;
                }
            }
            else if (consentResult.NeedConsent)
            {
                _privacyOptionsRequired = true;
                _canRequestAds = await ShowConsentDialogAsync(
                    _consent,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _privacyOptionsRequired = false;
                _canRequestAds = true;
            }

            if (_canRequestAds && IsAdsEligible)
            {
                await InitializeHuaweiAdsAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _initializationState, 2);
            ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
            EligibilityChanged?.Invoke(this, EventArgs.Empty);
            Log.Info($"HMS consent/init end canRequest={_canRequestAds} " +
                     $"privacyRequired={_privacyOptionsRequired} eligible={IsAdsEligible} " +
                     $"sdkInitialized={_sdkInitialized}");
        }
        catch (Exception ex) when (ex is not System.OperationCanceledException)
        {
            Interlocked.Exchange(ref _initializationState, 0);
            Log.Warn("HMS consent/init failed: " + ex.Message);
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
        if (!CanServeAds || host is null || string.IsNullOrWhiteSpace(_bannerUnitId))
        {
            return null;
        }

        var nativeHost = new HuaweiBannerNativeControlHost(_bannerUnitId, stateChanged)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        if (host is ContentControl contentControl)
        {
            contentControl.Content = nativeHost;
        }

        return new HuaweiBannerAdHandle(nativeHost);
    }

    public void PrimeInterstitial()
    {
        if (!CanServeAds || _interstitial is not null || _interstitialLoading ||
            string.IsNullOrWhiteSpace(_interstitialUnitId))
        {
            return;
        }

        _interstitialLoading = true;
        DispatchOnMainThread(() => LoadInterstitialAd(
                _interstitialUnitId,
                allowImageFallback: !string.IsNullOrWhiteSpace(_imageInterstitialUnitId) &&
                    !string.Equals(_interstitialUnitId, _imageInterstitialUnitId, StringComparison.Ordinal)));
    }

    public Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default)
    {
        var ad = _interstitial;
        var activity = _activityProvider.CurrentActivity;
        if (!CanServeAds || ad is null || activity is null || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        _interstitial = null;
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new HuaweiInterstitialListener(
            opened: () => completion.TrySetResult(true),
            failed: _ => completion.TrySetResult(false),
            closed: () => completion.TrySetResult(true));
        ad.AdListener = callback;

        DispatchOnMainThread(() =>
        {
            try
            {
                if (ad.IsLoaded)
                {
                    ad.Show(activity);
                }
                else
                {
                    completion.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("HMS interstitial show threw: " + ex.Message);
                completion.TrySetResult(false);
            }
        });
        PrimeInterstitial();
        return completion.Task;
    }

    private void LoadInterstitialAd(string adUnitId, bool allowImageFallback)
    {
        try
        {
            Huawei.Hms.Ads.InterstitialAd? ad = null;
            var listener = new HuaweiInterstitialListener(
                loaded: () =>
                {
                    if (ad is not null)
                    {
                        _interstitial = ad;
                    }

                    _interstitialLoading = false;
                    Log.Info($"HMS interstitial loaded format={adUnitId}");
                },
                failed: code =>
                {
                    if (allowImageFallback)
                    {
                        Log.Warn($"HMS video interstitial failed: code={code}; trying image test/formal slot");
                        LoadInterstitialAd(_imageInterstitialUnitId, allowImageFallback: false);
                        return;
                    }

                    _interstitialLoading = false;
                    Log.Warn($"HMS interstitial failed: code={code} format={adUnitId}");
                });
            ad = new Huawei.Hms.Ads.InterstitialAd(_context)
            {
                AdId = adUnitId,
                AdListener = listener
            };
            ad.LoadAd(new AdParam.Builder().Build());
        }
        catch (Exception ex)
        {
            if (allowImageFallback)
            {
                Log.Warn("HMS video interstitial load threw; trying image test/formal slot: " + ex.Message);
                LoadInterstitialAd(_imageInterstitialUnitId, allowImageFallback: false);
                return;
            }

            _interstitialLoading = false;
            Log.Warn("HMS interstitial load threw: " + ex.Message);
        }
    }

    public Task<bool> ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_privacyOptionsRequired || cancellationToken.IsCancellationRequested || _consent is null)
        {
            return Task.FromResult(false);
        }

        return ShowConsentDialogAsync(_consent, cancellationToken);
    }

    private async Task InitializeHuaweiAdsAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            try
            {
                HwAds.Init(_context);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        _sdkInitialized = true;
    }

    private Task<ConsentUpdateResult> RequestConsentUpdateAsync(
        Huawei.Hms.Ads.Consent.Inter.Consent consent,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ConsentUpdateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var listener = new HuaweiConsentUpdateListener(
            success: (status, needConsent) =>
                completion.TrySetResult(new ConsentUpdateResult(true, needConsent, null)),
            failed: error =>
                completion.TrySetResult(new ConsentUpdateResult(false, false, error)));

        DispatchOnMainThread(() =>
        {
            try
            {
                consent.RequestConsentUpdate(listener);
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new ConsentUpdateResult(false, false, ex.Message));
            }
        });
        return completion.Task;
    }

    private Task<bool> ShowConsentDialogAsync(
        Huawei.Hms.Ads.Consent.Inter.Consent consent,
        CancellationToken cancellationToken)
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            try
            {
                using var builder = new AlertDialog.Builder(activity);
                builder.SetTitle("Advertising privacy choices");
                builder.SetMessage(
                    "Allow Huawei Ads to use data for personalized advertising? " +
                    "You can change this choice later in Settings.");
                builder.SetPositiveButton(
                    "Allow personalized ads",
                    (_, _) =>
                    {
                        consent.SetConsentStatus(ConsentStatus.Personalized);
                        _canRequestAds = true;
                        _privacyOptionsRequired = true;
                        completion.TrySetResult(true);
                        ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
                        EligibilityChanged?.Invoke(this, EventArgs.Empty);
                    });
                builder.SetNegativeButton(
                    "Use non-personalized ads",
                    (_, _) =>
                    {
                        consent.SetConsentStatus(ConsentStatus.NonPersonalized);
                        _canRequestAds = true;
                        _privacyOptionsRequired = true;
                        completion.TrySetResult(true);
                        ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
                        EligibilityChanged?.Invoke(this, EventArgs.Empty);
                    });
                builder.SetOnCancelListener(new HuaweiDialogCancelListener(
                    () => completion.TrySetResult(false)));
                builder.Create()?.Show();
            }
            catch (Exception ex)
            {
                Log.Warn("HMS consent dialog failed: " + ex.Message);
                completion.TrySetResult(false);
            }
        });
        return completion.Task;
    }

    private void OnSubscriptionChanged()
    {
        if (_licenseService.IsPremium)
        {
            DisposeAllAds();
        }
        EligibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeAllAds()
    {
        _interstitial = null;
        _interstitialLoading = false;
    }

    private string ReadMetadata(string suffix)
        => Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(
                a.Key,
                MetadataPrefix + suffix,
                StringComparison.Ordinal))
            ?.Value ?? string.Empty;

    private void DispatchOnMainThread(Action action)
        => new Handler(Looper.MainLooper).Post(action);

    private static bool HasPackage(Context context, string packageName)
    {
        try
        {
            // Huawei's HMS Core package exposes the service metadata through
            // split APKs; a non-null package record is the reliable capability
            // signal even when ApplicationInfo is not populated by the binding.
            return context.PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.MetaData) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void LogInfo(string message)
        => global::Android.Util.Log.Info("NoctraAds", message);

    private static void LogWarn(string message)
        => global::Android.Util.Log.Warn("NoctraAds", message);

    private static class Log
    {
        public static void Info(string message) => LogInfo(message);
        public static void Warn(string message) => LogWarn(message);
    }

    private readonly record struct ConsentUpdateResult(
        bool Succeeded,
        bool NeedConsent,
        string? ErrorMessage);

    private sealed class HuaweiConsentUpdateListener : Java.Lang.Object, IConsentUpdateListener
    {
        private readonly Action<ConsentStatus, bool> _success;
        private readonly Action<string> _failed;

        public HuaweiConsentUpdateListener(
            Action<ConsentStatus, bool> success,
            Action<string> failed)
        {
            _success = success;
            _failed = failed;
        }

        public void OnFail(string error) => _failed(error);

        public void OnSuccess(ConsentStatus status, bool needConsent, IList<AdProvider> providers)
            => _success(status, needConsent);
    }

    private sealed class HuaweiDialogCancelListener : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        private readonly Action _cancel;

        public HuaweiDialogCancelListener(Action cancel) => _cancel = cancel;

        public void OnCancel(IDialogInterface? dialog) => _cancel();
    }

    internal sealed class HuaweiInterstitialListener : AdListener
    {
        private readonly Action? _loaded;
        private readonly Action<int>? _failed;
        private readonly Action? _opened;
        private readonly Action? _closed;

        public HuaweiInterstitialListener(
            Action? loaded = null,
            Action<int>? failed = null,
            Action? opened = null,
            Action? closed = null)
        {
            _loaded = loaded;
            _failed = failed;
            _opened = opened;
            _closed = closed;
        }

        public override void OnAdLoaded() => _loaded?.Invoke();

        public override void OnAdFailed(int errorCode) => _failed?.Invoke(errorCode);

        public override void OnAdOpened() => _opened?.Invoke();

        public override void OnAdClosed() => _closed?.Invoke();
    }
}

internal sealed class HuaweiBannerNativeControlHost : NativeControlHost
{
    private readonly string _adUnitId;
    private readonly Action<BannerAdLoadState>? _stateChanged;
    private Huawei.Hms.Ads.Banner.BannerView? _bannerView;
    private FrameLayout? _container;
    private int _disposed;
    private int _nativeGeneration;

    public HuaweiBannerNativeControlHost(
        string adUnitId,
        Action<BannerAdLoadState>? stateChanged)
    {
        _adUnitId = adUnitId;
        _stateChanged = stateChanged;
    }

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

        DestroyCurrentBanner();
        var generation = Interlocked.Increment(ref _nativeGeneration);
        var container = new FrameLayout(context);
        var banner = new Huawei.Hms.Ads.Banner.BannerView(context)
        {
            AdId = _adUnitId,
            BannerAdSize = Huawei.Hms.Ads.BannerAdSize.BannerSize32050
        };
        container.AddView(banner, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Center));
        _container = container;
        _bannerView = banner;
        banner.AdListener = new HuaweiBannerListener(
            banner,
            _stateChanged,
            () => Volatile.Read(ref _disposed) != 0 ||
                  Volatile.Read(ref _nativeGeneration) != generation);
        _stateChanged?.Invoke(BannerAdLoadState.Loading);
        banner.LoadAd(new AdParam.Builder().Build());
        return new AndroidViewControlHandle(container);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DestroyCurrentBanner();
        base.DestroyNativeControlCore(control);
    }

    public void DestroyAd()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DestroyCurrentBanner();
        }
    }

    private void DestroyCurrentBanner()
    {
        Interlocked.Increment(ref _nativeGeneration);
        var banner = _bannerView;
        _bannerView = null;
        if (banner is not null)
        {
            _container?.RemoveView(banner);
            banner.Destroy();
        }

        _container?.RemoveAllViews();
        _container = null;
    }
}

internal sealed class HuaweiBannerAdHandle : IDisposable
{
    private readonly HuaweiBannerNativeControlHost _host;
    private int _disposed;

    public HuaweiBannerAdHandle(HuaweiBannerNativeControlHost host) => _host = host;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _host.DestroyAd();
        }
    }
}

internal sealed class HuaweiBannerListener : AdListener
{
    private readonly Huawei.Hms.Ads.Banner.BannerView _banner;
    private readonly Action<BannerAdLoadState>? _stateChanged;
    private readonly Func<bool> _isInvalid;

    public HuaweiBannerListener(
        Huawei.Hms.Ads.Banner.BannerView banner,
        Action<BannerAdLoadState>? stateChanged,
        Func<bool> isInvalid)
    {
        _banner = banner;
        _stateChanged = stateChanged;
        _isInvalid = isInvalid;
    }

    public override void OnAdLoaded()
    {
        if (_isInvalid()) return;
        _stateChanged?.Invoke(BannerAdLoadState.Loaded);
        global::Android.Util.Log.Info("NoctraAds", "HMS banner loaded");
    }

    public override void OnAdFailed(int errorCode)
    {
        if (_isInvalid()) return;
        _stateChanged?.Invoke(BannerAdLoadState.Failed);
        global::Android.Util.Log.Warn("NoctraAds", $"HMS banner failed: code={errorCode}");
    }

    public override void OnAdImpression()
        => global::Android.Util.Log.Info("NoctraAds", "HMS banner impression");

    public override void OnAdClicked()
        => global::Android.Util.Log.Info("NoctraAds", "HMS banner clicked");
}
