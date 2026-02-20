using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;
using Noctra.Models;

namespace Noctra.Avalonia.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.SelectedGroup = null;
    }

    private void HomeView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var verticalOffset = scrollViewer.Offset.Y;
        HeroGrid.Opacity = System.Math.Max(0.2, 1.0 - (verticalOffset / 800.0));

        if (HeroGrid.RenderTransform is not global::Avalonia.Media.TransformGroup transforms)
        {
            return;
        }

        if (transforms.Children.Count > 0 && transforms.Children[0] is global::Avalonia.Media.TranslateTransform parallax)
        {
            parallax.Y = verticalOffset * 0.3;
        }

        if (transforms.Children.Count > 1 && transforms.Children[1] is global::Avalonia.Media.ScaleTransform zoom)
        {
            var zoomFactor = System.Math.Clamp(1.0 + (verticalOffset / 2500.0), 1.0, 1.16);
            zoom.ScaleX = zoomFactor;
            zoom.ScaleY = zoomFactor;
        }
    }

    
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
                if (parent is global::Avalonia.StyledElement styled && (styled.DataContext is Channel || styled.DataContext is Series)) return styled.DataContext;
                parent = parent.Parent;
            }
        }
        return null;
    }
}
