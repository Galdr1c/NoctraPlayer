using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Controls;

public partial class VodCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<VodCard, bool>(nameof(ShowHistoryMenu));
    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<VodCard, bool>(nameof(ShowRemoveFavoriteMenu));
    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<VodCard, bool>(nameof(ShowRemoveMyListMenu));

    public bool ShowHistoryMenu { get => GetValue(ShowHistoryMenuProperty); set => SetValue(ShowHistoryMenuProperty, value); }
    public bool ShowRemoveFavoriteMenu { get => GetValue(ShowRemoveFavoriteMenuProperty); set => SetValue(ShowRemoveFavoriteMenuProperty, value); }
    public bool ShowRemoveMyListMenu { get => GetValue(ShowRemoveMyListMenuProperty); set => SetValue(ShowRemoveMyListMenuProperty, value); }

    public VodCard() => InitializeComponent();

    private MainViewModel? ViewModel
        => VisualRoot is Control root ? root.DataContext as MainViewModel : null;

    private void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel) ViewModel?.AddToMyListCommand.Execute(channel);
    }

    private void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel) ViewModel?.ToggleFavoriteCommand.Execute(channel);
    }

    private void Context_RemoveFromHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel) ViewModel?.RemoveFromHistoryCommand.Execute(channel);
    }

    private void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel) ViewModel?.RemoveFromFavoritesCommand.Execute(channel);
    }

    private void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel) ViewModel?.RemoveFromMyListCommand.Execute(channel);
    }
}
