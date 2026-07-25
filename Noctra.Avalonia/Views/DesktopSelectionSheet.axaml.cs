using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Noctra.Avalonia.Views;

public sealed class DesktopSelectionOption
{
    public DesktopSelectionOption(object value, string label, bool isSelected, bool isLocked = false)
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

public partial class DesktopSelectionSheet : UserControl
{
    private const double DismissDragThresholdRatio = 0.22;
    private const double MinimumDismissDragDistance = 96;
    private const int SnapAnimationDurationMs = 150;
    private const int DismissAnimationDurationMs = 140;

    private Action<DesktopSelectionOption>? _selectionAction;
    private Action<DesktopSelectionOption>? _lockedAction;
    private bool _isDragging;
    private double _dragStartY;
    private double _dragOffsetY;
    private int _animationVersion;
    private TranslateTransform? _sheetTranslation;

    public DesktopSelectionSheet()
    {
        InitializeComponent();
        _sheetTranslation = SheetSurface.RenderTransform as TranslateTransform;
        DataContext = this;
    }

    public ObservableCollection<DesktopSelectionOption> Options { get; } = new();

    public void Show(
        string title,
        IEnumerable<DesktopSelectionOption> options,
        Action<DesktopSelectionOption> selectionAction,
        Action<DesktopSelectionOption>? lockedAction = null)
    {
        StopAnimation();
        ResetVisuals();
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
        if (sender is not Button { Tag: DesktopSelectionOption option })
        {
            return;
        }
        var action = option.IsLocked ? _lockedAction : _selectionAction;
        Close();
        action?.Invoke(option);
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } list)
        {
            list.SelectedIndex = -1;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

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
        {
            return;
        }
        var delta = e.GetCurrentPoint(this).Position.Y - _dragStartY;
        SetDragProgress(_dragOffsetY + delta);
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
            {
                return false;
            }
            var progress = stopwatch.ElapsedMilliseconds / (double)durationMs;
            var eased = 1 - Math.Pow(1 - progress, 3);
            SetVisuals(Lerp(initialOffset, targetOffset, eased), Lerp(initialOpacity, targetOpacity, eased));
            await Task.Delay(8);
        }
        if (version != _animationVersion || !IsVisible)
        {
            return false;
        }
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
            ?? throw new InvalidOperationException("DesktopSelectionSheet requires a TranslateTransform.");

    private void Close()
    {
        StopAnimation();
        ResetVisuals();
        IsVisible = false;
        _selectionAction = null;
        _lockedAction = null;
    }
}
