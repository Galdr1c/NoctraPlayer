using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views.Player;

public partial class MobilePlayerTimeline : UserControl
{
    private PlayerViewModel? _vm;
    private bool _isAttached;
    private bool _isDragging;
    private double _previewPosition;

    public MobilePlayerTimeline()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        KeyDown += OnKeyDown;
        SizeChanged += (_, _) => UpdateProgress();
        Tapped += (_, e) => e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsLiveContent || e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        var command = e.Key == Key.Left
            ? _vm.SkipBackwardCommand
            : _vm.SkipForwardCommand;

        if (command.CanExecute("10"))
        {
            command.Execute("10");
            _vm.ShowOverlayCommand.Execute(null);
        }

        e.Handled = true;
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
        if (_isAttached && _vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _isAttached = false;
        _isDragging = false;
        _vm?.ClearSeekPreview();
        _vm = null;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Position)
            or nameof(PlayerViewModel.Duration)
            or nameof(PlayerViewModel.BufferedPosition)
            or nameof(PlayerViewModel.LiveProgramProgress)
            or nameof(PlayerViewModel.IsLiveContent)
            or nameof(PlayerViewModel.IsDownloadedPlayback))
        {
            UpdateProgress();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // The whole timeline area must shield the underlying volume/brightness zones.
        e.Handled = true;

        if (_vm is null || _vm.IsLiveContent || _vm.Duration <= 0)
            return;

        _isDragging = true;
        _previewPosition = _vm.Position;

        if (_vm.StartSeekingCommand.CanExecute(null))
            _vm.StartSeekingCommand.Execute(null);

        e.Pointer.Capture(this);
        UpdatePreview(e.GetCurrentPoint(TrackGrid).Position.X);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm is null)
            return;

        e.Handled = true;
        UpdatePreview(e.GetCurrentPoint(TrackGrid).Position.X);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;

        if (!_isDragging || _vm is null)
            return;

        UpdatePreview(e.GetCurrentPoint(TrackGrid).Position.X);
        _isDragging = false;
        e.Pointer.Capture(null);
        CommitSeek();
        _vm.ClearSeekPreview();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        CommitSeek();
        _vm?.ClearSeekPreview();
    }

    private void UpdatePreview(double pointX)
    {
        if (_vm is null)
            return;

        var width = TrackGrid.Bounds.Width;
        if (width <= 0)
            return;

        var localX = Math.Clamp(pointX, 0, width);
        _previewPosition = (localX / width) * _vm.Duration;
        _vm.UpdateSeekPreview(_previewPosition);
        UpdateProgress();
    }

    private void CommitSeek()
    {
        if (_vm is null)
            return;

        var target = Math.Clamp(_previewPosition, 0, _vm.Duration);
        if (_vm.SeekCommand.CanExecute(target))
            _vm.SeekCommand.Execute(target);
    }

    private void UpdateProgress()
    {
        if (_vm is null)
            return;

        var trackWidth = TrackGrid.Bounds.Width;
        if (trackWidth <= 0)
            return;

        var displayedPosition = _isDragging
            ? _previewPosition
            : _vm.Position;

        double playedProgress;
        if (_vm.IsLiveContent)
        {
            playedProgress = _vm.LiveProgramProgress / 100d;
        }
        else if (_vm.Duration > 0)
        {
            playedProgress = displayedPosition / _vm.Duration;
        }
        else
        {
            playedProgress = 0;
        }

        playedProgress = Math.Clamp(playedProgress, 0, 1);

        var bufferedProgress = !_vm.IsLiveContent && _vm.Duration > 0
            ? _vm.BufferedPosition / _vm.Duration
            : 0;

        bufferedProgress = Math.Clamp(
            Math.Max(bufferedProgress, playedProgress),
            0,
            1);

        var playedWidth = trackWidth * playedProgress;
        var bufferedWidth = trackWidth * bufferedProgress;

        FillBar.Width = playedWidth;
        BufferBar.Width = bufferedWidth;
        BufferBar.IsVisible =
            !_vm.IsLiveContent &&
            !_vm.IsDownloadedPlayback &&
            bufferedWidth > playedWidth + 1;

        if (_vm.IsLiveContent)
        {
            Thumb.IsVisible = false;
            return;
        }

        Thumb.IsVisible = true;

        var thumbWidth = Thumb.Width;
        var thumbX = TrackGrid.Bounds.X + playedWidth - (thumbWidth / 2d);
        thumbX = Math.Clamp(
            thumbX,
            0,
            Math.Max(0, RootGrid.Bounds.Width - thumbWidth));

        Thumb.Margin = new Thickness(thumbX, 0, 0, 0);
    }
}
