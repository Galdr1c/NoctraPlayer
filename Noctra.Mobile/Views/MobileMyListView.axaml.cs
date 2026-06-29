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
        // MobileScrollPaging calls LoadMoreChannelsIfNeededAsync and LoadMoreSeriesIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            DataContext as MainViewModel,
            sender,
            MobileScrollPagingTarget.ChannelsAndSeries);
}
