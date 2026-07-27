using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerSheets : UserControl
{
    private const double DismissDragThresholdRatio = 0.25;
    private const double MinimumDismissDragDistance = 112;
    private const int SnapAnimationDurationMs = 180;
    private const int DismissAnimationDurationMs = 160;

    private bool _isDragging;
    private double _dragStartY;
    private double _dragOffsetY;
    private int _animationVersion;
    private TranslateTransform? _sheetTranslation;

    public MobilePlayerSheets()
    {
        InitializeComponent();
        _sheetTranslation = SheetSurface.RenderTransform as TranslateTransform;
    }

    private PlayerViewModel? ViewModel => DataContext as PlayerViewModel;

    private TranslateTransform SheetTranslation
        => _sheetTranslation ??= SheetSurface.RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("The sheet requires a translate transform.");

    private void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
        {
            CloseSheet();
        }
    }

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        StopSheetAnimation();
        _isDragging = true;
        _dragStartY = e.GetCurrentPoint(this).Position.Y;
        _dragOffsetY = Math.Max(0, SheetTranslation.Y);
        e.Pointer.Capture(DragHandle);
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var deltaY = e.GetCurrentPoint(this).Position.Y - _dragStartY;
        SetSheetDragProgress(_dragOffsetY + deltaY);
        e.Handled = true;
    }

    private void DragHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        e.Pointer.Capture(null);
        _ = CompleteDragAsync();
        e.Handled = true;
    }

    private void DragHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging) return;
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
                CloseSheet();
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
            if (animationVersion != _animationVersion) return false;

            var progress = stopwatch.ElapsedMilliseconds / (double)durationMs;
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            SetSheetVisuals(
                Lerp(initialOffset, targetOffset, easedProgress),
                Lerp(initialScrimOpacity, targetScrimOpacity, easedProgress));
            await Task.Delay(8);
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

    private void CloseSheet()
    {
        StopSheetAnimation();
        SetSheetVisuals(0, 1);
        ViewModel?.CloseAllPanels();
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);
}
