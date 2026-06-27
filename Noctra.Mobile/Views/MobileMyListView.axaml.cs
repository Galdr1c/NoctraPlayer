using Avalonia.Controls;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileMyListView : UserControl
{
    public MobileMyListView()
    {
        InitializeComponent();
    }

    private async void MyListScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var scrollableHeight = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
        await viewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
        await viewModel.LoadMoreSeriesIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
    }
}
