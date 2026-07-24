using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Behaviors;

/// <summary>
/// Provides a lightweight Android-style visual cue when a vertical scroll
/// gesture tries to move beyond the beginning or end of a ScrollViewer.
/// The feedback is visual-only and never changes the real scroll offset.
/// </summary>
internal sealed class MobileScrollEdgeFeedbackController
{
    private const double EdgeTolerance = 1;
    private static readonly TimeSpan HoldDuration =
        TimeSpan.FromMilliseconds(110);

    private readonly UserControl _host;
    private readonly DispatcherTimer _fadeTimer;
    private Grid? _rootGrid;
    private Canvas? _feedbackLayer;
    private Border? _topFeedback;
    private Border? _bottomFeedback;
    private DateTime _lastFeedbackUtc = DateTime.MinValue;
    private bool _isScrollGestureActive;

    public MobileScrollEdgeFeedbackController(UserControl host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _fadeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _fadeTimer.Tick += OnFadeTimerTick;

        _host.AddHandler(
            InputElement.ScrollGestureEvent,
            OnScrollGesture,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _host.AddHandler(
            InputElement.ScrollGestureEndedEvent,
            OnScrollGestureEnded,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _host.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheel,
            RoutingStrategies.Bubble,
            handledEventsToo: true);


        RefreshVisualTree();
    }

    public void RefreshVisualTree()
    {
        if (_host.Content is not Grid rootGrid)
        {
            Hide();
            return;
        }

        if (ReferenceEquals(_rootGrid, rootGrid) &&
            _feedbackLayer is not null &&
            rootGrid.Children.Contains(_feedbackLayer))
        {
            return;
        }

        if (_rootGrid is not null && _feedbackLayer is not null &&
            _rootGrid.Children.Contains(_feedbackLayer))
        {
            _rootGrid.Children.Remove(_feedbackLayer);
        }

        _rootGrid = rootGrid;
        _topFeedback = CreateFeedbackBorder(isTop: true);
        _bottomFeedback = CreateFeedbackBorder(isTop: false);
        _feedbackLayer = new Canvas
        {
            Name = "GlobalScrollEdgeFeedbackLayer",
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            ZIndex = 60000
        };
        _feedbackLayer.Children.Add(_topFeedback);
        _feedbackLayer.Children.Add(_bottomFeedback);

        Grid.SetRowSpan(
            _feedbackLayer,
            Math.Max(1, rootGrid.RowDefinitions.Count));
        Grid.SetColumnSpan(
            _feedbackLayer,
            Math.Max(1, rootGrid.ColumnDefinitions.Count));
        rootGrid.Children.Add(_feedbackLayer);
    }

    public void Hide()
    {
        _fadeTimer.Stop();
        HideFeedback(_topFeedback);
        HideFeedback(_bottomFeedback);
    }

    private Border CreateFeedbackBorder(bool isTop)
    {
        return new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            Opacity = 0,
            Height = 0,
            Background = ResolveFeedbackBrush(),
            CornerRadius = isTop
                ? new CornerRadius(0, 0, 48, 48)
                : new CornerRadius(48, 48, 0, 0)
        };
    }

