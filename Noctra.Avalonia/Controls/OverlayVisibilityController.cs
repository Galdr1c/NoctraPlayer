using System;

namespace Noctra.Avalonia.Controls;

public sealed class OverlayVisibilityController
{
    private readonly Func<bool> _isOverlayCurrentlyVisible;
    private readonly Action _showOverlay;
    private readonly Action _hideOverlay;
    private readonly Func<bool> _isOverlayTopmost;
    private readonly Action<bool> _setOverlayTopmost;

    public OverlayVisibilityController(
        Func<bool> isOverlayCurrentlyVisible,
        Action showOverlay,
        Action hideOverlay,
        Func<bool> isOverlayTopmost,
        Action<bool> setOverlayTopmost)
    {
        _isOverlayCurrentlyVisible = isOverlayCurrentlyVisible
            ?? throw new ArgumentNullException(nameof(isOverlayCurrentlyVisible));
        _showOverlay = showOverlay ?? throw new ArgumentNullException(nameof(showOverlay));
        _hideOverlay = hideOverlay ?? throw new ArgumentNullException(nameof(hideOverlay));
        _isOverlayTopmost = isOverlayTopmost ?? throw new ArgumentNullException(nameof(isOverlayTopmost));
        _setOverlayTopmost = setOverlayTopmost ?? throw new ArgumentNullException(nameof(setOverlayTopmost));
    }

    public void Update(bool isEffectivelyVisible, bool ownerIsTopmost)
    {
        if (_isOverlayTopmost() != ownerIsTopmost)
            _setOverlayTopmost(ownerIsTopmost);

        if (isEffectivelyVisible)
        {
            if (!_isOverlayCurrentlyVisible())
                _showOverlay();

            return;
        }

        if (_isOverlayCurrentlyVisible())
            _hideOverlay();
    }
}
