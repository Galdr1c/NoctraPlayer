using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;
using Noctra.Models;

namespace Noctra.Avalonia.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.SelectedGroup = null;
    }

    private void HistoryView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Logic will be moved here
    }

    
    private async void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem) return;
            var media = ResolveContextMedia(menuItem);
            if (media != null && ViewModel != null) await ViewModel.AddToMyListCommand.ExecuteAsync(media);
        }
        catch (Exception ex)
        {
            if (ViewModel != null) ViewModel.StatusMessage = $"Hata: {ex.Message}";
        }
    }

    private async void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem) return;
            var media = ResolveContextMedia(menuItem);
            if (media != null && ViewModel != null) await ViewModel.ToggleFavoriteCommand.ExecuteAsync(media);
        }
        catch (Exception ex)
        {
            if (ViewModel != null) ViewModel.StatusMessage = $"Hata: {ex.Message}";
        }
    }

    private async void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem) return;
            var media = ResolveContextMedia(menuItem);
            if (media != null && ViewModel != null) await ViewModel.RemoveFromMyListCommand.ExecuteAsync(media);
        }
        catch (Exception ex)
        {
            if (ViewModel != null) ViewModel.StatusMessage = $"Hata: {ex.Message}";
        }
    }

    private async void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem) return;
            var media = ResolveContextMedia(menuItem);
            if (media != null && ViewModel != null) await ViewModel.RemoveFromFavoritesCommand.ExecuteAsync(media);
        }
        catch (Exception ex)
        {
            if (ViewModel != null) ViewModel.StatusMessage = $"Hata: {ex.Message}";
        }
    }

    private static object? ResolveContextMedia(MenuItem menuItem)
    {
        if (menuItem.CommandParameter is Channel || menuItem.CommandParameter is Series) return menuItem.CommandParameter;
        if (menuItem.Tag is Channel || menuItem.Tag is Series) return menuItem.Tag;
        if (menuItem.DataContext is Channel || menuItem.DataContext is Series) return menuItem.DataContext;
        if (menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is global::Avalonia.StyledElement placementTarget &&
            (placementTarget.DataContext is Channel || placementTarget.DataContext is Series))
            return placementTarget.DataContext;
        if (menuItem.Parent is ContextMenu ownerMenu &&
            ownerMenu.PlacementTarget is Control placementControl)
        {
            var parent = placementControl.Parent;
            while (parent != null)
            {
                if (parent is global::Avalonia.StyledElement styled && (styled.DataContext is Channel || styled.DataContext is Series)) return styled.DataContext;
                parent = parent.Parent;
            }
        }
        return null;
    }
}
