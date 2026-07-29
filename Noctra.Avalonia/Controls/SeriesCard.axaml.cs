using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.Models;
using Noctra.ViewModels;

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

    private MainViewModel? ViewModel
        => VisualRoot is Control root ? root.DataContext as MainViewModel : null;

    internal void OnCardRightTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series)
        {
            DesktopCardActions.Raise(
                this,
                series,
                DesktopCardGridKind.Default,
                DesktopCardPresentationMode.Default);
        }
    }

    internal void HandleAction(DesktopCardActionKind action)
    {
        if (DataContext is not Series series || ViewModel is null)
            return;

        switch (action)
        {
            case DesktopCardActionKind.AddToMyList:
                ViewModel.AddToMyListCommand.Execute(series);
                break;
            case DesktopCardActionKind.ToggleFavorite:
                ViewModel.ToggleFavoriteCommand.Execute(series);
                break;
            case DesktopCardActionKind.RemoveFromHistory:
                ViewModel.RemoveFromHistoryCommand.Execute(series);
                break;
            case DesktopCardActionKind.RemoveFromFavorites:
                ViewModel.RemoveFromFavoritesCommand.Execute(series);
                break;
            case DesktopCardActionKind.RemoveFromMyList:
                ViewModel.RemoveFromMyListCommand.Execute(series);
                break;
        }
    }
}
