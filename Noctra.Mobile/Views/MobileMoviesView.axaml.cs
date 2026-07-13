using System;
using System.ComponentModel;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Material.Icons;
using Noctra.Mobile.Localization;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileMoviesView : UserControl
{
    private const long LongPressThresholdMs = 500;
    private CancellationTokenSource? _longPressCts;
    private MainViewModel? _sortViewModel;

    public MobileMoviesView()
    {
        InitializeComponent();

        if (GroupFilterComboBox is not null)
        {
            GroupFilterComboBox.PointerPressed += OnGroupComboBoxPointerPressed;
            GroupFilterComboBox.PointerReleased += OnGroupComboBoxPointerReleased;
        }
    }

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

    private void OnGroupComboBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _longPressCts?.Cancel();
        _longPressCts = new CancellationTokenSource();

        var token = _longPressCts.Token;
        DispatcherTimer.RunOnce(() =>
        {
            if (token.IsCancellationRequested) return;
            ShowGroupHideFlyout();
        }, TimeSpan.FromMilliseconds(LongPressThresholdMs));
    }

    private void OnGroupComboBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPressCts?.Cancel();
    }

    private void ShowGroupHideFlyout()
    {
        if (ViewModel?.SelectedGroup is not string selectedGroup || string.IsNullOrEmpty(selectedGroup))
            return;

        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.Bottom
        };

        var hideItem = new MenuItem
        {
            Header = LocalizationSource.Instance["Live.HideCategory.Tooltip"],
            Command = ViewModel.HideGroupCommand,
            CommandParameter = selectedGroup
        };
        hideItem.Icon = new Material.Icons.Avalonia.MaterialIcon
        {
            Kind = MaterialIconKind.EyeOffOutline
        };

        flyout.Items.Add(hideItem);
        flyout.ShowAt(GroupFilterComboBox);
    }

    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.SelectedGroup = null;
        }
    }

    private async void MoviesScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
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
        _longPressCts?.Cancel();
        if (GroupFilterComboBox is not null)
        {
            GroupFilterComboBox.PointerPressed -= OnGroupComboBoxPointerPressed;
            GroupFilterComboBox.PointerReleased -= OnGroupComboBoxPointerReleased;
        }
    }
}
