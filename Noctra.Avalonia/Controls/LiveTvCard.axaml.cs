using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Models;

namespace Noctra.Avalonia.Controls;

public partial class LiveTvCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<LiveTvCard, bool>(nameof(ShowHistoryMenu));
    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<LiveTvCard, bool>(nameof(ShowRemoveFavoriteMenu));
    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<LiveTvCard, bool>(nameof(ShowRemoveMyListMenu));

    public bool ShowHistoryMenu { get => GetValue(ShowHistoryMenuProperty); set => SetValue(ShowHistoryMenuProperty, value); }
    public bool ShowRemoveFavoriteMenu { get => GetValue(ShowRemoveFavoriteMenuProperty); set => SetValue(ShowRemoveFavoriteMenuProperty, value); }
    public bool ShowRemoveMyListMenu { get => GetValue(ShowRemoveMyListMenuProperty); set => SetValue(ShowRemoveMyListMenuProperty, value); }

    public LiveTvCard() => InitializeComponent();

    internal void OnCardRightTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel)
        {
            DesktopCardActions.Raise(
                this,
                channel,
                DesktopCardGridKind.Live,
                ResolvePresentationMode());
            e.Handled = true;
        }
    }

    private DesktopCardPresentationMode ResolvePresentationMode()
    {
        if (ShowHistoryMenu)
            return DesktopCardPresentationMode.History;
        if (ShowRemoveFavoriteMenu)
            return DesktopCardPresentationMode.Favorites;
        if (ShowRemoveMyListMenu)
            return DesktopCardPresentationMode.MyList;
        return DesktopCardPresentationMode.Default;
    }
}
