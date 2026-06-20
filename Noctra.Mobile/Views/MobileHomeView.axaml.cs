using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileHomeView : UserControl
{
    private DateTime _lastScrollCheck = DateTime.MinValue;

    public MobileHomeView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void HomeScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if ((DateTime.Now - _lastScrollCheck).TotalMilliseconds < 200)
        {
            return;
        }

        _lastScrollCheck = DateTime.Now;

        if (sender is not ScrollViewer scrollViewer || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        await viewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
    }

    private void OnQuickAccessClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string destination)
        {
            if (VisualRoot is MainView mainView)
            {
                mainView.NavigateToDestination(destination);
            }
        }
    }
}
