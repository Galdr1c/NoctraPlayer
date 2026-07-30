using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Models;

namespace Noctra.Avalonia.Controls;

public partial class ContinueWatchingCard : UserControl
{
    public ContinueWatchingCard() => InitializeComponent();

    internal void OnCardRightTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel)
        {
            DesktopCardActions.Raise(
                this,
                channel,
                DesktopCardGridKind.ContinueWatching,
                DesktopCardPresentationMode.Default);
            e.Handled = true;
        }
    }
}
