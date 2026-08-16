using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Noctra.Core.Advertising;
using Noctra.Services;

namespace Noctra.Mobile.Services;

public readonly record struct MobileNativeAdSlot(
    string OwnerKey,
    AdPlacement Placement,
    int Ordinal);

/// <summary>
/// Platform/provider boundary. The feed code never talks directly to AdMob or
/// any mediation network. A production Android provider owns consent, preloading,
/// NativeAd lifetime, no-fill handling and full-screen presentation.
/// </summary>
public interface IMobileAdvertisingService
{
    AdvertisingOptions Options { get; }
    bool CanServeAds { get; }
    event EventHandler? EligibilityChanged;

    void PrimeNative(string ownerKey, AdPlacement placement, int slotCount);
    bool TryCreateNativeAdControl(MobileNativeAdSlot slot, out Control? control);
    void ReleaseOwner(string ownerKey);

    void PrimeInterstitial();
    Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs once at startup after the remote config refresh and only when
    /// <see cref="CanServeAds"/> is true. A production provider runs the
    /// consent flow (UMP) and Mobile Ads SDK initialization here; no-op
    /// providers return immediately.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Store-safe fallback. Ads are fail-closed: a missing SDK/configuration never
/// blocks navigation and never creates an empty ad row.
/// </summary>
public sealed class NoOpMobileAdvertisingService : IMobileAdvertisingService
{
    public AdvertisingOptions Options => AdvertisingOptions.ConservativeDefault;
    public bool CanServeAds => false;
    public event EventHandler? EligibilityChanged
    {
        add { }
        remove { }
    }

    public void PrimeNative(string ownerKey, AdPlacement placement, int slotCount) { }
    public bool TryCreateNativeAdControl(MobileNativeAdSlot slot, out Control? control)
    {
        control = null;
        return false;
    }

    public void ReleaseOwner(string ownerKey) { }
    public void PrimeInterstitial() { }

    public Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// DEBUG-only visual harness. Set NOCTRA_ADS_PREVIEW=1 before launching Android
/// to exercise row placement/recycling without requesting real ads.
/// </summary>
public sealed class PreviewMobileAdvertisingService : IMobileAdvertisingService
{
    private readonly ILicenseService _licenseService;
    private readonly IRemoteAdvertisingConfigService? _remoteConfig;
    private readonly HashSet<MobileNativeAdSlot> _primedSlots = new();

    public PreviewMobileAdvertisingService(
        ILicenseService licenseService,
        IRemoteAdvertisingConfigService? remoteConfig = null)
    {
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _remoteConfig = remoteConfig;
        _licenseService.SubscriptionChanged += OnSubscriptionChanged;
        if (_remoteConfig is not null)
        {
            _remoteConfig.OptionsChanged += OnRemoteConfigChanged;
        }
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

    public AdvertisingOptions Options =>
        _remoteConfig?.CurrentOptions ?? AdvertisingOptions.ConservativeDefault;
    public bool CanServeAds => IsEnabled && !_licenseService.IsPremium;
    public event EventHandler? EligibilityChanged;

    public void PrimeNative(string ownerKey, AdPlacement placement, int slotCount)
    {
        if (!CanServeAds || string.IsNullOrWhiteSpace(ownerKey) || slotCount <= 0)
            return;

        for (var ordinal = 0; ordinal < slotCount; ordinal++)
        {
            _primedSlots.Add(new MobileNativeAdSlot(ownerKey, placement, ordinal));
        }
    }

    public bool TryCreateNativeAdControl(MobileNativeAdSlot slot, out Control? control)
    {
        if (!CanServeAds || !_primedSlots.Contains(slot))
        {
            control = null;
            return false;
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12
        };

        grid.Children.Add(new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.DimGray,
            Child = new TextBlock
            {
                Text = "AD",
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var copy = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(copy, 1);
        copy.Children.Add(new TextBlock
        {
            Text = "Sponsored • Preview",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{slot.Placement} / slot {slot.Ordinal + 1}",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Real providers replace this control with a mediated native ad.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(copy);

        control = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8),
            MinHeight = 112,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.DimGray,
            Background = Brushes.Transparent,
            Child = grid
        };
        return true;
    }

    public void ReleaseOwner(string ownerKey)
    {
        _primedSlots.RemoveWhere(slot =>
            string.Equals(slot.OwnerKey, ownerKey, StringComparison.Ordinal));
    }

    public void PrimeInterstitial() { }

    public Task<bool> TryShowInterstitialAsync(
        InterstitialAdContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private void OnSubscriptionChanged()
        => EligibilityChanged?.Invoke(this, EventArgs.Empty);

    private void OnRemoteConfigChanged(object? sender, EventArgs e)
        => EligibilityChanged?.Invoke(this, EventArgs.Empty);
}
