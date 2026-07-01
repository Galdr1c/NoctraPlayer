using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Material.Icons;
using Noctra.Mobile.Localization;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileLiveView : UserControl
{
    private const long LongPressThresholdMs = 500;
    private CancellationTokenSource? _longPressCts;

    public MobileLiveView()
    {
        InitializeComponent();

        if (GroupFilterComboBox is not null)
        {
            GroupFilterComboBox.PointerPressed += OnGroupComboBoxPointerPressed;
            GroupFilterComboBox.PointerReleased += OnGroupComboBoxPointerReleased;
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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

    private async void LiveScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        // MobileScrollPaging calls LoadMoreChannelsIfNeededAsync.
        => await MobileScrollPaging.LoadMoreIfNearEndAsync(
            ViewModel,
            sender,
            MobileScrollPagingTarget.Channels);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _longPressCts?.Cancel();
        if (GroupFilterComboBox is not null)
        {
            GroupFilterComboBox.PointerPressed -= OnGroupComboBoxPointerPressed;
            GroupFilterComboBox.PointerReleased -= OnGroupComboBoxPointerReleased;
        }
    }
}
