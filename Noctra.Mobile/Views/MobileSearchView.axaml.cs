using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSearchView : UserControl
{
    public MobileSearchView()
    {
        InitializeComponent();
    }

    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.CommitSearchCommand.Execute(null);
        e.Handled = true;
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SearchQuery = string.Empty;
            viewModel.SearchText = string.Empty;
        }

        e.Handled = true;
    }

    private async void SearchScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        // MobileScrollPaging calls LoadMoreChannelsIfNeededAsync and LoadMoreSeriesIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            DataContext as MainViewModel,
            sender,
            MobileScrollPagingTarget.ChannelsAndSeries);
}
