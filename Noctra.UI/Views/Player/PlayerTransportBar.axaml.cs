using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.UI.Views.Player;

public partial class PlayerTransportBar : UserControl
{
    public static readonly StyledProperty<bool> ShowPointerVolumeControlProperty =
        AvaloniaProperty.Register<PlayerTransportBar, bool>(nameof(ShowPointerVolumeControl));

    private const double ExpandedPointerVolumeWidth = 132d;
    private const int PointerWheelVolumeStep = 5;
    private static readonly TimeSpan PointerVolumeCloseDelay = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _pointerVolumeCloseTimer;

    public PlayerTransportBar()
    {
        InitializeComponent();

        _pointerVolumeCloseTimer = new DispatcherTimer
        {
            Interval = PointerVolumeCloseDelay
        };
        _pointerVolumeCloseTimer.Tick += (_, _) =>
        {
            _pointerVolumeCloseTimer.Stop();
            SetPointerVolumeOpen(false);
        };
    }

    public bool ShowPointerVolumeControl
    {
        get => GetValue(ShowPointerVolumeControlProperty);
        set => SetValue(ShowPointerVolumeControlProperty, value);
    }

    public void FocusPrimaryAction()
        => PlayPauseButton.Focus(NavigationMethod.Directional);

    private void VolumeCluster_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (!ShowPointerVolumeControl)
            return;

        _pointerVolumeCloseTimer.Stop();
        SetPointerVolumeOpen(true);
    }

    private void VolumeCluster_PointerExited(object? sender, PointerEventArgs e)
        => SchedulePointerVolumeClose();

    private void VolumeCluster_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (!ShowPointerVolumeControl)
            return;

        _pointerVolumeCloseTimer.Stop();
        SetPointerVolumeOpen(true);
    }

    private void VolumeCluster_LostFocus(object? sender, RoutedEventArgs e)
        => SchedulePointerVolumeClose();

    private void VolumeCluster_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!ShowPointerVolumeControl || DataContext is not PlayerViewModel vm)
            return;

        var direction = Math.Sign(e.Delta.Y);
        if (direction == 0)
            return;

        vm.Volume = Math.Clamp(vm.Volume + (direction * PointerWheelVolumeStep), 0, 100);
        vm.UserInteractionCommand.Execute(null);

        _pointerVolumeCloseTimer.Stop();
        SetPointerVolumeOpen(true);
        e.Handled = true;
    }

    private void SchedulePointerVolumeClose()
    {
        if (!ShowPointerVolumeControl)
        {
            SetPointerVolumeOpen(false);
            return;
        }

        _pointerVolumeCloseTimer.Stop();
        _pointerVolumeCloseTimer.Start();
    }

    private void SetPointerVolumeOpen(bool isOpen)
    {
        var shouldOpen = isOpen && ShowPointerVolumeControl;
        PointerVolumeReveal.Width = shouldOpen ? ExpandedPointerVolumeWidth : 0d;
        PointerVolumeReveal.Opacity = shouldOpen ? 1d : 0d;
        PointerVolumeReveal.IsHitTestVisible = shouldOpen;
    }
}
