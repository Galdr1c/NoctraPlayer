using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Models;

namespace Noctra.Avalonia.Controls;

public partial class SeriesCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<SeriesCard, bool>(nameof(ShowHistoryMenu));
    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<SeriesCard, bool>(nameof(ShowRemoveFavoriteMenu));
    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<SeriesCard, bool>(nameof(ShowRemoveMyListMenu));

    public bool ShowHistoryMenu { get => GetValue(ShowHistoryMenuProperty); set => SetValue(ShowHistoryMenuProperty, value); }
    public bool ShowRemoveFavoriteMenu { get => GetValue(ShowRemoveFavoriteMenuProperty); set => SetValue(ShowRemoveFavoriteMenuProperty, value); }
    public bool ShowRemoveMyListMenu { get => GetValue(ShowRemoveMyListMenuProperty); set => SetValue(ShowRemoveMyListMenuProperty, value); }

    public SeriesCard() => InitializeComponent();

    internal void OnCardRightTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series)
        {
            DesktopCardActions.Raise(
                this,
                series,
                DesktopCardGridKind.Default,
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
