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
    private bool _isSuppressed;

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
        get => _isSuppressed;
        set
        {
            if (_isSuppressed == value)
            {
                return;
            }

            _isSuppressed = value;
            UpdateVisibility();
        }
    }

    /// <summary>
    /// Asks the advertising service to load a banner ad into this host.
    /// </summary>
    public void LoadAd()
    {
        if (_adisposable is not null)
        {
            UpdateVisibility();
            return;
        }

        var service = MobileAdvertisingServices.TryGet();
        if (service is null || !service.CanServeAds)
        {
            return;
        }

        _adisposable = service.CreateBannerAd(this);
        UpdateVisibility();
    }

    /// <summary>
    /// Releases the current banner ad and collapses the control.
    /// </summary>
    public void ClearAd()
    {
        _adisposable?.Dispose();
        _adisposable = null;
        Content = null;
        UpdateVisibility();
    }

    private void UpdateVisibility()
        => IsVisible = _adisposable is not null && !_isSuppressed;
}
