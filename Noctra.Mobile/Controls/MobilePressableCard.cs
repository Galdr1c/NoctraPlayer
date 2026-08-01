using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Border-based card surface that exposes a real :pressed state without
/// taking pointer capture away from the parent ScrollViewer.
/// </summary>
public sealed class MobilePressableCard : Border
{
    private const double ScrollCancellationDistance = 12d;
    private static readonly TimeSpan LongPressDuration = TimeSpan.FromMilliseconds(500);

    private IPointer? _activePointer;
    private ScrollViewer? _ancestorScrollViewer;
    private Point _pressOrigin;
    private readonly DispatcherTimer _longPressTimer;
    private bool _suppressNextTap;

    public event EventHandler? LongPressed;

    public MobilePressableCard()
    {
        _longPressTimer = new DispatcherTimer { Interval = LongPressDuration };
        _longPressTimer.Tick += OnLongPressTimerTick;
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
        AddHandler(
            InputElement.ScrollGestureEvent,
            OnScrollGesture,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        PointerExited += OnPointerExited;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public bool ConsumeLongPressTapSuppression()
    {
        if (!_suppressNextTap)
        {
            return false;
        }

        _suppressNextTap = false;
        return true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_activePointer is not null || OriginatesFromNestedButton(e.Source))
        {
            return;
        }

        EnsureAncestorScrollViewer();

        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse &&
            !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _activePointer = e.Pointer;
        _pressOrigin = point.Position;
        _suppressNextTap = false;
        PseudoClasses.Set(":pressed", true);
        _longPressTimer.Stop();
        _longPressTimer.Start();
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
            CancelForScroll();
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
        CancelForScroll();

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _activePointer))
        {
            CancelForScroll();
        }
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (_activePointer is not null)
        {
            CancelForScroll();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        EnsureAncestorScrollViewer();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachFromAncestorScrollViewer();
        _suppressNextTap = false;
        ResetPressedState();
    }

    private void EnsureAncestorScrollViewer()
    {
        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (ReferenceEquals(scrollViewer, _ancestorScrollViewer))
        {
            return;
        }

        DetachFromAncestorScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        _ancestorScrollViewer = scrollViewer;
        _ancestorScrollViewer.ScrollChanged += OnAncestorScrollChanged;
        _ancestorScrollViewer.AddHandler(
            InputElement.ScrollGestureEvent,
            OnScrollGesture,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void DetachFromAncestorScrollViewer()
    {
        if (_ancestorScrollViewer is null)
        {
            return;
        }

        _ancestorScrollViewer.ScrollChanged -= OnAncestorScrollChanged;
        _ancestorScrollViewer.RemoveHandler(
            InputElement.ScrollGestureEvent,
            OnScrollGesture);
        _ancestorScrollViewer = null;
    }

    private void OnAncestorScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        CancelForScroll();
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

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_activePointer is null)
        {
            return;
        }

        _suppressNextTap = true;
        var longPressed = LongPressed;
        ResetPressedState();
        longPressed?.Invoke(this, EventArgs.Empty);
    }

    private void CancelForScroll()
    {
        if (_activePointer is null)
        {
            return;
        }

        // A scroll gesture must never be interpreted as a card tap after the
        // ScrollViewer releases the pointer back to the card.
        _suppressNextTap = true;
        ResetPressedState();
    }

    private void ResetPressedState()
    {
        _longPressTimer.Stop();
        _activePointer = null;
        PseudoClasses.Set(":pressed", false);
    }
}
