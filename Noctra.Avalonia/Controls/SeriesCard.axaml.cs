using Avalonia;
using Avalonia.Controls;
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

    private void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series) ViewModel?.AddToMyListCommand.Execute(series);
    }

    private void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series) ViewModel?.ToggleFavoriteCommand.Execute(series);
    }

    private void Context_RemoveFromHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series) ViewModel?.RemoveFromHistoryCommand.Execute(series);
    }

    private void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series) ViewModel?.RemoveFromFavoritesCommand.Execute(series);
    }

    private void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series) ViewModel?.RemoveFromMyListCommand.Execute(series);
    }
}
