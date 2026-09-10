using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.Avalonia.Localization;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class DownloadsView : UserControl
{
    private MainViewModel? _observedViewModel;

    public DownloadsView()
    {
        InitializeComponent();
        InstallSharedDownloadsPresentation();
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
        UpdateDownloadSortSelection();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        SelectionSheetHost.TryClose();
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel = null;
        }
        base.OnDataContextChanged(e);
        AttachViewModelObserver();
    }

    private void OpenDownloadSortSheet_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null) return;
        SelectionSheetHost.Show(
            LocalizationSource.Instance["Downloads.Sort.Title"],
            DesktopContentSortSelection.BuildDownloadOptions(viewModel),
            option =>
            {
                if (option.Value is DownloadSortOrder sortOrder)
                {
                    viewModel.SelectedDownloadSortOrder = sortOrder;
                    UpdateDownloadSortSelection();
                }
            });
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDownloadSortOrder))
        {
            UpdateDownloadSortSelection();
        }
    }

    private void UpdateDownloadSortSelection()
    {
        if (_observedViewModel is null) return;
        var label = DesktopContentSortSelection.GetDownloadLabel(_observedViewModel);
        SortSelectionIcon.Kind = DesktopContentSortSelection.GetDownloadIcon(_observedViewModel.SelectedDownloadSortOrder);
        ToolTip.SetTip(SortSelectionButton, label);
        AutomationProperties.SetName(SortSelectionButton, label);
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedIndex >= 0)
        {
            listBox.SelectedIndex = -1;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SelectionSheetHost.TryClose())
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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel = null;
        }
        SelectionSheetHost.TryClose();
        base.OnDetachedFromVisualTree(e);
    }
}
