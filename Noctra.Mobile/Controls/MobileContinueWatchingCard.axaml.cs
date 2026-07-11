using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Controls;

public partial class MobileContinueWatchingCard : UserControl
{
    public MobileContinueWatchingCard()
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

}
