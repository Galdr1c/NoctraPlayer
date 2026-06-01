using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

internal static class ScrollPaging
{
    private const double LoadMoreThreshold = 0.70;
    private const int MaxLoadPasses = 4;

    public static async Task LoadMoreIfNeededAsync(MainViewModel? viewModel, object? sender)
    {
        if (viewModel == null || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        for (var pass = 0; pass < MaxLoadPasses; pass++)
        {
            var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var shouldLoad = scrollableHeight <= 0 ||
                             scrollViewer.Offset.Y / scrollableHeight >= LoadMoreThreshold;

            if (!shouldLoad)
            {
                return;
            }

            var before = GetVisibleItemCount(viewModel);

            switch (viewModel.ActiveView)
            {
                case AppView.Live:
                case AppView.Movies:
                    await viewModel.LoadMoreChannelsAsync();
                    break;

                case AppView.Series:
                    await viewModel.LoadMoreSeriesAsync();
                    break;

                case AppView.Home:
                case AppView.Search:
                    await viewModel.LoadMoreChannelsAsync();
                    await viewModel.LoadMoreSeriesAsync();
                    break;

                default:
                    return;
            }

            if (GetVisibleItemCount(viewModel) == before)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
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

    private static int GetVisibleItemCount(MainViewModel viewModel)
        => viewModel.ActiveView == AppView.Series
            ? viewModel.SeriesViewItems.Count
            : viewModel.FilteredChannels.CountedItemCount + viewModel.SeriesViewItems.Count;
}
