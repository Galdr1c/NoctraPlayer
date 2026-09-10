using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.Avalonia.Localization;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class MoviesView : UserControl
{
    private MainViewModel? _observedViewModel;

    public MoviesView()
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
        UpdateSortSelection();
        UpdateCategorySelection();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        SelectionSheetHost.TryClose();
        CategorySelectionHost.TryClose();
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
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
            CategorySelectionHost.Show(viewModel, LocalizationSource.Instance["Mobile.Categories.MoviesTitle"]);
        }
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

    private async void MoviesView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel = null;
        }
        SelectionSheetHost.TryClose();
        CategorySelectionHost.TryClose();
        base.OnDetachedFromVisualTree(e);
    }
}
