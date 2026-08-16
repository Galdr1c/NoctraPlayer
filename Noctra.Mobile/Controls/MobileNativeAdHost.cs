using System;
using Avalonia;
using Avalonia.Controls;
using Noctra.Mobile.Services;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Recyclable host for provider-owned native ad controls. A failed/no-fill slot
/// collapses to zero immediately; it never reserves blank feed space.
/// </summary>
public sealed class MobileNativeAdHost : ContentControl
{
    private MobileNativeAdSlot? _slot;
    private bool _hasAd;
    private bool _coordinatorSubscribed;

    public MobileNativeAdHost()
    {
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        AttachedToVisualTree += (_, _) => SubscribeCoordinator();
        DetachedFromVisualTree += (_, _) => UnsubscribeCoordinator();
    }

    public void Bind(MobileNativeAdSlot slot)
    {
        if (_slot == slot && _hasAd && Content is not null)
        {
            ApplyVisibility();
            return;
        }

        _slot = slot;
        Content = null;
        _hasAd = false;
        MinHeight = 0;
        IsVisible = false;

        var service = MobileAdvertisingServices.TryGet();
        if (service is null ||
            !service.CanServeAds ||
            !service.TryCreateNativeAdControl(slot, out var control) ||
            control is null)
        {
            return;
        }

        Content = control;
        _hasAd = true;
        MinHeight = 112;
        ApplyVisibility();
    }

    public void Clear()
    {
        _slot = null;
        Content = null;
        _hasAd = false;
        MinHeight = 0;
        IsVisible = false;
    }

    private void SubscribeCoordinator()
    {
        if (_coordinatorSubscribed)
            return;

        MobileNativeAdSurfaceCoordinator.Changed += Coordinator_Changed;
        _coordinatorSubscribed = true;
        ApplyVisibility();
    }

    private void UnsubscribeCoordinator()
    {
        if (!_coordinatorSubscribed)
            return;

        MobileNativeAdSurfaceCoordinator.Changed -= Coordinator_Changed;
        _coordinatorSubscribed = false;
    }

    private void Coordinator_Changed(object? sender, EventArgs e)
        => ApplyVisibility();

    private void ApplyVisibility()
        => IsVisible = _hasAd && !MobileNativeAdSurfaceCoordinator.IsSuppressed;
}
