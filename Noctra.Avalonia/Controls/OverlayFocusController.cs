// ============================================================
// Noctra.Avalonia/Controls/OverlayFocusController.cs
//
// Refactored state machine for overlay visibility and focus management.
// ============================================================

using System;

namespace Noctra.Avalonia.Controls;

public sealed class OverlayFocusController
{
    private readonly Func<bool>   _isOurProcessActive;
    private readonly Func<bool>   _isEffectivelyVisible;
    private readonly Func<bool>   _isOverlayCurrentlyVisible;
    private readonly Action       _showOverlay;
    private readonly Action       _hideOverlay;
    private readonly Action<bool> _setTopmost;

    public bool IsRootActive { get; private set; } = true;
    public bool IsLayoutVisible { get; private set; } = true;

    public int ShowCallCount     { get; private set; }
    public int HideCallCount     { get; private set; }
    public int TopmostTrueCount  { get; private set; }
    public int TopmostFalseCount { get; private set; }

    public OverlayFocusController(
        Func<bool>   isOurProcessActive,
        Func<bool>   isEffectivelyVisible,
        Func<bool>   isOverlayCurrentlyVisible,
        Action       showOverlay,
        Action       hideOverlay,
        Action<bool> setTopmost)
    {
        _isOurProcessActive        = isOurProcessActive        ?? throw new ArgumentNullException(nameof(isOurProcessActive));
        _isEffectivelyVisible      = isEffectivelyVisible      ?? throw new ArgumentNullException(nameof(isEffectivelyVisible));
        _isOverlayCurrentlyVisible = isOverlayCurrentlyVisible ?? throw new ArgumentNullException(nameof(isOverlayCurrentlyVisible));
        _showOverlay               = showOverlay               ?? throw new ArgumentNullException(nameof(showOverlay));
        _hideOverlay               = hideOverlay               ?? throw new ArgumentNullException(nameof(hideOverlay));
        _setTopmost                = setTopmost                ?? throw new ArgumentNullException(nameof(setTopmost));
    }

    public void OnTimerTick()
    {
        // Always sync layout visibility from UI delegate
        IsLayoutVisible = _isEffectivelyVisible();
        
        bool isOurs = _isOurProcessActive();

        if (isOurs)
        {
            IsRootActive = true;

            if (IsLayoutVisible)
            {
                if (!_isOverlayCurrentlyVisible())
                {
                    _showOverlay();
                    ShowCallCount++;
                }
                _setTopmost(true);
                TopmostTrueCount++;
            }
            else
            {
                // Noctra has focus, but this specific view is hidden (navigated away)
                if (_isOverlayCurrentlyVisible())
                {
                    _hideOverlay();
                    HideCallCount++;
                }
            }
        }
        else
        {
            IsRootActive = false;

            if (_isOverlayCurrentlyVisible())
            {
                _setTopmost(false);
                TopmostFalseCount++;
                _hideOverlay();
                HideCallCount++;
            }
        }
    }

    public void OnLayoutChanged(bool isEffectivelyVisible)
    {
        IsLayoutVisible = isEffectivelyVisible;

    // Hide the overlay whenever layout visibility is lost, regardless of focus state.
    // Previously we relied on the timer tick to handle the `IsRootActive=true` case,
    // but the 200ms timer is now stopped when the layout is not visible (CPU optimization),
    // so we must hide immediately to prevent the overlay from lingering on screen.
    if (!IsLayoutVisible)
        {
            if (_isOverlayCurrentlyVisible())
            {
                _hideOverlay();
                HideCallCount++;
            }
        }
        else if (IsRootActive)
        {
            // Immediate show if we have focus and become visible
            if (!_isOverlayCurrentlyVisible())
            {
                _showOverlay();
                ShowCallCount++;
            }
            _setTopmost(true);
            TopmostTrueCount++;
        }
    }

    public void ResetCounters()
    {
        ShowCallCount     = 0;
        HideCallCount     = 0;
        TopmostTrueCount  = 0;
        TopmostFalseCount = 0;
    }
}
