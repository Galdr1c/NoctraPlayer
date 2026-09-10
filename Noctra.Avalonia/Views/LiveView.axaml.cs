using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noctra.Avalonia.Localization;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class LiveView : UserControl
{
    private MainViewModel? _observedViewModel;
    private long _visibleEpgPublishGeneration;

    public LiveView()
    {
        InitializeComponent();
        CategorySelectionHost.CloseRequested += (_, _) => CategorySelectionHost.TryClose();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void AttachViewModelObserver()
    {
        if (_observedViewModel is not null)
            return;
        var vm = ViewModel;
        if (vm is null)
            return;
        _observedViewModel = vm;
        _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _observedViewModel.FilteredChannels.CollectionChanged += FilteredChannels_CollectionChanged;
        UpdateSortSelection();
        UpdateCategorySelection();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        SelectionSheetHost.TryClose();
        CategorySelectionHost.TryClose();
        Interlocked.Increment(ref _visibleEpgPublishGeneration);
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel.FilteredChannels.CollectionChanged -= FilteredChannels_CollectionChanged;
            _observedViewModel = null;
        }
        base.OnDataContextChanged(e);
        AttachViewModelObserver();
    }

    private void OpenSortSelectionSheet_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null) return;
        SelectionSheetHost.Show(
            LocalizationSource.Instance["Main.Sort.Title"],
            DesktopContentSortSelection.BuildOptions(viewModel),
            option =>
            {
                if (option.Value is ChannelSortOrder sortOrder)
                {
                    viewModel.SelectedSortOrder = sortOrder;
                    UpdateSortSelection();
                }
            });
    }

    private void OpenCategorySelection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            SelectionSheetHost.TryClose();
            CategorySelectionHost.Show(viewModel, LocalizationSource.Instance["Mobile.Categories.LiveTitle"]);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // MainViewModel raises PropertyChanged from background threads while
            // the profile is loading; control access must stay on the UI thread.
            Dispatcher.UIThread.Post(() => ViewModel_PropertyChanged(sender, e), DispatcherPriority.Background);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedSortOrder) or nameof(MainViewModel.SortOptions))
        {
            UpdateSortSelection();
        }
        if (e.PropertyName == nameof(MainViewModel.SelectedGroup))
        {
            UpdateCategorySelection();
        }
        if (e.PropertyName == nameof(MainViewModel.ActiveView) && ViewModel?.ActiveView == AppView.Live)
        {
            QueueVisibleEpgSnapshot();
        }
    }

    private void FilteredChannels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueVisibleEpgSnapshot();

    private void PublishVisibleEpgSnapshot(
        MainViewModel? expectedViewModel = null,
        long? expectedGeneration = null)
    {
        var viewModel = ViewModel;
        if (viewModel is null ||
            (expectedViewModel is not null && !ReferenceEquals(viewModel, expectedViewModel)) ||
            VisualRoot is null ||
            viewModel.ActiveView != AppView.Live)
        {
            return;
        }

        var generation = expectedGeneration ?? viewModel.VisibleEpgChannelsInvalidationGeneration;
        viewModel.TrySetVisibleEpgChannels(
            PrimaryScrollContent.GetVisibleSourceItems().OfType<Channel>(),
            generation);
    }

    private void QueueVisibleEpgSnapshot()
    {
        // The filter pipeline can raise FilteredChannels changes from background
        // threads (profile load); DataContext is UI-thread only.
        var publishGeneration = Interlocked.Increment(ref _visibleEpgPublishGeneration);
        if (Dispatcher.UIThread.CheckAccess())
        {
            PublishQueuedVisibleEpgSnapshot(publishGeneration);
            return;
        }

        Dispatcher.UIThread.Post(
            () => PublishQueuedVisibleEpgSnapshot(publishGeneration),
            DispatcherPriority.Background);
    }

    private void PublishQueuedVisibleEpgSnapshot(long publishGeneration)
    {
        var viewModel = ViewModel;
        if (viewModel is null ||
            publishGeneration != Volatile.Read(ref _visibleEpgPublishGeneration))
        {
            return;
        }

        var expectedGeneration = viewModel.VisibleEpgChannelsInvalidationGeneration;
        PublishVisibleEpgSnapshot(viewModel, expectedGeneration);
    }

    private void UpdateSortSelection()
    {
        if (_observedViewModel is null) return;
        var label = DesktopContentSortSelection.GetSelectedLabel(_observedViewModel);
        CatalogContent.SortIconKind = DesktopContentSortSelection.GetIcon(_observedViewModel.SelectedSortOrder);
        CatalogContent.SortLabel = label;
    }

    private void UpdateCategorySelection()
    {
        var selected = ViewModel?.SelectedGroup;
        var label = string.IsNullOrWhiteSpace(selected)
            ? LocalizationSource.Instance["Common.All"]
            : selected;
        CatalogContent.CategoryLabel = label;
    }

    private async void LiveView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        PublishVisibleEpgSnapshot();
        try
        {
            await ScrollPaging.LoadMoreIfNeededAsync(ViewModel, sender);
        }
        catch (Exception ex)
        {
            if (ViewModel is { } vm) vm.StatusMessage = $"Kaydırma hatası: {ex.Message}";
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (CategorySelectionHost.TryClose() || SelectionSheetHost.TryClose()))
        {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachViewModelObserver();
        QueueVisibleEpgSnapshot();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Interlocked.Increment(ref _visibleEpgPublishGeneration);
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel.FilteredChannels.CollectionChanged -= FilteredChannels_CollectionChanged;
            _observedViewModel = null;
        }
        SelectionSheetHost.TryClose();
        CategorySelectionHost.TryClose();
        ViewModel?.ClearVisibleEpgChannels();
        base.OnDetachedFromVisualTree(e);
    }
}
