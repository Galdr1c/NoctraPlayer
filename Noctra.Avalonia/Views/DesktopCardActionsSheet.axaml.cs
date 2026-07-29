using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    private const int SnapAnimationDurationMs = 150;
    private const int DismissAnimationDurationMs = 140;

    private Action<DesktopCardActionSheetItem>? _selectionAction;
    private bool _isDragging;
    private double _dragStartY;
    private double _dragOffsetY;
    private int _animationVersion;
    private TranslateTransform? _sheetTranslation;

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
        StopAnimation();
        ResetVisuals();
        TitleTextBlock.Text = title;
        Actions.Clear();
        foreach (var action in actions)
        {
            Actions.Add(action);
        }

        if (Actions.Count == 0)
        {
            Close();
            return;
        }

        _selectionAction = selectionAction;
        IsVisible = true;
        Focus();

        Task.Run(async () =>
        {
            await Task.Delay(50);
            await Dispatcher.InvokeAsync(FocusFirstAction);
        });
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
        {
            Close();
        }
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
        StopAnimation();
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
        _ = AnimateToAsync(0, 1, SnapAnimationDurationMs);
    }

    private async Task CompleteDragAsync()
    {
        if (SheetTranslation.Y >= GetDismissDistance())
        {
            var target = Math.Max(SheetSurface.Bounds.Height + 24, SheetTranslation.Y);
            if (await AnimateToAsync(target, 0, DismissAnimationDurationMs))
            {
                Close();
            }
            return;
        }
        await AnimateToAsync(0, 1, SnapAnimationDurationMs);
    }

    private void SetDragProgress(double offset)
    {
        var maximum = Math.Max(GetDismissDistance(), SheetSurface.Bounds.Height + 24);
        var clamped = Math.Clamp(offset, 0, maximum);
        var progress = Math.Clamp(clamped / GetDismissDistance(), 0, 1);
        SetVisuals(clamped, 1 - (0.65 * progress));
    }

    private async Task<bool> AnimateToAsync(double targetOffset, double targetOpacity, int durationMs)
    {
        var version = ++_animationVersion;
        var initialOffset = SheetTranslation.Y;
        var initialOpacity = ScrimLayer.Opacity;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < durationMs)
        {
            if (version != _animationVersion || !IsVisible)
                return false;
            var progress = stopwatch.ElapsedMilliseconds / (double)durationMs;
            var eased = 1 - Math.Pow(1 - progress, 3);
            SetVisuals(Lerp(initialOffset, targetOffset, eased), Lerp(initialOpacity, targetOpacity, eased));
            await Task.Delay(8);
        }
        if (version != _animationVersion || !IsVisible)
            return false;
        SetVisuals(targetOffset, targetOpacity);
        return true;
    }

    private double GetDismissDistance()
        => Math.Max(MinimumDismissDragDistance, Bounds.Height * DismissDragThresholdRatio);

    private void SetVisuals(double offset, double opacity)
    {
        SheetTranslation.Y = offset;
        ScrimLayer.Opacity = opacity;
    }

    private void StopAnimation() => _animationVersion++;

    private void ResetVisuals()
    {
        _isDragging = false;
        _dragOffsetY = 0;
        SetVisuals(0, 1);
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);

    private TranslateTransform SheetTranslation
        => _sheetTranslation ??= SheetSurface.RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("DesktopCardActionsSheet requires a TranslateTransform.");

    private void FocusFirstAction()
    {
        if (!IsVisible)
            return;

        var firstButton = ActionsList.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault();
        firstButton?.Focus();
    }

    private void Close()
    {
        StopAnimation();
        ResetVisuals();
        IsVisible = false;
        TitleTextBlock.Text = string.Empty;
        Actions.Clear();
        _selectionAction = null;
    }
}
