using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Navigation;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileLiveView : UserControl, IMobileNavigationStateParticipant
{
    private MainViewModel? _sortViewModel;

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

        if (_sortViewModel is not null)
        {
            _sortViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        base.OnDataContextChanged(e);

        _sortViewModel = ViewModel;
        if (_sortViewModel is not null)
        {
            _sortViewModel.PropertyChanged += ViewModel_PropertyChanged;
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
        if (e.PropertyName is nameof(MainViewModel.SelectedSortOrder) or nameof(MainViewModel.SortOptions))
        {
            UpdateSortSelection();
        }

        if (e.PropertyName == nameof(MainViewModel.SelectedGroup))
        {
            UpdateCategorySelection();
        }
    }

    private void UpdateSortSelection()
    {
        if (_sortViewModel is not null)
        {
            var label = MobileContentSortSelection.GetSelectedLabel(_sortViewModel);
            SortSelectionIcon.Kind = MobileContentSortSelection.GetIcon(_sortViewModel.SelectedSortOrder);
            ToolTip.SetTip(SortSelectionButton, label);
            Avalonia.Automation.AutomationProperties.SetName(SortSelectionButton, label);
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
        CategorySelectionValue.Text = label;
        ToolTip.SetTip(CategorySelectionButton, label);
        Avalonia.Automation.AutomationProperties.SetName(CategorySelectionButton, label);
    }

    private async void LiveScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        // MobileScrollPaging calls LoadMoreChannelsIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            ViewModel,
            sender,
            MobileScrollPagingTarget.Channels);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_sortViewModel is not null)
        {
            _sortViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _sortViewModel = null;
        }

        SelectionSheetHost.TryClose();
        base.OnDetachedFromVisualTree(e);
    }
}
