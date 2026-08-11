using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Navigation;

internal interface IMobileNavigationStateParticipant
{
    bool TryCaptureNavigationState(out MobilePageScrollState state);

    bool TryRestoreNavigationState(MobilePageScrollState state, bool allowClamping);
}

internal static class MobileNavigationScrollState
{
    public static bool TryCapture(
        Control primaryScrollOwner,
        out MobilePageScrollState state)
    {
        ArgumentNullException.ThrowIfNull(primaryScrollOwner);
        var scrollViewer = FindScrollViewer(primaryScrollOwner);
        if (scrollViewer is null || !scrollViewer.IsEffectivelyVisible)
        {
            state = MobilePageScrollState.Empty;
            return false;
        }

        state = new MobilePageScrollState(
            scrollViewer.Offset.X,
            scrollViewer.Offset.Y).Normalize();
        return true;
    }

    public static bool TryRestore(
        Control primaryScrollOwner,
        MobilePageScrollState state,
        bool allowClamping)
    {
        ArgumentNullException.ThrowIfNull(primaryScrollOwner);
        var scrollViewer = FindScrollViewer(primaryScrollOwner);
        if (scrollViewer is null ||
            !scrollViewer.IsEffectivelyVisible ||
            scrollViewer.Viewport.Width <= 0 ||
            scrollViewer.Viewport.Height <= 0)
        {
            return false;
        }

        var maximumHorizontalOffset = Math.Max(
            0,
            scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        var maximumVerticalOffset = Math.Max(
            0,
            scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var normalized = state.Normalize();
        if (!allowClamping &&
            (normalized.HorizontalOffset > maximumHorizontalOffset ||
             normalized.VerticalOffset > maximumVerticalOffset))
        {
            return false;
        }

        var clamped = state.Clamp(maximumHorizontalOffset, maximumVerticalOffset);
        scrollViewer.Offset = new Vector(clamped.HorizontalOffset, clamped.VerticalOffset);
        return true;
    }

    private static ScrollViewer? FindScrollViewer(Control primaryScrollOwner)
        => primaryScrollOwner as ScrollViewer ??
           primaryScrollOwner
               .GetVisualDescendants()
               .OfType<ScrollViewer>()
               .FirstOrDefault();
}
