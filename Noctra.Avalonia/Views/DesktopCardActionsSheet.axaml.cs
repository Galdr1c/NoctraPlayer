using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Noctra.Avalonia.Controls;

namespace Noctra.Avalonia.Views;

public sealed class DesktopCardActionSheetItem
{
    public DesktopCardActionSheetItem(
        DesktopCardActionKind action,
        string label,
        MaterialIconKind icon,
        bool isDestructive)
    {
        Action = action;
        Label = label;
        Icon = icon;
        IsDestructive = isDestructive;
    }

    public DesktopCardActionKind Action { get; }
    public string Label { get; }
    public MaterialIconKind Icon { get; }
    public bool IsDestructive { get; }
}

public partial class DesktopCardActionsSheet : UserControl
{
    private const double DismissDragThresholdRatio = 0.22;
    private const double MinimumDismissDragDistance = 96;
    private static readonly TimeSpan SnapAnimationDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DismissAnimationDuration = TimeSpan.FromMilliseconds(140);

    private Action<DesktopCardActionSheetItem>? _selectionAction;
    private bool _isDragging;
    private double _dragStartY;
    private double _dragOffsetY;
    private TranslateTransform? _sheetTranslation;
    private Control? _previousFocus;
    private CancellationTokenSource? _transitionCts;
    private int _presentationVersion;

    public DesktopCardActionsSheet()
    {
        InitializeComponent();
        _sheetTranslation = SheetSurface.RenderTransform as TranslateTransform;
        DataContext = this;
    }

    public ObservableCollection<DesktopCardActionSheetItem> Actions { get; } = new();

    public void Show(
        string title,
        IEnumerable<DesktopCardActionSheetItem> actions,
        Action<DesktopCardActionSheetItem> selectionAction)
    {
        CancelPendingTransition();
        var presentationVersion = Interlocked.Increment(ref _presentationVersion);

        if (!IsVisible)
        {
            _previousFocus = TopLevel.GetTopLevel(this)?
                .FocusManager?
                .GetFocusedElement() as Control;
        }

        ResetVisuals(useTransitions: false);
        TitleTextBlock.Text = title;
        Actions.Clear();
        foreach (var action in actions)
            Actions.Add(action);

        if (Actions.Count == 0)
        {
            Close();
            return;
        }

        _selectionAction = selectionAction;
        IsVisible = true;
        Focus();

        // Loaded priority is deterministic and is cancelled naturally by the
        // IsVisible check; no background Task/50 ms timing race is required.
        Dispatcher.UIThread.Post(
            () => FocusFirstAction(presentationVersion),
            DispatcherPriority.Loaded);
    }

    public bool TryClose()
    {
        if (!IsVisible)
            return false;

        Close();
        return true;
    }

    private void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DesktopCardActionSheetItem action })
            return;

        var selectionAction = _selectionAction;
        Close();
        selectionAction?.Invoke(action);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            Close();
    }

    private void Sheet_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsVisible)
            return;

        CancelPendingTransition();
        SetTransitions(null);
        _isDragging = true;
        _dragStartY = e.GetCurrentPoint(this).Position.Y;
        _dragOffsetY = Math.Max(0, SheetTranslation.Y);
        e.Pointer.Capture(DragHandle);
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
            return;

        var delta = e.GetCurrentPoint(this).Position.Y - _dragStartY;
        SetDragProgress(_dragOffsetY + delta);
        e.Handled = true;
    }

    private void DragHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
        _ = CompleteDragAsync();
        e.Handled = true;
    }

    private void DragHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        SnapBack();
    }

    private async Task CompleteDragAsync()
    {
        if (SheetTranslation.Y < GetDismissDistance())
        {
            SnapBack();
            return;
        }

        var target = Math.Max(SheetSurface.Bounds.Height + 24, SheetTranslation.Y);
        var cts = BeginTransition(DismissAnimationDuration);
        SetVisuals(target, 0);

        try
        {
            await Task.Delay(DismissAnimationDuration, cts.Token);
            if (!cts.IsCancellationRequested)
                Close();
        }
        catch (OperationCanceledException)
        {
            // A new Show/Close/drag superseded this transition.
        }
    }

    private void SnapBack()
    {
        BeginTransition(SnapAnimationDuration);
        SetVisuals(0, 1);
    }

    private void SetDragProgress(double offset)
    {
        var maximum = Math.Max(GetDismissDistance(), SheetSurface.Bounds.Height + 24);
        var clamped = Math.Clamp(offset, 0, maximum);
        var progress = Math.Clamp(clamped / GetDismissDistance(), 0, 1);
        SetVisuals(clamped, 1 - (0.65 * progress));
    }

    private double GetDismissDistance() =>
        Math.Max(MinimumDismissDragDistance, Bounds.Height * DismissDragThresholdRatio);

    private void SetVisuals(double offset, double opacity)
    {
        SheetTranslation.Y = offset;
        ScrimLayer.Opacity = opacity;
    }

    private CancellationTokenSource BeginTransition(TimeSpan duration)
    {
        CancelPendingTransition();
        var cts = new CancellationTokenSource();
        _transitionCts = cts;
        SetTransitions(duration);
        return cts;
    }

    private void SetTransitions(TimeSpan? duration)
    {
        if (duration is null)
        {
            SheetTranslation.Transitions = null;
            ScrimLayer.Transitions = null;
            return;
        }

        SheetTranslation.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = duration.Value,
                Easing = new CubicEaseOut()
            }
        };

        ScrimLayer.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration.Value,
                Easing = new CubicEaseOut()
            }
        };
    }

    private void CancelPendingTransition()
    {
        var cts = Interlocked.Exchange(ref _transitionCts, null);
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    private void ResetVisuals(bool useTransitions)
    {
        _isDragging = false;
        _dragOffsetY = 0;
        SetTransitions(useTransitions ? SnapAnimationDuration : null);
        SetVisuals(0, 1);
    }

    private TranslateTransform SheetTranslation =>
        _sheetTranslation ??= SheetSurface.RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException(
                "DesktopCardActionsSheet requires a TranslateTransform.");

    private void FocusFirstAction(int presentationVersion)
    {
        if (!IsVisible || presentationVersion != _presentationVersion)
            return;

        var firstButton = ActionsList.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault();
        firstButton?.Focus(NavigationMethod.Tab);
    }

    private void Close()
    {
        var presentationVersion = Interlocked.Increment(ref _presentationVersion);
        CancelPendingTransition();
        ResetVisuals(useTransitions: false);
        IsVisible = false;
        TitleTextBlock.Text = string.Empty;
        Actions.Clear();
        _selectionAction = null;

        var focusTarget = _previousFocus;
        _previousFocus = null;
        if (focusTarget is { Focusable: true, IsEffectivelyVisible: true })
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!IsVisible && presentationVersion == _presentationVersion)
                        focusTarget.Focus(NavigationMethod.Unspecified);
                },
                DispatcherPriority.Input);
        }
    }
}