    private IBrush ResolveFeedbackBrush()
    {
        if (_host.TryFindResource(
                "AccentBrush",
                _host.ActualThemeVariant,
                out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromRgb(230, 57, 120));
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        _isScrollGestureActive = true;
        TryShow(e.Source, e.Delta.Y);
    }

    private void OnScrollGestureEnded(object? sender, ScrollGestureEndedEventArgs e)
    {
        _isScrollGestureActive = false;
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        _lastFeedbackUtc = DateTime.UtcNow;
        TryShow(e.Source, -e.Delta.Y);
    }

    private void TryShow(object? source, double intendedOffsetDeltaY)
    {
        if (Math.Abs(intendedOffsetDeltaY) < double.Epsilon)
        {
            return;
        }

        if (_rootGrid is null ||
            _feedbackLayer is null ||
            _topFeedback is null ||
            _bottomFeedback is null)
        {
            RefreshVisualTree();
        }

        if (_rootGrid is null ||
            _feedbackLayer is null ||
            _topFeedback is null ||
            _bottomFeedback is null)
        {
            return;
        }

        var scrollViewer = FindVerticalScrollViewer(source);
        if (scrollViewer is null)
        {
            return;
        }

        var maximumOffset = Math.Max(
            0,
            scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (maximumOffset <= EdgeTolerance)
        {
            return;
        }

        var atTop = scrollViewer.Offset.Y <= EdgeTolerance;
        var atBottom = scrollViewer.Offset.Y >= maximumOffset - EdgeTolerance;
        var showTop = intendedOffsetDeltaY < 0 && atTop;
        var showBottom = intendedOffsetDeltaY > 0 && atBottom;
        if (!showTop && !showBottom)
        {
            return;
        }

        var origin = scrollViewer.TranslatePoint(default, _rootGrid);
        if (origin is not { } viewerOrigin)
        {
            return;
        }

        var rootWidth = _rootGrid.Bounds.Width;
        var rootHeight = _rootGrid.Bounds.Height;
        if (rootWidth <= 0 || rootHeight <= 0)
        {
            return;
        }

        var left = Math.Clamp(viewerOrigin.X, 0, rootWidth);
        var top = Math.Clamp(viewerOrigin.Y, 0, rootHeight);
        var right = Math.Clamp(
            viewerOrigin.X + scrollViewer.Bounds.Width,
            left,
            rootWidth);
        var bottom = Math.Clamp(
            viewerOrigin.Y + scrollViewer.Bounds.Height,
            top,
            rootHeight);
        var feedbackWidth = right - left;
        if (feedbackWidth <= 0 || bottom <= top)
        {
            return;
        }

        var intensity = Math.Clamp(
            Math.Abs(intendedOffsetDeltaY) / 48d,
            0.22,
            1d);
        var feedbackHeight = 12 + (34 * intensity);
        var feedbackOpacity = 0.12 + (0.18 * intensity);
        var activeFeedback = showTop ? _topFeedback : _bottomFeedback;
        var inactiveFeedback = showTop ? _bottomFeedback : _topFeedback;

        activeFeedback.Background = ResolveFeedbackBrush();
        activeFeedback.Width = feedbackWidth;
        activeFeedback.Height = Math.Min(feedbackHeight, bottom - top);
        activeFeedback.Opacity = feedbackOpacity;
        activeFeedback.IsVisible = true;
        HideFeedback(inactiveFeedback);

        Canvas.SetLeft(activeFeedback, left);
        Canvas.SetTop(
            activeFeedback,
            showTop
                ? top
                : Math.Max(top, bottom - activeFeedback.Height));

        _lastFeedbackUtc = DateTime.UtcNow;
        if (!_fadeTimer.IsEnabled)
        {
            _fadeTimer.Start();
        }
    }

    private static ScrollViewer? FindVerticalScrollViewer(object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        if (visual is ScrollViewer ownScrollViewer &&
            IsVerticallyScrollable(ownScrollViewer))
        {
            return ownScrollViewer;
        }

        return visual.GetVisualAncestors()
            .OfType<ScrollViewer>()
            .FirstOrDefault(IsVerticallyScrollable);
    }

    private static bool IsVerticallyScrollable(ScrollViewer scrollViewer)
        => scrollViewer.IsEffectivelyVisible &&
           scrollViewer.Viewport.Height > 0 &&
           scrollViewer.Extent.Height >
               scrollViewer.Viewport.Height + EdgeTolerance;

    private void OnFadeTimerTick(object? sender, EventArgs e)
    {
        // While a touch scroll gesture is active, keep the feedback visible.
        if (_isScrollGestureActive)
        {
            return;
        }

        if (DateTime.UtcNow - _lastFeedbackUtc < HoldDuration)
        {
            return;
        }

        var hasVisibleFeedback = Fade(_topFeedback) | Fade(_bottomFeedback);
        if (!hasVisibleFeedback)
        {
            _fadeTimer.Stop();
        }
    }

    private static bool Fade(Border? feedback)
    {
        if (feedback?.IsVisible != true)
        {
            return false;
        }

        feedback.Opacity = Math.Max(0, feedback.Opacity - 0.045);
        if (feedback.Opacity <= 0.01)
        {
            HideFeedback(feedback);
            return false;
        }

        return true;
    }

    private static void HideFeedback(Border? feedback)
    {
        if (feedback is null)
        {
            return;
        }

        feedback.Opacity = 0;
        feedback.IsVisible = false;
    }
}
