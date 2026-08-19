using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Noctra.Core.Advertising;
using Noctra.Services;

namespace Noctra.Mobile.Services;

/// <summary>
/// Platform/provider boundary. The feed code never talks directly to AdMob or
/// any mediation network. A production Android provider owns consent, preloading,
/// banner lifetime, no-fill handling and full-screen presentation.
/// </summary>
public interface IMobileAdvertisingService
{
    AdvertisingOptions Options { get; }

    /// <summary>
    /// True when this user/device could see ads: free entitlement, advertising
    /// enabled by remote config and a real provider is available. Deliberately
    /// independent of UMP consent so bootstrap can run consent before the SDK
    /// is initialized (CanRequestAds is false until consent is updated).
    /// </summary>
    bool IsAdsEligible { get; }

    /// <summary>
    /// True when UMP consent state permits requesting ads (UMP's CanRequestAds).
    /// Requires a consent info update.
    /// </summary>
    bool CanRequestAds { get; }

    /// <summary>
    /// True when UMP demands a visible privacy-options entry point
    /// (PrivacyOptionsRequirementStatus == Required).
    /// </summary>
    bool CanShowPrivacyOptions { get; }

    /// <summary>
    /// Effective gate for the UI: eligible + consent granted + SDK initialized.
    /// </summary>
    bool CanServeAds { get; }

    event EventHandler? EligibilityChanged;

    /// <summary>
    /// Raised when UMP consent/privacy-options requirement changes so the
    /// Settings privacy section can show/hide the "Privacy choices" entry.
    /// </summary>
    event EventHandler? ConsentStatusChanged;

    void PrimeInterstitial();
    Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the UMP privacy-options form (GDPR/US state choices). Returns false
    /// when no form is available or no activity is present.
    /// </summary>
    Task<bool> ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs once at startup after the remote config refresh and only when
    /// <see cref="IsAdsEligible"/> is true. A production provider runs the
    /// consent flow (UMP) and Mobile Ads SDK initialization here; no-op
    /// providers return immediately.
    /// </summary>
    /// <summary>
    /// Creates a banner ad and attaches it to the given host control.
    /// Returns a disposable handle that releases the ad when disposed.
    /// Returns null when no banner ad can be served.
    /// </summary>
    IDisposable? CreateBannerAd(Control host);

    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Store-safe fallback. Ads are fail-closed: a missing SDK/configuration never
/// blocks navigation and never creates an empty ad row.
/// </summary>
public sealed class NoOpMobileAdvertisingService : IMobileAdvertisingService
{
    public AdvertisingOptions Options => AdvertisingOptions.ConservativeDefault;
    public bool IsAdsEligible => false;
    public bool CanRequestAds => false;
    public bool CanShowPrivacyOptions => false;
    public bool CanServeAds => false;
    public event EventHandler? EligibilityChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? ConsentStatusChanged
    {
        add { }
        remove { }
    }

    public void PrimeInterstitial() { }

    public Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public IDisposable? CreateBannerAd(Control host) => null;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// DEBUG-only visual harness. Set NOCTRA_ADS_PREVIEW=1 before launching Android
/// to exercise entitlement gating without requesting real ads.
/// </summary>
public sealed class PreviewMobileAdvertisingService : IMobileAdvertisingService
{
    private readonly ILicenseService _licenseService;

    public PreviewMobileAdvertisingService(ILicenseService licenseService)
    {
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _licenseService.SubscriptionChanged += OnSubscriptionChanged;
    }

    public static bool IsPreviewEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("NOCTRA_ADS_PREVIEW"),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// DEBUG-only override for platforms where the process environment cannot be
    /// seeded (Android). Set from the launch intent extra "noctra.ads.preview"
    /// in MainActivity (DEBUG builds only).
    /// </summary>
    public static bool DebugOverrideEnabled { get; set; }

    public static bool IsEnabled => IsPreviewEnabled || DebugOverrideEnabled;

    public AdvertisingOptions Options => AdvertisingOptions.ConservativeDefault;
    public bool IsAdsEligible => IsEnabled && !_licenseService.IsPremium;
    public bool CanRequestAds => IsEnabled && !_licenseService.IsPremium;
    public bool CanShowPrivacyOptions => false;
    public bool CanServeAds => IsEnabled && !_licenseService.IsPremium;
    public event EventHandler? EligibilityChanged;
    public event EventHandler? ConsentStatusChanged;

    public void PrimeInterstitial() { }

    public Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public IDisposable? CreateBannerAd(Control host) => null;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private void OnSubscriptionChanged()
        => EligibilityChanged?.Invoke(this, EventArgs.Empty);
}
