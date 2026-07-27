using System;
using Avalonia.Controls;
using Avalonia.Input;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views.Player;

public partial class MobilePlayerTimeline : UserControl
{
    private PlayerViewModel? _vm;
    private bool _isAttached;
    private bool _isDragging;

    public MobilePlayerTimeline()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        Tapped += (_, e) => e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Detach();
        Attach();
    }

    private void Attach()
    {
        if (_isAttached) return;
        _vm = DataContext as PlayerViewModel;
        if (_vm is null) return;

        _isAttached = true;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateProgress();
    }

    private void Detach()
    {
        if (!_isAttached) return;
        _isAttached = false;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Position)
            or nameof(PlayerViewModel.Duration)
            or nameof(PlayerViewModel.LiveProgramProgress)
            or nameof(PlayerViewModel.IsLiveContent))
        {
            UpdateProgress();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || _vm.IsLiveContent || _vm.Duration <= 0) return;

        _isDragging = true;
        e.Pointer.Capture(this);
        SeekToPoint(e.GetCurrentPoint(RootGrid).Position.X);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm is null) return;
        SeekToPoint(e.GetCurrentPoint(RootGrid).Position.X);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }

    private void SeekToPoint(double pointX)
    {
        var width = RootGrid.Bounds.Width;
        if (width <= 0) return;

        var progress = Math.Clamp(pointX / width, 0, 1);
        var targetPosition = progress * _vm!.Duration;

        if (_vm.SeekCommand.CanExecute(targetPosition))
            _vm.SeekCommand.Execute(targetPosition);
    }

    private void UpdateProgress()
    {
        if (_vm is null) return;

        var width = RootGrid.Bounds.Width - Thumb.Width;
        if (width <= 0) return;

        double progress;
        if (_vm.IsLiveContent)
        {
            progress = _vm.LiveProgramProgress / 100d;
        }
        else if (_vm.Duration > 0)
        {
            progress = Math.Clamp(_vm.Position / _vm.Duration, 0, 1);
        }
        else
        {
            progress = 0;
        }

        var fillWidth = width * progress;
        FillBar.Width = fillWidth;
        Thumb.Margin = new Avalonia.Thickness(fillWidth, 0, 0, 0);
    }
}
