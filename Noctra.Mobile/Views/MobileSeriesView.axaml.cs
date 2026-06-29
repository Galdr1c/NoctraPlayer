using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSeriesView : UserControl
{
    public MobileSeriesView()
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

    private async void SeriesScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        // MobileScrollPaging calls LoadMoreSeriesIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            ViewModel,
            sender,
            MobileScrollPagingTarget.Series);
}
