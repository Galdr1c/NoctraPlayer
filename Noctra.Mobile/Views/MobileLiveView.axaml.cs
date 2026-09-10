using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Threading;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Navigation;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileLiveView : UserControl, IMobileNavigationStateParticipant
{
    private MainViewModel? _sortViewModel;
    private long _visibleEpgPublishGeneration;

    public MobileLiveView()
    {
        InitializeComponent();
    }

    bool IMobileNavigationStateParticipant.TryCaptureNavigationState(out MobilePageScrollState state)
        => MobileNavigationScrollState.TryCapture(PrimaryScrollContent, out state);

    bool IMobileNavigationStateParticipant.TryRestoreNavigationState(
        MobilePageScrollState state,
        bool allowClamping)
        => MobileNavigationScrollState.TryRestore(PrimaryScrollContent, state, allowClamping);

    public event EventHandler<MobileCategorySelectionRequestedEventArgs>? CategorySelectionRequested;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        SelectionSheetHost.TryClose();
        Interlocked.Increment(ref _visibleEpgPublishGeneration);

        if (_sortViewModel is not null)
        {
            _sortViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _sortViewModel.FilteredChannels.CollectionChanged -= FilteredChannels_CollectionChanged;
        }

        base.OnDataContextChanged(e);

        _sortViewModel = ViewModel;
        if (_sortViewModel is not null)
        {
            _sortViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _sortViewModel.FilteredChannels.CollectionChanged += FilteredChannels_CollectionChanged;
            UpdateSortSelection();
            UpdateCategorySelection();
        }
    }

    internal bool TryHandleBack() => SelectionSheetHost.TryClose();

    private void OpenSortSelectionSheet_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        SelectionSheetHost.Show(
            LocalizationSource.Instance["Main.Sort.Title"],
            MobileContentSortSelection.BuildOptions(viewModel),
            option =>
            {
                if (option.Value is ChannelSortOrder sortOrder)
                {
                    viewModel.SelectedSortOrder = sortOrder;
                    UpdateSortSelection();
                }
            });
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
        if (_sortViewModel is not null)
        {
            var label = MobileContentSortSelection.GetSelectedLabel(_sortViewModel);
            CatalogContent.SortIconKind = MobileContentSortSelection.GetIcon(_sortViewModel.SelectedSortOrder);
            CatalogContent.SortLabel = label;
        }
    }

    private void OpenCategorySelection_Click(object? sender, RoutedEventArgs e)
    {
        CategorySelectionRequested?.Invoke(
            this,
            new MobileCategorySelectionRequestedEventArgs("Mobile.Categories.LiveTitle"));
    }

    private void UpdateCategorySelection()
    {
        var selectedGroup = ViewModel?.SelectedGroup;
        var label = string.IsNullOrWhiteSpace(selectedGroup)
            ? LocalizationSource.Instance["Common.All"]
            : selectedGroup;
        CatalogContent.CategoryLabel = label;
    }

    private async void LiveScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        PublishVisibleEpgSnapshot();
        await MobileScrollPaging.LoadMoreIfNearEndAsync(
            ViewModel,
            sender,
            MobileScrollPagingTarget.Channels);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        QueueVisibleEpgSnapshot();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Interlocked.Increment(ref _visibleEpgPublishGeneration);
        if (_sortViewModel is not null)
        {
            _sortViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _sortViewModel.FilteredChannels.CollectionChanged -= FilteredChannels_CollectionChanged;
            _sortViewModel = null;
        }

        SelectionSheetHost.TryClose();
        ViewModel?.ClearVisibleEpgChannels();
        base.OnDetachedFromVisualTree(e);
    }
}
