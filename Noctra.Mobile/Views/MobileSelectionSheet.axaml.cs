using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Noctra.Mobile.Views;

public sealed class MobileSelectionOption
{
    public MobileSelectionOption(object value, string label, bool isSelected, bool isLocked = false)
    {
        Value = value;
        Label = label;
        IsSelected = isSelected;
        IsLocked = isLocked;
    }

    public object Value { get; }

    public string Label { get; }

    public bool IsSelected { get; }

    public bool IsLocked { get; }

    public bool ShowSelectionMark => IsSelected && !IsLocked;
}

public partial class MobileSelectionSheet : UserControl
{
    private const double DismissDragThresholdRatio = 0.25;
    private const double MinimumDismissDragDistance = 112;
    private const int SnapAnimationDurationMs = 180;
    private const int DismissAnimationDurationMs = 160;

    private Action<MobileSelectionOption>? _selectionAction;
    private Action<MobileSelectionOption>? _lockedAction;
    private bool _isDragging;
    private double _dragStartY;
    private double _dragOffsetY;
    private int _animationVersion;
    private TranslateTransform? _sheetTranslation;

    public MobileSelectionSheet()
    {
        InitializeComponent();
        _sheetTranslation = SheetSurface.RenderTransform as TranslateTransform;
        DataContext = this;
    }

    public ObservableCollection<MobileSelectionOption> Options { get; } = new();

    public void Show(
        string title,
        IEnumerable<MobileSelectionOption> options,
        Action<MobileSelectionOption> selectionAction,
        Action<MobileSelectionOption>? lockedAction = null)
    {
        StopSheetAnimation();
        ResetDragVisuals();
        TitleTextBlock.Text = title;
        Options.Clear();
        foreach (var option in options)
        {
            Options.Add(option);
        }

        _selectionAction = selectionAction;
        _lockedAction = lockedAction;
        IsVisible = true;
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

    private void Option_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MobileSelectionOption option })
        {
            return;
        }

        var action = option.IsLocked ? _lockedAction : _selectionAction;
        Close();
        action?.Invoke(option);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
        {
            Close();
        }
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

    private double GetDismissDragDistance()
        => Math.Max(MinimumDismissDragDistance, Bounds.Height * DismissDragThresholdRatio);

    private void SetSheetVisuals(double offset, double scrimOpacity)
    {
        SheetTranslation.Y = offset;
        ScrimLayer.Opacity = scrimOpacity;
    }

    private void StopSheetAnimation()
    {
        _animationVersion++;
    }

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
            ?? throw new InvalidOperationException("The selection sheet requires a translate transform.");

    private void Close()
    {
        StopSheetAnimation();
        ResetDragVisuals();
        IsVisible = false;
        _selectionAction = null;
        _lockedAction = null;
    }
}
