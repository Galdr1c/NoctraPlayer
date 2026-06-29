using Avalonia.Controls;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileHomeView : UserControl
{
    public MobileHomeView()
    {
        InitializeComponent();
    }

    private async void HomeScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            DataContext as MainViewModel,
            sender,
            MobileScrollPagingTarget.ChannelsAndSeries);
}
