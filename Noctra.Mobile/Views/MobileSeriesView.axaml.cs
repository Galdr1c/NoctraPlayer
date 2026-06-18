using Avalonia.Controls;

namespace Noctra.Mobile.Views;

public partial class MobileSeriesView : UserControl
{
    public MobileSeriesView()
    {
        InitializeComponent();
    }

    private async void SeriesScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer &&
            DataContext is Noctra.ViewModels.MainViewModel viewModel)
        {
            await viewModel.LoadMoreSeriesIfNeededAsync(
                scrollViewer.Offset.Y,
                scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        }
    }
}
