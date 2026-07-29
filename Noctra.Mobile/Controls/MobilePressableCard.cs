using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Border-based card surface that exposes a real :pressed state without
/// taking pointer capture away from the parent ScrollViewer.
/// </summary>
public sealed class MobilePressableCard : Border
{
    private const double ScrollCancellationDistance = 12d;

    private IPointer? _activePointer;
    private Point _pressOrigin;

    public MobilePressableCard()
    {
        AddHandler(
            PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            PointerMovedEvent,
            OnPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            PointerReleasedEvent,
            OnPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            PointerCaptureLostEvent,
            OnPointerCaptureLost,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        PointerExited += OnPointerExited;
        DetachedFromVisualTree += (_, _) => ResetPressedState();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_activePointer is not null || OriginatesFromNestedButton(e.Source))
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse &&
            !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _activePointer = e.Pointer;
        _pressOrigin = point.Position;
        PseudoClasses.Set(":pressed", true);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _activePointer))
        {
            return;
        }

        var current = e.GetPosition(this);
        var deltaX = current.X - _pressOrigin.X;
        var deltaY = current.Y - _pressOrigin.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) >=
            ScrollCancellationDistance * ScrollCancellationDistance)
        {
            ResetPressedState();
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _activePointer))
        {
            ResetPressedState();
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ResetPressedState();

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _activePointer))
        {
            ResetPressedState();
        }
    }

    private bool OriginatesFromNestedButton(object? source)
    {
        for (var visual = source as Visual;
             visual is not null && !ReferenceEquals(visual, this);
             visual = visual.GetVisualParent())
        {
            if (visual is Button)
            {
                return true;
            }
        }

        return false;
    }

    private void ResetPressedState()
    {
        _activePointer = null;
        PseudoClasses.Set(":pressed", false);
    }
}
