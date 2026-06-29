using Avalonia.Controls;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileHistoryView : UserControl
{
    public MobileHistoryView()
    {
        InitializeComponent();
    }

    private async void HistoryScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        // MobileScrollPaging calls LoadMoreHistoryIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            DataContext as MainViewModel,
            sender,
            MobileScrollPagingTarget.History);
}
