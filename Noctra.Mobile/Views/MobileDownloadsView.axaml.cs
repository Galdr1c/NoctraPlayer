using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Navigation;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileDownloadsView : UserControl, IMobileNavigationStateParticipant
{
    private MainViewModel? _viewModel;

    public MobileDownloadsView()
    {
        InitializeComponent();
        InstallSharedDownloadsPresentation();
    }

    bool IMobileNavigationStateParticipant.TryCaptureNavigationState(out MobilePageScrollState state)
        => MobileNavigationScrollState.TryCapture(PrimaryScrollContent, out state);

    bool IMobileNavigationStateParticipant.TryRestoreNavigationState(
        MobilePageScrollState state,
        bool allowClamping)
        => MobileNavigationScrollState.TryRestore(PrimaryScrollContent, state, allowClamping);

    protected override void OnDataContextChanged(EventArgs e)
    {
        SelectionSheetHost.TryClose();

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateDownloadSortTrigger();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }

        SelectionSheetHost.TryClose();
        base.OnDetachedFromVisualTree(e);
    }

    internal bool TryHandleBack() => SelectionSheetHost.TryClose();

    private void OpenDownloadSortSheet_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        SelectionSheetHost.Show(
            LocalizationSource.Instance["Downloads.Sort.Title"],
            new[]
            {
                CreateSortOption(DownloadSortOrder.Latest),
                CreateSortOption(DownloadSortOrder.NameAZ),
                CreateSortOption(DownloadSortOrder.SizeLarge)
            },
            option =>
            {
                if (option.Value is DownloadSortOrder sortOrder)
                {
                    _viewModel.SelectedDownloadSortOrder = sortOrder;
                    UpdateDownloadSortTrigger();
                }
            });
    }

    private MobileSelectionOption CreateSortOption(DownloadSortOrder sortOrder)
        => new(sortOrder, GetSortLabel(sortOrder), _viewModel?.SelectedDownloadSortOrder == sortOrder);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDownloadSortOrder))
        {
            UpdateDownloadSortTrigger();
        }
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedIndex >= 0)
        {
            listBox.SelectedIndex = -1;
        }
    }

    private void UpdateDownloadSortTrigger()
    {
        if (_viewModel is not null)
        {
            var label = GetSortLabel(_viewModel.SelectedDownloadSortOrder);
            DownloadSortSelectionIcon.Kind = MobileContentSortSelection.GetIcon(_viewModel.SelectedDownloadSortOrder);
            ToolTip.SetTip(DownloadSortSelectionButton, label);
            Avalonia.Automation.AutomationProperties.SetName(DownloadSortSelectionButton, label);
        }
    }

    private static string GetSortLabel(DownloadSortOrder sortOrder)
        => LocalizationSource.Instance[sortOrder switch
        {
            DownloadSortOrder.NameAZ => "Downloads.Sort.Name",
            DownloadSortOrder.SizeLarge => "Downloads.Sort.Size",
            _ => "Downloads.Sort.Recent"
        }];
}
