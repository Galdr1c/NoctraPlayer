using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Noctra.Mobile.Controls;

namespace Noctra.Mobile.Behaviors;

/// <summary>
/// Android-style stretch overscroll for touch and pen input.
///
/// Unlike the former global color overlay, this controller transforms the
/// content of the ScrollViewer that actually reached its edge. The transform
/// stays inside ScrollContentPresenter's clipped viewport, respects nested
/// scroll chaining, ignores mouse-wheel input, and uses frame-time based
/// spring release animation.
/// </summary>
internal sealed class MobileStretchOverscrollController : IDisposable
{
    private enum OverscrollState
    {
        Idle,
        PullingTop,
        PullingBottom,
        Releasing
    }

    private enum OverscrollEdge
    {
        Top,
        Bottom
    }

    private sealed class Session
    {
        public required ScrollViewer Viewer { get; init; }
        public required ScrollContentPresenter Presenter { get; init; }
        public required Control Content { get; init; }
        public required OverscrollEdge Edge { get; set; }
        public required ITransform? OriginalTransform { get; init; }
        public required RelativePoint OriginalOrigin { get; init; }
        public required TransformGroup AppliedTransform { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Translation { get; init; }
    }

    private readonly UserControl _host;
    private Grid? _feedbackRoot;
    private Canvas? _feedbackLayer;
    private Border? _topGlow;
    private Border? _bottomGlow;
    private IPointer? _pointer;
    private Visual? _pressedSource;
    private Point _pressPosition;
    private Point _lastPosition;
    private double _edgeProbeDistance;
    private ScrollViewer? _edgeProbeViewer;
    private OverscrollEdge? _edgeProbeEdge;
    private double _activationDistance = MobileOverscrollPhysics.FallbackActivationDistance;
    private bool _verticalAxisLocked;
    private bool _verticalGestureRejected;
    private double _rawPullDistance;
    private Session? _session;
    private OverscrollState _state;
    private bool _ownsPointerCapture;
    private bool _suppressCaptureLost;
    private long _animationVersion;
    private bool _disposed;

