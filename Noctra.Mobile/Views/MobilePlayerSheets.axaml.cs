using System;
using System.ComponentModel;
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
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Services;
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
    private PlayerViewModel? _boundViewModel;
    private MobileInputModeService? _inputModeService;
    private IInputElement? _focusBeforeOpen;
    private bool _wasOpen;

    public MobilePlayerSheets()
    {
        InitializeComponent();
        _sheetTranslation = SheetSurface.RenderTransform as TranslateTransform;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => AttachInputModeService();
        DetachedFromVisualTree += (_, _) => DetachInputModeService();
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private PlayerViewModel? ViewModel => DataContext as PlayerViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _boundViewModel = ViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        SyncSheetFocusState();
    }

    private void AttachInputModeService()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _boundViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        if (_inputModeService is null && Application.Current is App { Services: not null } app)
        {
            _inputModeService = app.Services.GetService<MobileInputModeService>();
        }

        if (_inputModeService is not null)
        {
            _inputModeService.ModeChanged -= InputModeService_ModeChanged;
            _inputModeService.ModeChanged += InputModeService_ModeChanged;
        }
    }

    private void DetachInputModeService()
    {
        if (_inputModeService is not null)
        {
            _inputModeService.ModeChanged -= InputModeService_ModeChanged;
        }

        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    private void InputModeService_ModeChanged(object? sender, MobileInputMode mode)
    {
        if (mode == MobileInputMode.Remote && _wasOpen)
        {
            QueuePreferredFocus();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.ActiveMobilePanelState)
            or nameof(PlayerViewModel.IsMobileDetailPanelOpen))
        {
            SyncSheetFocusState();
        }
    }

    private void SyncSheetFocusState()
    {
        var isOpen = _boundViewModel?.IsMobileDetailPanelOpen == true;
        if (isOpen && !_wasOpen)
        {
            _focusBeforeOpen = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }

        if (isOpen && _inputModeService?.IsRemote == true)
        {
            QueuePreferredFocus();
        }
        else if (!isOpen && _wasOpen)
        {
            RestorePreviousFocus();
        }

        _wasOpen = isOpen;
    }

    private void QueuePreferredFocus()
        => Dispatcher.UIThread.Post(FocusPreferredControl, DispatcherPriority.Input);

    private void FocusPreferredControl()
    {
        if (_boundViewModel?.IsMobileDetailPanelOpen != true || _inputModeService?.IsRemote != true)
        {
            return;
        }

        var buttons = SheetSurface.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.IsEnabled && button.Focusable)
            .ToList();

        var selectedButton = buttons.FirstOrDefault(button =>
            button.GetVisualDescendants()
                .OfType<MaterialIcon>()
                .Any(icon => icon.Kind == MaterialIconKind.RadioboxMarked && icon.IsEffectivelyVisible));

        (selectedButton ?? buttons.FirstOrDefault())?.Focus(NavigationMethod.Directional);
    }

    private void RestorePreviousFocus()
    {
        if (_focusBeforeOpen is Control { IsEffectivelyVisible: true, IsEnabled: true } control)
        {
            Dispatcher.UIThread.Post(
                () => control.Focus(NavigationMethod.Directional),
                DispatcherPriority.Input);
        }

        _focusBeforeOpen = null;
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
        => _inputModeService?.SetMode(MobileInputMode.Touch);

    private void PlayerSheets_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.None)
        {
            return;
        }

        _inputModeService?.SetMode(MobileInputMode.Remote);
        if (e.Key is not (Key.Escape or Key.Back))
        {
            return;
        }

        if (_boundViewModel?.BackFromPlayerPanelCommand.CanExecute(null) == true)
        {
            _boundViewModel.BackFromPlayerPanelCommand.Execute(null);
        }

        e.Handled = true;
    }

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
