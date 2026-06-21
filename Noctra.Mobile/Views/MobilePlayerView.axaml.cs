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
}
