using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileLiveView : UserControl
{
    public MobileLiveView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.SelectedGroup = null;
        }
    }

    private async void LiveScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer &&
            ViewModel is { } viewModel)
        {
            await viewModel.LoadMoreChannelsIfNeededAsync(
                scrollViewer.Offset.Y,
                scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        }
    }
}
