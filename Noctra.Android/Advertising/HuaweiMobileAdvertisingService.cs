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
using Android.Text.Method;
using Android.Text.Util;
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

    private static readonly object SdkInitializationGate = new();
    private static bool _sdkInitializedForProcess;

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
    private int _consentRetryPending;
    private Huawei.Hms.Ads.Consent.Inter.Consent? _consent;
    private ConsentStatus _consentStatus = ConsentStatus.Unknown;
    private IReadOnlyList<AdProvider> _adProviders = Array.Empty<AdProvider>();
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
    /// Initializes Petal Ads at the Android application boundary, matching
    /// Huawei's integration guidance. The provider-specific consent pipeline
    /// still controls when requests are allowed; this only makes the SDK
    /// runtime ready early enough for HMS Core's network/GRS context.
    /// </summary>
    public static void InitializeSdkIfSupported(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsHmsOnlyDevice(context) && !ForceProviderForDebug)
        {
            return;
        }

        TryInitializeSdk(context);
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
            if (!firstRun && Volatile.Read(ref _consentRetryPending) == 0)
            {
                await EnsureHuaweiAdsInitializedCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (!firstRun)
            {
                Interlocked.Exchange(ref _initializationState, 1);
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
                _consentStatus = ConsentStatus.NonPersonalized;
                _adProviders = consentResult.Providers;
                Volatile.Write(ref _consentRetryPending, 1);
                _privacyOptionsRequired = true;
                _canRequestAds = true;
                if (_forceConsent)
                {
                    // Debug QA must still be able to exercise the Huawei
                    // privacy dialog when the lab device is offline. This
                    // branch is never enabled by Release metadata; production
                    // remains fail-closed on a consent-service outage.
                    Log.Warn("HMS consent debug fallback: showing local choice dialog");
                    _privacyOptionsRequired = true;
                    var choseConsent = await ShowConsentDialogAsync(
                        _consent,
                        _adProviders,
                        cancellationToken).ConfigureAwait(false);
                    Volatile.Write(ref _consentRetryPending, choseConsent ? 0 : 1);
                }
            }
            else if (consentResult.NeedConsent &&
                (consentResult.Status == ConsentStatus.Unknown ||
                 consentResult.Status.Value == ConsentStatus.Unknown.Value))
            {
                _privacyOptionsRequired = true;
                Volatile.Write(ref _consentRetryPending, 1);
                var choseConsent = await ShowConsentDialogAsync(
                    _consent,
                    consentResult.Providers,
                    cancellationToken).ConfigureAwait(false);
                // Huawei permits only non-personalized requests when the user
                // skips the choice, so keep the provider usable in that mode.
                _canRequestAds = true;
                Volatile.Write(ref _consentRetryPending, choseConsent ? 0 : 1);
            }
            else
            {
                _privacyOptionsRequired = consentResult.NeedConsent;
                _canRequestAds = true;
                Volatile.Write(ref _consentRetryPending, 0);
            }

            await EnsureHuaweiAdsInitializedCoreAsync(cancellationToken)
                .ConfigureAwait(false);

            Interlocked.Exchange(ref _initializationState, 2);
            ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
            EligibilityChanged?.Invoke(this, EventArgs.Empty);
            Log.Info($"HMS consent/init end canRequest={_canRequestAds} " +
                     $"privacyRequired={_privacyOptionsRequired} eligible={IsAdsEligible} " +
                     $"sdkInitialized={_sdkInitialized}");
        }
        catch (System.OperationCanceledException)
        {
            Interlocked.Exchange(ref _initializationState, 0);
            throw;
        }
        catch (Exception ex)
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

        return ShowConsentDialogAsync(_consent, _adProviders, cancellationToken);
    }

    private async Task InitializeHuaweiAdsAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            try
            {
                if (!TryInitializeSdk(_context))
                {
                    throw new InvalidOperationException("HMS Ads SDK initialization failed.");
                }

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

    private static bool TryInitializeSdk(Context context)
    {
        var applicationContext = context.ApplicationContext ?? context;
        lock (SdkInitializationGate)
        {
            if (_sdkInitializedForProcess)
            {
                return true;
            }

            try
            {
                HwAds.Init(applicationContext);
                _sdkInitializedForProcess = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("HMS SDK initialization failed: " + ex.Message);
                return false;
            }
        }
    }

    private async Task EnsureHuaweiAdsInitializedIfEligibleAsync(
        CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureHuaweiAdsInitializedCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureHuaweiAdsInitializedCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!CanRequestAds || !IsAdsEligible)
        {
            return;
        }

        var wasInitialized = _sdkInitialized;
        if (!wasInitialized)
        {
            await InitializeHuaweiAdsAsync(cancellationToken).ConfigureAwait(false);
        }

        ApplyHuaweiRequestOptions();
        if (!wasInitialized && _sdkInitialized)
        {
            EligibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyHuaweiRequestOptions()
    {
        var requestOptions = HwAds.RequestOptions ?? new RequestOptions();
        var nonPersonalizedMode =
            _consentStatus == ConsentStatus.Personalized ||
            _consentStatus.Value == ConsentStatus.Personalized.Value
                ? NonPersonalizedAd.AllowAll
                : NonPersonalizedAd.AllowNonPersonalized;

        HwAds.RequestOptions = requestOptions
            .ToBuilder()
            .SetNonPersonalizedAd(new global::Java.Lang.Integer(nonPersonalizedMode))
            .Build();
    }

    private Task<ConsentUpdateResult> RequestConsentUpdateAsync(
        Huawei.Hms.Ads.Consent.Inter.Consent consent,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ConsentUpdateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var listener = new HuaweiConsentUpdateListener(
            success: (status, needConsent, providers) =>
            {
                _consentStatus = status;
                _adProviders = providers;
                completion.TrySetResult(new ConsentUpdateResult(
                    true,
                    status,
                    needConsent,
                    providers,
                    null));
            },
            failed: error =>
                completion.TrySetResult(new ConsentUpdateResult(
                    false,
                    ConsentStatus.Unknown,
                    false,
                    Array.Empty<AdProvider>(),
                    error)));

        DispatchOnMainThread(() =>
        {
            try
            {
                consent.RequestConsentUpdate(listener);
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new ConsentUpdateResult(
                    false,
                    ConsentStatus.Unknown,
                    false,
                    Array.Empty<AdProvider>(),
                    ex.Message));
            }
        });
        return completion.Task;
    }

    private Task<bool> ShowConsentDialogAsync(
        Huawei.Hms.Ads.Consent.Inter.Consent consent,
        IReadOnlyList<AdProvider> providers,
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
                var messageView = new TextView(activity)
                {
                    Text =
                    "Allow Huawei Ads to use data for personalized advertising? " +
                    "You can change this choice later in Settings.\n\n" +
                    BuildProviderSummary(providers)
                };
                messageView.SetTextIsSelectable(true);
                messageView.MovementMethod = LinkMovementMethod.Instance;
                Linkify.AddLinks(messageView, MatchOptions.WebUrls);
                builder.SetView(messageView);
                builder.SetPositiveButton(
                    "Allow personalized ads",
                    (_, _) =>
                    {
                        consent.SetConsentStatus(ConsentStatus.Personalized);
                        _consentStatus = ConsentStatus.Personalized;
                        _adProviders = providers;
                        Volatile.Write(ref _consentRetryPending, 0);
                        _canRequestAds = true;
                        _privacyOptionsRequired = true;
                        completion.TrySetResult(true);
                        ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
                        EligibilityChanged?.Invoke(this, EventArgs.Empty);
                        _ = EnsureAfterConsentChoiceAsync();
                    });
                builder.SetNegativeButton(
                    "Use non-personalized ads",
                    (_, _) =>
                    {
                        consent.SetConsentStatus(ConsentStatus.NonPersonalized);
                        _consentStatus = ConsentStatus.NonPersonalized;
                        _adProviders = providers;
                        Volatile.Write(ref _consentRetryPending, 0);
                        _canRequestAds = true;
                        _privacyOptionsRequired = true;
                        completion.TrySetResult(true);
                        ConsentStatusChanged?.Invoke(this, EventArgs.Empty);
                        EligibilityChanged?.Invoke(this, EventArgs.Empty);
                        _ = EnsureAfterConsentChoiceAsync();
                    });
                builder.SetOnCancelListener(new HuaweiDialogCancelListener(
                    () => completion.TrySetResult(false)));
                var dialog = builder.Create();
                dialog?.Show();
            }
            catch (Exception ex)
            {
                Log.Warn("HMS consent dialog failed: " + ex.Message);
                completion.TrySetResult(false);
            }
        });
        return completion.Task;
    }

    private async Task EnsureAfterConsentChoiceAsync()
    {
        try
        {
            await EnsureHuaweiAdsInitializedIfEligibleAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OperationCanceledException)
        {
            Log.Warn("HMS post-consent initialization failed: " + ex.Message);
        }
    }

    private void OnSubscriptionChanged()
    {
        if (_licenseService.IsPremium)
        {
            DisposeAllAds();
        }
        else if (Volatile.Read(ref _consentRetryPending) != 0)
        {
            _ = RetryConsentAndInitializationAsync();
        }
        else if (_consent is not null && _canRequestAds)
        {
            _ = EnsureAfterConsentChoiceAsync();
        }

        EligibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RetryConsentAndInitializationAsync()
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OperationCanceledException)
        {
            Log.Warn("HMS consent retry failed: " + ex.Message);
        }
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

    private static string BuildProviderSummary(IReadOnlyList<AdProvider> providers)
    {
        if (providers.Count == 0)
        {
            return "Ad technology providers: Huawei Ads";
        }

        var lines = providers.Select(provider =>
        {
            var name = string.IsNullOrWhiteSpace(provider.Name)
                ? provider.Id
                : provider.Name;
            var area = string.IsNullOrWhiteSpace(provider.ServiceArea)
                ? string.Empty
                : $" ({provider.ServiceArea})";
            var privacy = string.IsNullOrWhiteSpace(provider.PrivacyPolicyUrl)
                ? string.Empty
                : $"\nPrivacy policy: {provider.PrivacyPolicyUrl}";
            return $"• {name}{area}{privacy}";
        });

        return "Ad technology providers:\n" + string.Join("\n", lines);
    }

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
        ConsentStatus Status,
        bool NeedConsent,
        IReadOnlyList<AdProvider> Providers,
        string? ErrorMessage);

    private sealed class HuaweiConsentUpdateListener : Java.Lang.Object, IConsentUpdateListener
    {
        private readonly Action<ConsentStatus, bool, IReadOnlyList<AdProvider>> _success;
        private readonly Action<string> _failed;

        public HuaweiConsentUpdateListener(
            Action<ConsentStatus, bool, IReadOnlyList<AdProvider>> success,
            Action<string> failed)
        {
            _success = success;
            _failed = failed;
        }

        public void OnFail(string error) => _failed(error);

        public void OnSuccess(ConsentStatus status, bool needConsent, IList<AdProvider> providers)
            => _success(
                status,
                needConsent,
                providers?.ToArray() ?? Array.Empty<AdProvider>());
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
    private const long BannerReloadDelayMs = 1200;
    private readonly string _adUnitId;
    private readonly Action<BannerAdLoadState>? _stateChanged;
    private Huawei.Hms.Ads.Banner.BannerView? _bannerView;
    private FrameLayout? _container;
    private int _disposed;
    private int _nativeGeneration;
    private int _reloadScheduled;

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
        _container = container;
        CreateAndLoadBanner(context, container, generation);
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
        DestroyBannerView();

        _container?.RemoveAllViews();
        _container = null;
    }

    private void CreateAndLoadBanner(
        Context context,
        FrameLayout container,
        int generation)
    {
        var banner = new Huawei.Hms.Ads.Banner.BannerView(context)
        {
            AdId = _adUnitId,
            BannerAdSize = Huawei.Hms.Ads.BannerAdSize.BannerSizeSmart
        };
        container.AddView(banner, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.CenterHorizontal));
        _bannerView = banner;
        banner.AdListener = new HuaweiBannerListener(
            banner,
            _stateChanged,
            () => Volatile.Read(ref _disposed) != 0 ||
                  Volatile.Read(ref _nativeGeneration) != generation,
            () => ScheduleBannerReload(generation));
        _stateChanged?.Invoke(BannerAdLoadState.Loading);
        banner.LoadAd(new AdParam.Builder().Build());
        MonitorBannerVisibility(generation);
    }

    private void MonitorBannerVisibility(int generation)
    {
        new Handler(Looper.MainLooper).PostDelayed(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                Volatile.Read(ref _nativeGeneration) != generation ||
                _bannerView is not { } banner ||
                _container is not { } container)
            {
                return;
            }

            var bannerIsAttached = banner.Parent is not null &&
                banner.Visibility == ViewStates.Visible;
            var hostIsVisible = container.IsShown &&
                container.WindowVisibility == ViewStates.Visible;
            if (hostIsVisible && !bannerIsAttached)
            {
                ScheduleBannerReload(generation);
                return;
            }

            MonitorBannerVisibility(generation);
        }, BannerReloadDelayMs);
    }

    private void ScheduleBannerReload(int generation)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _nativeGeneration) != generation ||
            Interlocked.Exchange(ref _reloadScheduled, 1) != 0)
        {
            return;
        }

        new Handler(Looper.MainLooper).PostDelayed(() =>
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    Volatile.Read(ref _nativeGeneration) != generation ||
                    _container is not { } container)
                {
                    return;
                }

                Interlocked.Increment(ref _nativeGeneration);
                DestroyBannerView();
                container.RemoveAllViews();
                var nextGeneration = Volatile.Read(ref _nativeGeneration);
                CreateAndLoadBanner(
                    container.Context ?? global::Android.App.Application.Context,
                    container,
                    nextGeneration);
            }
            finally
            {
                Volatile.Write(ref _reloadScheduled, 0);
            }
        }, BannerReloadDelayMs);
    }

    private void DestroyBannerView()
    {
        var banner = _bannerView;
        _bannerView = null;
        if (banner is not null)
        {
            _container?.RemoveView(banner);
            banner.Destroy();
        }
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
    private readonly Action? _reloadRequested;

    public HuaweiBannerListener(
        Huawei.Hms.Ads.Banner.BannerView banner,
        Action<BannerAdLoadState>? stateChanged,
        Func<bool> isInvalid,
        Action? reloadRequested)
    {
        _banner = banner;
        _stateChanged = stateChanged;
        _isInvalid = isInvalid;
        _reloadRequested = reloadRequested;
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

    public override void OnAdClosed()
    {
        if (_isInvalid()) return;
        global::Android.Util.Log.Info("NoctraAds", "HMS banner closed; scheduling reload");
        _reloadRequested?.Invoke();
    }

    public override void OnAdLeave()
    {
        if (_isInvalid()) return;
        global::Android.Util.Log.Info("NoctraAds", "HMS banner left; scheduling reload");
        _reloadRequested?.Invoke();
    }
}
