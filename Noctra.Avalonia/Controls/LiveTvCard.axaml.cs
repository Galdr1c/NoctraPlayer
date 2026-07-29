using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.Models;
using Noctra.ViewModels;

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

    private MainViewModel? ViewModel
        => VisualRoot is Control root ? root.DataContext as MainViewModel : null;

    internal void OnCardRightTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel)
        {
            DesktopCardActions.Raise(
                this,
                channel,
                DesktopCardGridKind.Live,
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
