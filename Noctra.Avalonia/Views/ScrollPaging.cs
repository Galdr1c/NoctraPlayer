using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

internal static class ScrollPaging
{
    public static async Task LoadMoreIfNeededAsync(MainViewModel? viewModel, object? sender)
    {
        if (viewModel == null || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (scrollableHeight <= 0)
        {
            return;
        }

        switch (viewModel.ActiveView)
        {
            case AppView.Live:
            case AppView.Movies:
                await viewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
                break;

            case AppView.Series:
                await viewModel.LoadMoreSeriesIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
                break;

            case AppView.Home:
            case AppView.Search:
                await viewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
                await viewModel.LoadMoreSeriesIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
                break;
        }
    }

    public static void QueueLoadMoreAfterWheel(MainViewModel? viewModel, object? sender, PointerWheelEventArgs _)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await LoadMoreIfNeededAsync(viewModel, scrollViewer);
        }, DispatcherPriority.Background);
    }
}
