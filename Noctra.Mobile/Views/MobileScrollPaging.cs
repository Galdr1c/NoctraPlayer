using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

internal enum MobileScrollPagingTarget
{
    Channels,
    Series,
    ChannelsAndSeries,
    History
}

internal static class MobileScrollPaging
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(120);
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollPagingState> States = new();

    public static async Task LoadMoreIfNearEndAsync(
        MainViewModel? viewModel,
        object? sender,
        MobileScrollPagingTarget target)
    {
        if (viewModel is null || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var state = States.GetValue(scrollViewer, _ => new ScrollPagingState());
        var now = DateTime.UtcNow;
        var elapsed = now - state.LastRunUtc;
        if (elapsed < MinimumInterval)
        {
            QueueDeferredRun(viewModel, scrollViewer, target, state, MinimumInterval - elapsed);
            return;
        }

        if (state.IsRunning)
        {
            state.Pending = true;
            return;
        }

        state.IsRunning = true;
        state.LastRunUtc = now;

        try
        {
            var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            await RunTargetAsync(viewModel, scrollViewer.Offset.Y, scrollableHeight, target);
        }
        finally
        {
            state.IsRunning = false;

            if (state.Pending)
            {
                state.Pending = false;
                QueueDeferredRun(viewModel, scrollViewer, target, state, TimeSpan.Zero);
            }
        }
    }

    private static async Task RunTargetAsync(
        MainViewModel viewModel,
        double offset,
        double scrollableHeight,
        MobileScrollPagingTarget target)
    {
        switch (target)
        {
            case MobileScrollPagingTarget.Channels:
                await viewModel.LoadMoreChannelsIfNeededAsync(offset, scrollableHeight);
                break;
            case MobileScrollPagingTarget.Series:
                await viewModel.LoadMoreSeriesIfNeededAsync(offset, scrollableHeight);
                break;
            case MobileScrollPagingTarget.ChannelsAndSeries:
                await viewModel.LoadMoreChannelsIfNeededAsync(offset, scrollableHeight);
                await viewModel.LoadMoreSeriesIfNeededAsync(offset, scrollableHeight);
                break;
            case MobileScrollPagingTarget.History:
                await viewModel.LoadMoreHistoryIfNeededAsync(offset, scrollableHeight);
                break;
        }
    }

    private static void QueueDeferredRun(
        MainViewModel viewModel,
        ScrollViewer scrollViewer,
        MobileScrollPagingTarget target,
        ScrollPagingState state,
        TimeSpan delay)
    {
        if (state.DeferredQueued)
        {
            return;
        }

        state.DeferredQueued = true;
        Dispatcher.UIThread.Post(async () =>
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            state.DeferredQueued = false;
            await LoadMoreIfNearEndAsync(viewModel, scrollViewer, target);
        }, DispatcherPriority.Background);
    }

    private sealed class ScrollPagingState
    {
        public DateTime LastRunUtc { get; set; } = DateTime.MinValue;
        public bool IsRunning { get; set; }
        public bool Pending { get; set; }
        public bool DeferredQueued { get; set; }
    }
}
