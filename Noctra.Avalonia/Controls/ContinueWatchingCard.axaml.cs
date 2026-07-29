using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Controls;

public partial class ContinueWatchingCard : UserControl
{
    public ContinueWatchingCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MainViewModel? ViewModel
        => VisualRoot is Control root ? root.DataContext as MainViewModel : null;

    internal void OnCardRightTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel)
        {
            DesktopCardActions.Raise(
                this,
                channel,
                DesktopCardGridKind.ContinueWatching,
                DesktopCardPresentationMode.Default);
        }
    }

    internal void HandleAction(DesktopCardActionKind action)
    {
        if (DataContext is not Channel channel || ViewModel is null)
            return;

        switch (action)
        {
            case DesktopCardActionKind.AddToMyList:
                ViewModel.AddToMyListCommand.Execute(channel);
                break;
            case DesktopCardActionKind.ToggleFavorite:
                ViewModel.ToggleFavoriteCommand.Execute(channel);
                break;
            case DesktopCardActionKind.RemoveFromHistory:
                ViewModel.RemoveFromHistoryCommand.Execute(channel);
                break;
            case DesktopCardActionKind.RemoveFromFavorites:
                ViewModel.RemoveFromFavoritesCommand.Execute(channel);
                break;
            case DesktopCardActionKind.RemoveFromMyList:
                ViewModel.RemoveFromMyListCommand.Execute(channel);
                break;
        }
    }
}