    public MobileStretchOverscrollController(UserControl host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        _host.AddHandler(
            InputElement.PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _host.AddHandler(
            InputElement.PointerMovedEvent,
            OnPointerMoved,
            // Observe offsets before ScrollViewer consumes this move. This lets
            // an already-reached edge react immediately without counting the
            // final, normally-consumed scroll delta as overscroll.
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _host.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _host.AddHandler(
            InputElement.PointerCaptureLostEvent,
            OnPointerCaptureLost,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _host.AddHandler(
            InputElement.ScrollGestureEvent,
            OnScrollGesture,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        RefreshVisualTree();
    }

    public void RefreshVisualTree()
    {
        // Hot reload can replace presenter/content instances. Never retain or
        // animate transforms that belong to a detached visual tree.
        if (_session is not null &&
            (TopLevel.GetTopLevel(_session.Viewer) is null ||
             TopLevel.GetTopLevel(_session.Content) is null ||
             !ReferenceEquals(
                 _session.Content.RenderTransform,
                 _session.AppliedTransform)))
        {
            ResetSession(restoreTransform: ReferenceEquals(
                _session.Content.RenderTransform,
                _session.AppliedTransform));
        }

        EnsureFeedbackLayer();
    }

    public void Hide()
    {
        ReleasePointerCapture();
        ResetPointerTracking();
        ResetSession(restoreTransform: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        DetachFeedbackLayer();
        _host.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _host.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _host.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _host.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
        _host.RemoveHandler(InputElement.ScrollGestureEvent, OnScrollGesture);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_disposed || !IsTouchOrPen(e.Pointer) ||
            (_pointer is not null && e.Pointer != _pointer) ||
            IsExcludedGestureSource(e.Source as Visual))
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(_host);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pointer = e.Pointer;
        _pressedSource = e.Source as Visual;
        _pressPosition = currentPoint.Position;
        _lastPosition = currentPoint.Position;
        ResetEdgeProbe();
        _activationDistance = GetActivationDistance(e.Pointer.Type);

        if (_state == OverscrollState.Releasing &&
            _session is not null &&
            IsInsideViewer(_pressedSource, _session.Viewer))
        {
            CancelAnimation();
            _rawPullDistance = MobileOverscrollPhysics.EstimatePullDistance(
                Math.Abs(_session.Translation.Y),
                GetViewportHeight(_session));
            _state = _session.Edge == OverscrollEdge.Top
                ? OverscrollState.PullingTop
                : OverscrollState.PullingBottom;
            _verticalAxisLocked = true;
            CapturePointer(e.Pointer);
            if (!_ownsPointerCapture)
            {
                BeginRelease(MobileOverscrollPhysics.ReleaseDuration);
                return;
            }

            e.PreventGestureRecognition();
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_disposed || e.Pointer != _pointer || !IsTouchOrPen(e.Pointer))
        {
            return;
        }

        var position = e.GetPosition(_host);
        var delta = position - _lastPosition;
        _lastPosition = position;

        if (_session is not null && _ownsPointerCapture)
        {
            ApplyCapturedPointerDelta(delta.Y);
            e.PreventGestureRecognition();
            e.Handled = true;
            return;
        }

        if (_verticalGestureRejected)
        {
            return;
        }

        var total = position - _pressPosition;
        var horizontalDistance = Math.Abs(total.X);
        var verticalDistance = Math.Abs(total.Y);

        if (!_verticalAxisLocked)
        {
            if (horizontalDistance >= _activationDistance &&
                horizontalDistance > verticalDistance * MobileOverscrollPhysics.AxisLockRatio)
            {
                // Once a horizontal carousel/slider wins the gesture, never
                // steal it later because of a small diagonal correction.
                _verticalGestureRejected = true;
                return;
            }

            if (verticalDistance < _activationDistance ||
                verticalDistance < horizontalDistance * MobileOverscrollPhysics.AxisLockRatio)
            {
                return;
            }

            _verticalAxisLocked = true;
        }

        var intendedOffsetDelta = -delta.Y;
        var target = ResolveOverscrollTarget(_pressedSource, intendedOffsetDelta);
        if (target is null)
        {
            ResetEdgeProbe();
            return;
        }

        if (!ReferenceEquals(_edgeProbeViewer, target.Value.Viewer) ||
            _edgeProbeEdge != target.Value.Edge)
        {
            _edgeProbeViewer = target.Value.Viewer;
            _edgeProbeEdge = target.Value.Edge;
            _edgeProbeDistance = 0;
        }

        var outwardDelta = target.Value.Edge == OverscrollEdge.Top
            ? delta.Y
            : -delta.Y;
        _edgeProbeDistance = Math.Max(0, _edgeProbeDistance + outwardDelta);
        if (_edgeProbeDistance < _activationDistance)
        {
            return;
        }

        if (!BeginSession(target.Value.Viewer, target.Value.Edge))
        {
            ResetEdgeProbe();
            return;
        }

        CancelCardLongPress(_pressedSource);

        _rawPullDistance = Math.Max(
            0,
            _edgeProbeDistance - _activationDistance);
        ApplyVisualPull();
        CapturePointer(e.Pointer);
        if (!_ownsPointerCapture)
        {
            BeginRelease(MobileOverscrollPhysics.ReleaseDuration);
            return;
        }

        e.PreventGestureRecognition();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _pointer)
        {
            return;
        }

        var hadCapture = _ownsPointerCapture;
        ReleasePointerCapture();
        ResetPointerTracking();
        BeginRelease(MobileOverscrollPhysics.ReleaseDuration);

        if (hadCapture)
        {
            e.PreventGestureRecognition();
            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer != _pointer || _suppressCaptureLost ||
            ReferenceEquals(e.Pointer.Captured, _host))
        {
            return;
        }

        // Also clear passive tracking when the platform cancels a normal
        // ScrollViewer gesture before this controller owns pointer capture.
        _ownsPointerCapture = false;
        ResetPointerTracking();
        BeginRelease(MobileOverscrollPhysics.ReleaseDuration);
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        // ScrollContentPresenter gets first chance. An unhandled gesture means
        // the complete nested chain could not consume the remaining delta.
        if (_disposed || _pointer is not null || _session is not null ||
            (e.Handled && !e.ShouldEndScrollGesture) ||
            Math.Abs(e.Delta.Y) < double.Epsilon)
        {
            return;
        }

        var target = ResolveOverscrollTarget(e.Source as Visual, e.Delta.Y);
        if (target is null || !BeginSession(target.Value.Viewer, target.Value.Edge))
        {
            return;
        }

        CancelCardLongPress(e.Source as Visual);
        _rawPullDistance = MobileOverscrollPhysics.EstimatePullDistance(
            MobileOverscrollPhysics.FlingBounceDistance,
            GetViewportHeight(_session!));
        ApplyVisualPull();
        e.Handled = true;
        e.ShouldEndScrollGesture = true;
        BeginRelease(MobileOverscrollPhysics.FlingReleaseDuration);
    }

    private void ApplyCapturedPointerDelta(double pointerDeltaY)
    {
        if (_session is null || Math.Abs(pointerDeltaY) < double.Epsilon)
        {
            return;
        }

        var outwardSign = _session.Edge == OverscrollEdge.Top ? 1d : -1d;
        var outwardDelta = pointerDeltaY * outwardSign;

        if (_rawPullDistance > 0)
        {
            _rawPullDistance += outwardDelta;
            if (_rawPullDistance >= 0)
            {
                ApplyVisualPull();
                return;
            }

            // Reverse movement first consumes the visible stretch. Only the
            // remainder is allowed to scroll the content.
            var inwardRemainder = -_rawPullDistance;
            _rawPullDistance = 0;
            ApplyVisualPull();
            pointerDeltaY = -inwardRemainder * outwardSign;
        }

        var unconsumedOffsetPixels = ScrollManually(_session, -pointerDeltaY);
        if (Math.Abs(unconsumedOffsetPixels) <= MobileOverscrollPhysics.EdgeTolerance)
        {
            return;
        }

        var newEdge = unconsumedOffsetPixels < 0
            ? OverscrollEdge.Top
            : OverscrollEdge.Bottom;
        SwitchEdge(newEdge);
        _rawPullDistance = Math.Abs(unconsumedOffsetPixels);
        ApplyVisualPull();
    }

    private double ScrollManually(Session session, double intendedOffsetPixels)
    {
        var factor = GetOffsetUnitsPerPixel(session);
        var intendedUnits = intendedOffsetPixels * factor;
        var maximum = GetMaximumOffset(session.Viewer);
        var oldOffset = session.Viewer.Offset.Y;
        var newOffset = Math.Clamp(oldOffset + intendedUnits, 0, maximum);

        session.Viewer.SetCurrentValue(
            ScrollViewer.OffsetProperty,
            session.Viewer.Offset.WithY(newOffset));

        var consumedUnits = newOffset - oldOffset;
        var unconsumedUnits = intendedUnits - consumedUnits;
        return factor <= double.Epsilon ? 0 : unconsumedUnits / factor;
    }

    private static double GetOffsetUnitsPerPixel(Session session)
    {
        if (session.Content is ILogicalScrollable logical &&
            logical.IsLogicalScrollEnabled &&
            logical.Viewport.Height > 0 &&
            session.Presenter.Bounds.Height > 0)
        {
            return logical.Viewport.Height / session.Presenter.Bounds.Height;
        }

        return 1;
    }

    private bool BeginSession(ScrollViewer viewer, OverscrollEdge edge)
    {
        CancelAnimation();

        if (_session is not null &&
            (!ReferenceEquals(_session.Viewer, viewer) ||
             !ReferenceEquals(
                 _session.Content.RenderTransform,
                 _session.AppliedTransform)))
        {
            ResetSession(restoreTransform: ReferenceEquals(
                _session.Content.RenderTransform,
                _session.AppliedTransform));
        }

        if (_session is null)
        {
            var presenter = FindPresenter(viewer);
            var content = presenter?.Child;
            if (presenter is null || content is null ||
                presenter.Bounds.Height <= 0 ||
                TopLevel.GetTopLevel(content) is null)
            {
                return false;
            }

            var scale = new ScaleTransform(1, 1);
            var translation = new TranslateTransform();
            var group = new TransformGroup();
            var originalTransform = content.RenderTransform;

            if (originalTransform is Transform mutableTransform)
            {
                group.Children.Add(mutableTransform);
            }
            else if (originalTransform is not null)
            {
                group.Children.Add(new MatrixTransform(originalTransform.Value));
            }

            group.Children.Add(scale);
            group.Children.Add(translation);

            _session = new Session
            {
                Viewer = viewer,
                Presenter = presenter,
                Content = content,
                Edge = edge,
                OriginalTransform = originalTransform,
                OriginalOrigin = content.RenderTransformOrigin,
                AppliedTransform = group,
                Scale = scale,
                Translation = translation
            };

            content.SetCurrentValue(Visual.RenderTransformProperty, group);
        }

        EnsureFeedbackLayer();
        RefreshGlowBrushes();
        SwitchEdge(edge);
        _state = edge == OverscrollEdge.Top
            ? OverscrollState.PullingTop
            : OverscrollState.PullingBottom;
        return true;
    }

    private void SwitchEdge(OverscrollEdge edge)
    {
        if (_session is null)
        {
            return;
        }

        _session.Edge = edge;
        _session.Content.SetCurrentValue(
            Visual.RenderTransformOriginProperty,
            edge == OverscrollEdge.Top
                ? new RelativePoint(0.5, 0, RelativeUnit.Relative)
                : new RelativePoint(0.5, 1, RelativeUnit.Relative));
        _state = edge == OverscrollEdge.Top
            ? OverscrollState.PullingTop
            : OverscrollState.PullingBottom;
    }

    private void ApplyVisualPull()
    {
        if (_session is null)
        {
            return;
        }

        var viewportHeight = GetViewportHeight(_session);
        var translation = MobileOverscrollPhysics.GetTranslation(
            _rawPullDistance,
            viewportHeight);
        var direction = _session.Edge == OverscrollEdge.Top ? 1 : -1;

        _session.Translation.Y = translation * direction;
        _session.Scale.ScaleY = MobileOverscrollPhysics.GetScale(
            translation,
            viewportHeight);
        _session.Scale.ScaleX = 1;
        UpdateGlow(Math.Abs(_session.Translation.Y));
    }

    private void EnsureFeedbackLayer()
    {
        if (_host.Content is not Grid rootGrid)
        {
            DetachFeedbackLayer();
            return;
        }

        if (ReferenceEquals(_feedbackRoot, rootGrid) &&
            _feedbackLayer is not null &&
            rootGrid.Children.Contains(_feedbackLayer))
        {
            return;
        }

        DetachFeedbackLayer();

        _feedbackRoot = rootGrid;
        _topGlow = CreateGlowBorder(isTop: true);
        _bottomGlow = CreateGlowBorder(isTop: false);
        _feedbackLayer = new Canvas
        {
            Name = "ScrollEdgeGlowLayer",
            IsVisible = false,
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            ZIndex = MobileZIndex.ShellEdgeFeedback
        };
        _feedbackLayer.Children.Add(_topGlow);
        _feedbackLayer.Children.Add(_bottomGlow);

        Grid.SetRowSpan(
            _feedbackLayer,
            Math.Max(1, rootGrid.RowDefinitions.Count));
        Grid.SetColumnSpan(
            _feedbackLayer,
            Math.Max(1, rootGrid.ColumnDefinitions.Count));
        rootGrid.Children.Add(_feedbackLayer);
    }

    private Border CreateGlowBorder(bool isTop)
        => new()
        {
            IsVisible = false,
            IsHitTestVisible = false,
            Opacity = 0,
            Background = CreateGlowBrush(isTop),
            // Keep the geometry stable during the gesture. The spring only
            // changes ScaleY, so glow feedback does not invalidate layout on
            // every pointer/animation frame.
            RenderTransform = new ScaleTransform(1, 0),
            RenderTransformOrigin = isTop
                ? new RelativePoint(0.5, 0, RelativeUnit.Relative)
                : new RelativePoint(0.5, 1, RelativeUnit.Relative)
        };

    private LinearGradientBrush CreateGlowBrush(bool isTop)
    {
        var accent = ResolveAccentColor();
        var transparent = Color.FromArgb(0, accent.R, accent.G, accent.B);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = isTop
                ?
                [
                    new GradientStop(accent, 0),
                    new GradientStop(transparent, 1)
                ]
                :
                [
                    new GradientStop(transparent, 0),
                    new GradientStop(accent, 1)
                ]
        };
    }

    private Color ResolveAccentColor()
    {
        if (_host.TryFindResource(
                "AccentBrush",
                _host.ActualThemeVariant,
                out var resource) &&
            resource is ISolidColorBrush solidBrush)
        {
            return solidBrush.Color;
        }

        return Color.FromRgb(139, 92, 246);
    }

    private void RefreshGlowBrushes()
    {
        if (_topGlow is not null)
        {
            _topGlow.Background = CreateGlowBrush(isTop: true);
        }

        if (_bottomGlow is not null)
        {
            _bottomGlow.Background = CreateGlowBrush(isTop: false);
        }
    }

    private void UpdateGlow(double translation)
    {
        if (_session is null || translation <= 0)
        {
            HideGlow();
            return;
        }

        EnsureFeedbackLayer();
        if (_feedbackRoot is null || _feedbackLayer is null ||
            _topGlow is null || _bottomGlow is null)
        {
            return;
        }

        var origin = _session.Viewer.TranslatePoint(default, _feedbackRoot);
        if (origin is not { } viewerOrigin)
        {
            HideGlow();
            return;
        }

        var rootWidth = _feedbackRoot.Bounds.Width;
        var rootHeight = _feedbackRoot.Bounds.Height;
        var viewerWidth = _session.Viewer.Bounds.Width;
        var viewerHeight = _session.Viewer.Bounds.Height;
        if (rootWidth <= 0 || rootHeight <= 0 ||
            viewerWidth <= 0 || viewerHeight <= 0)
        {
            HideGlow();
            return;
        }

        var left = Math.Clamp(viewerOrigin.X, 0, rootWidth);
        var top = Math.Clamp(viewerOrigin.Y, 0, rootHeight);
        var right = Math.Clamp(viewerOrigin.X + viewerWidth, left, rootWidth);
        var bottom = Math.Clamp(viewerOrigin.Y + viewerHeight, top, rootHeight);
        if (right <= left || bottom <= top)
        {
            HideGlow();
            return;
        }

        var viewportHeight = GetViewportHeight(_session);
        var depth = Math.Min(
            MobileOverscrollPhysics.GetGlowDepth(translation, viewportHeight),
            bottom - top);
        var baseDepth = Math.Min(
            MobileOverscrollPhysics.MaxGlowDepth,
            bottom - top);
        var opacity = MobileOverscrollPhysics.GetGlowOpacity(
            translation,
            viewportHeight);
        if (depth <= 0 || baseDepth <= 0 || opacity <= 0)
        {
            HideGlow();
            return;
        }

        var activeGlow = _session.Edge == OverscrollEdge.Top
            ? _topGlow
            : _bottomGlow;
        var inactiveGlow = _session.Edge == OverscrollEdge.Top
            ? _bottomGlow
            : _topGlow;

        SetGlowGeometry(
            activeGlow,
            left,
            _session.Edge == OverscrollEdge.Top
                ? top
                : bottom - baseDepth,
            right - left,
            baseDepth);

        if (activeGlow.RenderTransform is not ScaleTransform glowScale)
        {
            glowScale = new ScaleTransform(1, 0);
            activeGlow.RenderTransform = glowScale;
        }

        glowScale.ScaleY = Math.Clamp(depth / baseDepth, 0, 1);
        activeGlow.Opacity = opacity;
        activeGlow.IsVisible = true;
        HideGlow(inactiveGlow);
        _feedbackLayer.IsVisible = true;
    }

    private static void SetGlowGeometry(
        Border glow,
        double left,
        double top,
        double width,
        double height)
    {
        // Width, height and Canvas coordinates are layout properties. They
        // are only assigned when the active viewer geometry actually changes
        // (orientation, navigation or a resize), never for pull distance.
        if (MobileOverscrollPhysics.NeedsGlowGeometryUpdate(glow.Width, width))
        {
            glow.Width = width;
        }

        if (MobileOverscrollPhysics.NeedsGlowGeometryUpdate(glow.Height, height))
        {
            glow.Height = height;
        }

        if (MobileOverscrollPhysics.NeedsGlowGeometryUpdate(
                Canvas.GetLeft(glow),
                left))
        {
            Canvas.SetLeft(glow, left);
        }

        if (MobileOverscrollPhysics.NeedsGlowGeometryUpdate(
                Canvas.GetTop(glow),
                top))
        {
            Canvas.SetTop(glow, top);
        }
    }

    private void HideGlow()
    {
        HideGlow(_topGlow);
        HideGlow(_bottomGlow);
        if (_feedbackLayer is not null)
        {
            _feedbackLayer.IsVisible = false;
        }
    }

    private static void HideGlow(Border? glow)
    {
        if (glow is null)
        {
            return;
        }

        if (glow.RenderTransform is ScaleTransform glowScale)
        {
            glowScale.ScaleY = 0;
        }

        glow.Opacity = 0;
        glow.IsVisible = false;
    }

    private void DetachFeedbackLayer()
    {
        HideGlow();
        if (_feedbackRoot is not null && _feedbackLayer is not null)
        {
            _feedbackRoot.Children.Remove(_feedbackLayer);
        }

        _feedbackRoot = null;
        _feedbackLayer = null;
        _topGlow = null;
        _bottomGlow = null;
    }

    private void BeginRelease(TimeSpan duration)
    {
        if (_session is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(_host);
        if (topLevel is null)
        {
            ResetSession(restoreTransform: true);
            return;
        }

        _state = OverscrollState.Releasing;
        var version = ++_animationVersion;
        var startTranslation = _session.Translation.Y;
        var startScale = _session.Scale.ScaleY;
        TimeSpan? startedAt = null;

        void Frame(TimeSpan timestamp)
        {
            if (version != _animationVersion || _session is null)
            {
                return;
            }

            startedAt ??= timestamp;
            var elapsed = timestamp - startedAt.Value;
            var progress = duration.TotalMilliseconds <= 0
                ? 1
                : Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var remaining = MobileOverscrollPhysics.GetSpringRemaining(progress);

            _session.Translation.Y = startTranslation * remaining;
            _session.Scale.ScaleY = 1 + ((startScale - 1) * remaining);
            UpdateGlow(Math.Abs(_session.Translation.Y));

            if (progress >= 1 ||
                (Math.Abs(_session.Translation.Y) < 0.05 &&
                 Math.Abs(_session.Scale.ScaleY - 1) < 0.0005))
            {
                ResetSession(restoreTransform: true);
                return;
            }

            topLevel.RequestAnimationFrame(Frame);
        }

        topLevel.RequestAnimationFrame(Frame);
    }

    private void CancelAnimation()
        => _animationVersion++;

    private void ResetSession(bool restoreTransform)
    {
        CancelAnimation();

        if (_session is not null)
        {
            _session.Translation.Y = 0;
            _session.Scale.ScaleX = 1;
            _session.Scale.ScaleY = 1;

            if (restoreTransform && ReferenceEquals(
                    _session.Content.RenderTransform,
                    _session.AppliedTransform))
            {
                _session.Content.SetCurrentValue(
                    Visual.RenderTransformProperty,
                    _session.OriginalTransform);
                _session.Content.SetCurrentValue(
                    Visual.RenderTransformOriginProperty,
                    _session.OriginalOrigin);
            }
        }

        _session = null;
        _rawPullDistance = 0;
        HideGlow();
        ResetEdgeProbe();
        _state = OverscrollState.Idle;
    }

    private void CapturePointer(IPointer pointer)
    {
        if (_ownsPointerCapture)
        {
            return;
        }

        _suppressCaptureLost = true;
        try
        {
            pointer.Capture(_host);
            _ownsPointerCapture = ReferenceEquals(pointer.Captured, _host);
        }
        finally
        {
            _suppressCaptureLost = false;
        }
    }

    private void ReleasePointerCapture()
    {
        if (!_ownsPointerCapture || _pointer is null)
        {
            return;
        }

        _suppressCaptureLost = true;
        try
        {
            _ownsPointerCapture = false;
            _pointer.Capture(null);
        }
        finally
        {
            _suppressCaptureLost = false;
        }
    }

    private void ResetPointerTracking()
    {
        _pointer = null;
        _pressedSource = null;
        ResetEdgeProbe();
        _activationDistance = MobileOverscrollPhysics.FallbackActivationDistance;
        _verticalAxisLocked = false;
        _verticalGestureRejected = false;
        _pressPosition = default;
        _lastPosition = default;
    }

    private void ResetEdgeProbe()
    {
        _edgeProbeDistance = 0;
        _edgeProbeViewer = null;
        _edgeProbeEdge = null;
    }

    private double GetActivationDistance(PointerType pointerType)
    {
        var tapSize = GetTapSize(pointerType);

        return Math.Clamp(
            (tapSize?.Height ?? (MobileOverscrollPhysics.FallbackActivationDistance * 2)) / 2,
            MobileOverscrollPhysics.FallbackActivationDistance,
            12);
    }

    private Size? GetTapSize(PointerType pointerType)
    {
        // Avalonia 12 exposes platform tap metrics differently across targets.
        // Use the platform method when present and keep a conservative fallback
        // for targets that do not expose PlatformSettings publicly.
        var topLevel = TopLevel.GetTopLevel(_host);
        var settings = typeof(TopLevel)
            .GetProperty("PlatformSettings")?
            .GetValue(topLevel);
        var method = settings?.GetType().GetMethod(
            "GetTapSize",
            new[] { typeof(PointerType) });
        var value = method?.Invoke(settings, new object[] { pointerType });

        return value is Size size ? size : null;
    }

    private static bool IsTouchOrPen(IPointer pointer)
        => pointer.IsPrimary &&
           pointer.Type is PointerType.Touch or PointerType.Pen;

    private static bool IsExcludedGestureSource(Visual? source)
    {
        if (source is null)
        {
            return true;
        }

        // Do not steal direct manipulation from seek bars, sliders, scrollbars,
        // resize thumbs, or text editing controls. Buttons remain eligible so
        // a normal mobile drag can still turn into page scrolling.
        return source
            .GetSelfAndVisualAncestors()
            .Any(v => v is TextBox or Slider or ScrollBar or Thumb);
    }

    private static bool IsInsideViewer(Visual? source, ScrollViewer viewer)
        => source is not null &&
           (ReferenceEquals(source, viewer) || viewer.IsVisualAncestorOf(source));

    private static void CancelCardLongPress(Visual? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var card in source
                     .GetSelfAndVisualAncestors()
                     .OfType<MobilePressableCard>())
        {
            card.CancelLongPressForScroll();
        }
    }

    private static (ScrollViewer Viewer, OverscrollEdge Edge)? ResolveOverscrollTarget(
        Visual? source,
        double intendedOffsetDelta)
    {
        if (source is null || Math.Abs(intendedOffsetDelta) < double.Epsilon)
        {
            return null;
        }

        (ScrollViewer Viewer, OverscrollEdge Edge)? candidate = null;
        foreach (var viewer in EnumerateScrollViewers(source))
        {
            if (!IsEligible(viewer))
            {
                continue;
            }

            var maximum = GetMaximumOffset(viewer);
            var movingTowardTop = intendedOffsetDelta < 0;
            var canConsume = movingTowardTop
                ? viewer.Offset.Y > MobileOverscrollPhysics.EdgeTolerance
                : viewer.Offset.Y < maximum - MobileOverscrollPhysics.EdgeTolerance;

            if (canConsume)
            {
                return null;
            }

            candidate = (
                viewer,
                movingTowardTop ? OverscrollEdge.Top : OverscrollEdge.Bottom);

            if (!viewer.IsScrollChainingEnabled)
            {
                return candidate;
            }
        }

        return candidate;
    }

    private static IEnumerable<ScrollViewer> EnumerateScrollViewers(Visual source)
    {
        if (source is ScrollViewer ownViewer)
        {
            yield return ownViewer;
        }

        foreach (var viewer in source.GetVisualAncestors().OfType<ScrollViewer>())
        {
            yield return viewer;
        }
    }

    private static bool IsEligible(ScrollViewer viewer)
    {
        if (!viewer.IsEffectivelyVisible || !MobileOverscroll.GetIsEnabled(viewer) ||
            viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ||
            viewer.Viewport.Height <= 0 ||
            GetMaximumOffset(viewer) <= MobileOverscrollPhysics.EdgeTolerance)
        {
            return false;
        }

        // Internal TextBox scrolling must preserve text selection/caret behavior.
        return !viewer.GetVisualAncestors().OfType<TextBox>().Any();
    }

    private static ScrollContentPresenter? FindPresenter(ScrollViewer viewer)
        => viewer.GetVisualDescendants()
            .OfType<ScrollContentPresenter>()
            .FirstOrDefault(p => ReferenceEquals(
                p.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(),
                viewer));

    private static double GetMaximumOffset(ScrollViewer viewer)
        => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

    private static double GetViewportHeight(Session session)
        => Math.Max(1, session.Presenter.Bounds.Height > 0
            ? session.Presenter.Bounds.Height
            : session.Viewer.Bounds.Height);

}
