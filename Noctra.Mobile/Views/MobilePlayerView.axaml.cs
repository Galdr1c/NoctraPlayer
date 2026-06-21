using System;
using Avalonia.Controls;
using Avalonia.Input;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerView : UserControl
{
    /// <summary>
    /// EPG timeline'dan kanal seçildiğinde tetiklenir.
    /// MainView bu event'e abone olup kanalı oynatır.
    /// </summary>
    public event Action<Channel>? ChannelSelected;

    public MobilePlayerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// EPG timeline kanal satırına tıklandığında çağrılır.
    /// Seçilen kanalı ChannelSelected event'i ile iletir, EPG panelini kapatır.
    /// </summary>
    private void EpgRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;

        if (sender is Control control && control.DataContext is EpgPanelRow row)
        {
            // EPG panelini kapat
            if (DataContext is PlayerViewModel playerVm)
            {
                playerVm.ToggleEpgPanelCommand.Execute(null);
            }

            // Kanal seçim event'ini fırlat
            ChannelSelected?.Invoke(row.Channel);
            e.Handled = true;
        }
    }

    private void OnLeftDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlayerViewModel playerVm && playerVm.SkipBackwardCommand.CanExecute("10"))
        {
            playerVm.SkipBackwardCommand.Execute("10");
        }
        e.Handled = true;
    }

    private void OnRightDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlayerViewModel playerVm && playerVm.SkipForwardCommand.CanExecute("10"))
        {
            playerVm.SkipForwardCommand.Execute("10");
        }
        e.Handled = true;
    }

    private void OnPlayerBackgroundTapped(object? sender, TappedEventArgs e)
    {
        // Find the bottom sheet and toggle its opacity or visibility for immersion
        // For now, we just handle the click to prevent passing it down if needed, 
        // or trigger a toggle overlay command if it existed.
    }
}
