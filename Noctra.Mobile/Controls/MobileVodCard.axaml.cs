using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Controls;

public partial class MobileVodCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowHistoryMenu));

    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowRemoveFavoriteMenu));

    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowRemoveMyListMenu));

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

    public MobileVodCard()
    {
        InitializeComponent();
    }

    private void CardContainer_Tapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled ||
            DataContext is not Channel channel ||
            this.FindAncestorOfType<ItemsControl>()?.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectMediaCommand.CanExecute(channel))
        {
            viewModel.SelectMediaCommand.Execute(channel);
            e.Handled = true;
        }
    }

    private void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel media && this.FindAncestorOfType<ItemsControl>()?.DataContext is MainViewModel vm)
        {
            if (vm.AddToMyListCommand.CanExecute(media))
            {
                vm.AddToMyListCommand.Execute(media);
            }
        }
    }

    private void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel media && this.FindAncestorOfType<ItemsControl>()?.DataContext is MainViewModel vm)
        {
            if (vm.RemoveFromMyListCommand.CanExecute(media))
            {
                vm.RemoveFromMyListCommand.Execute(media);
            }
        }
    }

    private void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel media && this.FindAncestorOfType<ItemsControl>()?.DataContext is MainViewModel vm)
        {
            if (vm.ToggleFavoriteCommand.CanExecute(media))
            {
                vm.ToggleFavoriteCommand.Execute(media);
            }
        }
    }

    private void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel media && this.FindAncestorOfType<ItemsControl>()?.DataContext is MainViewModel vm)
        {
            if (vm.RemoveFromFavoritesCommand.CanExecute(media))
            {
                vm.RemoveFromFavoritesCommand.Execute(media);
            }
        }
    }

    private void Context_RemoveFromHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel media && this.FindAncestorOfType<ItemsControl>()?.DataContext is MainViewModel vm)
        {
            if (vm.RemoveFromHistoryCommand.CanExecute(media))
            {
                vm.RemoveFromHistoryCommand.Execute(media);
            }
        }
    }
}
