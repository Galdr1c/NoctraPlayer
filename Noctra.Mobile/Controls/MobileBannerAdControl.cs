using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Noctra.Mobile.Services;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Persistent banner ad host placed at the bottom of the content area.
/// The platform provider (e.g. AdMob) loads a banner ad into this control.
/// Collapses to zero height when no ad is available, the user is premium,
/// or the shell chrome is hidden (player / overlays).
///
/// Android suspend/resume safety net: a native creative that was backgrounded
/// too long can die silently without raising a failure callback, leaving an
/// empty, non-clickable shell. After a long foreground absence the current ad
/// is destroyed and reloaded from scratch so a stale creative never survives.
/// </summary>
public sealed class MobileBannerAdControl : ContentControl
{
    private static readonly TimeSpan BannerStaleAfterBackground = ResolveStaleThreshold();

    private IDisposable? _adisposable;
    private readonly BannerAdPresentationState _adState = new();
    private DateTimeOffset? _backgroundedAtUtc;
    private bool _lifecycleSubscribed;

    public MobileBannerAdControl()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsVisible = false;
    }

    /// <summary>
    /// When true the loaded ad is hidden but kept alive (player open, overlay
    /// on top). Collapses the control like ClearAd would.
    /// </summary>
    public bool IsSuppressed
    {
        get => _adState.IsSuppressed;
        set
        {
            if (_adState.IsSuppressed == value)
            {
                return;
            }

            _adState.SetSuppressed(value);
            UpdateVisibility();
        }
    }

    /// <summary>
    /// Asks the advertising service to load a banner ad into this host.
    /// When ads are no longer servable (consent withdrawn, premium, provider
    /// gone) any existing banner is released so a stale ad never stays on
    /// screen; a later call reloads when eligibility returns.
    /// </summary>
    public void LoadAd()
    {
        var service = MobileAdvertisingServices.TryGet();
        if (service is null || !service.CanServeAds)
        {
            ClearAd();
            return;
        }

        if (_adisposable is not null)
        {
            UpdateVisibility();
            return;
        }

        var generation = _adState.BeginLoad();
        _adisposable = service.CreateBannerAd(
            this,
            state => OnAdLoadStateChanged(generation, state));

        if (_adisposable is null)
        {
            _adState.Clear();
        }

        UpdateVisibility();
    }

    /// <summary>
    /// Releases the current banner ad and collapses the control.
    /// </summary>
    public void ClearAd()
    {
        var disposable = _adisposable;
        _adisposable = null;
        Content = null;
        _adState.Clear();
        disposable?.Dispose();
        UpdateVisibility();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_lifecycleSubscribed)
        {
            return;
        }

        _lifecycleSubscribed = true;
        MobileAppLifecycle.Paused += OnAppPaused;
        MobileAppLifecycle.Resumed += OnAppResumed;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_lifecycleSubscribed)
        {
            _lifecycleSubscribed = false;
            MobileAppLifecycle.Paused -= OnAppPaused;
            MobileAppLifecycle.Resumed -= OnAppResumed;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnAppPaused(object? sender, EventArgs e)
        => _backgroundedAtUtc = DateTimeOffset.UtcNow;

    private void OnAppResumed(object? sender, EventArgs e)
    {
        var pausedAt = _backgroundedAtUtc;
        _backgroundedAtUtc = null;
        if (pausedAt is null)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - pausedAt.Value;
        if (elapsed < BannerStaleAfterBackground)
        {
            return;
        }

        Console.WriteLine(
            $"NoctraAds: banner stale after {elapsed.TotalSeconds:F0}s in background -> recreate");

        // A dead creative keeps reporting Loaded and never raises a failure
        // callback, so the only reliable fix is a full destroy + fresh request.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ClearAd();
            LoadAd();
        });
    }

    private static TimeSpan ResolveStaleThreshold()
    {
#if DEBUG
        // Short threshold so the recreate path can be verified on device
        // without waiting for a real long background session.
        return TimeSpan.FromSeconds(15);
#else
        return TimeSpan.FromMinutes(2);
#endif
    }

    private void OnAdLoadStateChanged(long generation, BannerAdLoadState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_adState.ApplyLoadState(generation, state))
            {
                return;
            }

            if (state == BannerAdLoadState.Failed)
            {
                ClearAd();
                return;
            }

            UpdateVisibility();
        });
    }

    private void UpdateVisibility()
    {
        // NativeControlHost must remain attached while the request is loading,
        // otherwise CreateNativeControlCore never runs. Keep that transient
        // host transparent and non-interactive; only a loaded creative is
        // visible/clickable, while a failure collapses the row completely.
        IsVisible = _adState.HasHandle && !_adState.IsSuppressed;
        IsHitTestVisible = _adState.IsVisible;
        Opacity = _adState.LoadState == BannerAdLoadState.Loading ? 0 : 1;
        if (IsVisible)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine(
                    $"NoctraAds: banner control bounds={Bounds.Width}x{Bounds.Height} visible={IsVisible}");
            });
        }
    }
}
