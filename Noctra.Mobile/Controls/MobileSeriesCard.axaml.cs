using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Controls;

public partial class MobileSeriesCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowHistoryMenu));

    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowRemoveFavoriteMenu));

    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowRemoveMyListMenu));

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public bool ShowRemoveFavoriteMenu
    {
        get => GetValue(ShowRemoveFavoriteMenuProperty);
        set => SetValue(ShowRemoveFavoriteMenuProperty, value);
    }

    public bool ShowRemoveMyListMenu
    {
        get => GetValue(ShowRemoveMyListMenuProperty);
        set => SetValue(ShowRemoveMyListMenuProperty, value);
    }

    public MobileSeriesCard()
    {
        InitializeComponent();
    }

    private void CardContainer_Tapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled ||
            DataContext is not Series series ||
            this.FindAncestorOfType<ItemsControl>()?.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectMediaCommand.CanExecute(series))
        {
            viewModel.SelectMediaCommand.Execute(series);
            e.Handled = true;
        }
    }
}
