using System;
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
    {
        if (sender is not ScrollViewer scrollViewer ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        await viewModel.LoadMoreHistoryIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
    }
}
