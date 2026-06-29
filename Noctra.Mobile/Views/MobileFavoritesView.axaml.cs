using Avalonia.Controls;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileFavoritesView : UserControl
{
    public MobileFavoritesView()
    {
        InitializeComponent();
    }

    private async void FavoritesScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        // MobileScrollPaging calls LoadMoreChannelsIfNeededAsync and LoadMoreSeriesIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            DataContext as MainViewModel,
            sender,
            MobileScrollPagingTarget.ChannelsAndSeries);
}
