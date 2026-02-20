using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;
using Noctra.Models;

namespace Noctra.Avalonia.Views;

public partial class MoviesView : UserControl
{
    public MoviesView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.SelectedGroup = null;
    }

    private async void MoviesView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        var scrollableHeight = System.Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (ViewModel != null) await ViewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
    }

    // Shared Context menu handlers
    private async void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.AddToMyListCommand.ExecuteAsync(media);
    }

    private async void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.ToggleFavoriteCommand.ExecuteAsync(media);
    }

    private async void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.RemoveFromMyListCommand.ExecuteAsync(media);
    }

    private async void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.RemoveFromFavoritesCommand.ExecuteAsync(media);
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
                if (parent.DataContext is Channel || parent.DataContext is Series) return parent.DataContext;
                parent = parent.Parent;
            }
        }
        return null;
    }
}


