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
/// A provider no-fill result preserves an already-loaded creative and uses a
/// bounded exponential refresh retry; an initial no-fill still collapses the
/// host so no blank slot is presented.
/// </summary>
public sealed class MobileBannerAdControl : ContentControl
{
    private static readonly TimeSpan BannerStaleAfterBackground = ResolveStaleThreshold();
    private static readonly TimeSpan BannerRetryBaseDelay = ResolveRetryBaseDelay();
    private static readonly TimeSpan BannerRetryMaxDelay = TimeSpan.FromMinutes(5);

    private IDisposable? _adisposable;
    private readonly BannerAdPresentationState _adState = new();
    private DateTimeOffset? _backgroundedAtUtc;
    private bool _lifecycleSubscribed;
    private Avalonia.Threading.DispatcherTimer? _retryTimer;
    private int _retryAttempt;
    private bool _hasLoadedCreative;

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
            if (value)
            {
                StopAdRetry(resetAttempt: false);
            }
            else if (_retryAttempt > 0)
            {
                // An ad is still on screen but a no-fill retry was pending
                // when the player suppressed the banner. Resume the search
                // for replacement inventory now that the chrome is visible.
                ScheduleAdRetry();
            }
            else if (_adisposable is null)
            {
                // A no-fill result while the shell was suppressed should be
                // retried once the navigation chrome is visible again.
                LoadAdCore(resetRetry: false);
            }
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
        => LoadAdCore(resetRetry: true);

    private void LoadAdCore(bool resetRetry)
    {
        if (resetRetry)
        {
            StopAdRetry();
        }

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
            ScheduleAdRetry();
        }

        UpdateVisibility();
    }

    /// <summary>
    /// Releases the current banner ad and collapses the control.
    /// </summary>
    public void ClearAd()
    {
        StopAdRetry();
        _hasLoadedCreative = false;
        ClearAdCore();
    }

    private void ClearAdCore()
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

        if (_retryAttempt > 0)
        {
            ScheduleAdRetry();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_lifecycleSubscribed)
        {
            _lifecycleSubscribed = false;
            MobileAppLifecycle.Paused -= OnAppPaused;
            MobileAppLifecycle.Resumed -= OnAppResumed;
        }

        // Keep the backoff attempt across a temporary detach, but never leave
        // a DispatcherTimer subscribed to a detached visual tree.
        StopAdRetry(resetAttempt: false);

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

    private void ScheduleAdRetry()
    {
        if (_retryTimer is not null ||
            _adState.IsSuppressed ||
            !_lifecycleSubscribed)
        {
            return;
        }

        var exponent = Math.Min(_retryAttempt, 3);
        var multiplier = Math.Pow(2, exponent);
        var delay = TimeSpan.FromTicks(Math.Min(
            BannerRetryMaxDelay.Ticks,
            (long)(BannerRetryBaseDelay.Ticks * multiplier)));
        _retryAttempt++;

        var timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = delay
        };
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            if (ReferenceEquals(_retryTimer, timer))
            {
                _retryTimer = null;
            }

            if (_adState.IsSuppressed)
            {
                return;
            }

            if (_hasLoadedCreative && _adisposable is IBannerAdRefreshHandle refreshHandle)
            {
                // Reuse the native view so the currently displayed creative
                // remains visible while the provider looks for replacement
                // inventory.
                refreshHandle.RequestRefresh();
            }
            else if (_adisposable is null)
            {
                LoadAdCore(resetRetry: false);
            }
        };
        timer.Tick += tick;
        _retryTimer = timer;
        timer.Start();

        Console.WriteLine(
            $"NoctraAds: banner retry scheduled in {delay.TotalSeconds:F0}s (attempt {_retryAttempt})");
    }

    private void StopAdRetry(bool resetAttempt = true)
    {
        var timer = _retryTimer;
        _retryTimer = null;
        if (timer is not null)
        {
            timer.Stop();
        }

        if (resetAttempt)
        {
            _retryAttempt = 0;
        }
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

    private static TimeSpan ResolveRetryBaseDelay()
    {
#if DEBUG
        return TimeSpan.FromSeconds(5);
#else
        return TimeSpan.FromSeconds(30);
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

            if (state == BannerAdLoadState.Loading)
            {
                // A native AdView recreate sends Loading for the fresh view.
                // The old creative is gone; if the new request fails we must
                // not pretend the previous creative is still on screen.
                _hasLoadedCreative = false;
            }

            if (state == BannerAdLoadState.Failed)
            {
                if (_hasLoadedCreative && _adisposable is not null)
                {
                    // A refresh no-fill does not invalidate the creative that
                    // is already on screen. Keep it visible/clickable while a
                    // later request looks for replacement inventory.
                    _adState.ApplyLoadState(generation, BannerAdLoadState.Loaded);
                    ScheduleAdRetry();
                    UpdateVisibility();
                    return;
                }

                // Keep the current backoff attempt so a persistent no-fill
                // response does not turn into a fixed-rate request loop.
                ClearAdForRetry();
                ScheduleAdRetry();
                return;
            }

            if (state == BannerAdLoadState.Loaded)
            {
                _hasLoadedCreative = true;
                StopAdRetry();
            }

            UpdateVisibility();
        });
    }

    private void ClearAdForRetry()
    {
        StopAdRetry(resetAttempt: false);
        ClearAdCore();
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
