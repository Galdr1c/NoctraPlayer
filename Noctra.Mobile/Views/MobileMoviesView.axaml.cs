using Avalonia.Controls;

namespace Noctra.Mobile.Views;

public partial class MobileMoviesView : UserControl
{
    public MobileMoviesView()
    {
        InitializeComponent();
    }

    private async void MoviesScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer &&
            DataContext is Noctra.ViewModels.MainViewModel viewModel)
        {
            await viewModel.LoadMoreChannelsIfNeededAsync(
                scrollViewer.Offset.Y,
                scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        }
    }
}
