using Avalonia;
using Avalonia.Controls;

namespace Noctra.Mobile.Controls;

public partial class MobileLiveTvCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileLiveTvCard, bool>(nameof(ShowHistoryMenu));

    public static readonly StyledProperty<bool> ShowMyListMenuProperty =
        AvaloniaProperty.Register<MobileLiveTvCard, bool>(nameof(ShowMyListMenu));

    public static readonly StyledProperty<bool> ShowFavoriteMenuProperty =
        AvaloniaProperty.Register<MobileLiveTvCard, bool>(nameof(ShowFavoriteMenu), true);

    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<MobileLiveTvCard, bool>(nameof(ShowRemoveFavoriteMenu));

    public MobileLiveTvCard()
    {
        InitializeComponent();
    }

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public bool ShowMyListMenu
    {
        get => GetValue(ShowMyListMenuProperty);
        set => SetValue(ShowMyListMenuProperty, value);
    }

    public bool ShowFavoriteMenu
    {
        get => GetValue(ShowFavoriteMenuProperty);
        set => SetValue(ShowFavoriteMenuProperty, value);
    }

    public bool ShowRemoveFavoriteMenu
    {
        get => GetValue(ShowRemoveFavoriteMenuProperty);
        set => SetValue(ShowRemoveFavoriteMenuProperty, value);
    }
}
