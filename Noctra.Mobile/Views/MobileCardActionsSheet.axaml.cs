using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Noctra.Mobile.Controls;

namespace Noctra.Mobile.Views;

public sealed class MobileCardActionSheetItem
{
    public MobileCardActionSheetItem(
        MobileCardActionKind action,
        string label,
        MaterialIconKind icon,
        bool isDestructive)
    {
        Action = action;
        Label = label;
        Icon = icon;
        IsDestructive = isDestructive;
    }

    public MobileCardActionKind Action { get; }

    public string Label { get; }

    public MaterialIconKind Icon { get; }

    public bool IsDestructive { get; }
}

public partial class MobileCardActionsSheet : UserControl
{
    private const double DismissDragThresholdRatio = 0.25;
    private const double MinimumDismissDragDistance = 112;
    private const int SnapAnimationDurationMs = 180;
    private const int DismissAnimationDurationMs = 160;

    private Action<MobileCardActionSheetItem>? _selectionAction;
    private bool _isDragging;
    private double _dragStartY;
    private double _dragOffsetY;
    private int _animationVersion;
    private TranslateTransform? _sheetTranslation;
    private Thickness _safeArea;

    public MobileCardActionsSheet()
    {
        InitializeComponent();
        _sheetTranslation = SheetSurface.RenderTransform as TranslateTransform;
        DataContext = this;
    }

    public ObservableCollection<MobileCardActionSheetItem> Actions { get; } = new();

    public void Show(
        string title,
        IEnumerable<MobileCardActionSheetItem> actions,
        Action<MobileCardActionSheetItem> selectionAction)
    {
        StopSheetAnimation();
        ResetDragVisuals();
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
        ApplySafeArea(_safeArea);
        IsVisible = true;

        Dispatcher.UIThread.Post(
            FocusFirstAction,
            DispatcherPriority.Background);
    }

    public void ApplySafeArea(Thickness safeArea)
    {
        _safeArea = safeArea;
        SheetSurface.Padding = new Thickness(
            12,
            8,
            12,
            Math.Max(8, safeArea.Bottom + 8));
    }

    public bool TryClose()
    {
        if (!IsVisible)
        {
            return false;
        }

        Close();
        return true;
    }

    private void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MobileCardActionSheetItem action })
        {
            return;
        }

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
            e.Handled = true;
        }
    }

    private void Sheet_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Close();
        e.Handled = true;
    }

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        StopSheetAnimation();
        _isDragging = true;
        _dragStartY = e.GetCurrentPoint(this).Position.Y;
        _dragOffsetY = Math.Max(0, SheetTranslation.Y);
        e.Pointer.Capture(DragHandle);
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var deltaY = e.GetCurrentPoint(this).Position.Y - _dragStartY;
        SetSheetDragProgress(_dragOffsetY + deltaY);
        e.Handled = true;
    }

    private void DragHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);
        _ = CompleteDragAsync();
        e.Handled = true;
    }

    private void DragHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        _ = AnimateSheetToAsync(0, 1, SnapAnimationDurationMs);
    }

    private async Task CompleteDragAsync()
    {
        if (SheetTranslation.Y >= GetDismissDragDistance())
        {
            var dismissOffset = Math.Max(SheetSurface.Bounds.Height + 24, SheetTranslation.Y);
            if (await AnimateSheetToAsync(dismissOffset, 0, DismissAnimationDurationMs))
            {
                Close();
            }

            return;
        }

        await AnimateSheetToAsync(0, 1, SnapAnimationDurationMs);
    }

    private void SetSheetDragProgress(double offset)
    {
        var maximumOffset = Math.Max(GetDismissDragDistance(), SheetSurface.Bounds.Height + 24);
        var clampedOffset = Math.Clamp(offset, 0, maximumOffset);
        var progress = Math.Clamp(clampedOffset / GetDismissDragDistance(), 0, 1);
        SetSheetVisuals(
            clampedOffset,
            1 - (0.65 * progress));
    }

    private async Task<bool> AnimateSheetToAsync(
        double targetOffset,
        double targetScrimOpacity,
        int durationMs)
    {
        var animationVersion = ++_animationVersion;
        var initialOffset = SheetTranslation.Y;
        var initialScrimOpacity = ScrimLayer.Opacity;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < durationMs)
        {
            if (animationVersion != _animationVersion || !IsVisible)
            {
                return false;
            }

            var progress = stopwatch.ElapsedMilliseconds / (double)durationMs;
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            SetSheetVisuals(
                Lerp(initialOffset, targetOffset, easedProgress),
                Lerp(initialScrimOpacity, targetScrimOpacity, easedProgress));
            await Task.Delay(8);
        }

        if (animationVersion != _animationVersion || !IsVisible)
        {
            return false;
        }

        SetSheetVisuals(targetOffset, targetScrimOpacity);
        return true;
    }

    private void FocusFirstAction()
    {
        if (!IsVisible)
        {
            return;
        }

        this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("CardActionRow"))
            ?.Focus();
    }

    private double GetDismissDragDistance()
        => Math.Max(MinimumDismissDragDistance, Bounds.Height * DismissDragThresholdRatio);

    private void SetSheetVisuals(double offset, double scrimOpacity)
    {
        SheetTranslation.Y = offset;
        ScrimLayer.Opacity = scrimOpacity;
    }

    private void StopSheetAnimation() => _animationVersion++;

    private void ResetDragVisuals()
    {
        _isDragging = false;
        _dragOffsetY = 0;
        SetSheetVisuals(0, 1);
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);

    private TranslateTransform SheetTranslation
        => _sheetTranslation ??= SheetSurface.RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException(
                "The card action sheet requires a translate transform.");

    private void Close()
    {
        StopSheetAnimation();
        ResetDragVisuals();
        IsVisible = false;
        TitleTextBlock.Text = string.Empty;
        Actions.Clear();
        _selectionAction = null;
    }
}
