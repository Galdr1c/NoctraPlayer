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
/// </summary>
public sealed class MobileBannerAdControl : ContentControl
{
    private IDisposable? _adisposable;
    private readonly BannerAdPresentationState _adState = new();

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
