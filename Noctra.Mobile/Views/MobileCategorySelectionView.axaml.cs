using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Core.Collections;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public sealed class MobileCategorySelectionRequestedEventArgs : EventArgs
{
    public MobileCategorySelectionRequestedEventArgs(string titleKey)
    {
        TitleKey = titleKey;
    }

    public string TitleKey { get; }
}

public sealed class MobileCategorySelectionItem
{
    public MobileCategorySelectionItem(string name, bool isSelected)
    {
        Name = name;
        IsSelected = isSelected;
    }

    public string Name { get; }

    public bool IsSelected { get; }
}

public partial class MobileCategorySelectionView : UserControl
{
    private MainViewModel? _viewModel;
    private INotifyCollectionChanged? _groupsCollection;
    private string _categorySearchQuery = string.Empty;

    public MobileCategorySelectionView()
    {
        InitializeComponent();
        DataContext = this;
    }

    public BatchObservableCollection<MobileCategorySelectionItem> Categories { get; } = new();

    public event EventHandler? CloseRequested;

    public void Show(MainViewModel viewModel, string title)
    {
        DetachViewModel();
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AttachGroupsCollection();

        TitleTextBlock.Text = title;
        ResetCategorySearch();
        RefreshCategories();
        if (Categories.Count > 0)
        {
            CategoryListBox.ScrollIntoView(Categories[0]);
        }
        IsVisible = true;
    }

    public bool TryClose()
    {
        if (!IsVisible)
        {
            return false;
        }

        IsVisible = false;
        _undoTimer?.Stop();
        UndoSnackbar.IsVisible = false;
        _lastHiddenCategory = null;
        DetachViewModel();
        return true;
    }

    public void ApplySafeArea(Thickness safeArea)
    {
        HeaderContent.Margin = new Thickness(safeArea.Left, safeArea.Top, safeArea.Right, 0);
        CategorySafeAreaHost.Margin = new Thickness(safeArea.Left, 0, safeArea.Right, safeArea.Bottom);
    }

    private void SelectCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (sender is Button { Tag: MobileCategorySelectionItem item })
        {
            _viewModel.SelectedGroup = item.Name;
        }
        else
        {
            _viewModel.SelectedGroup = null;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void HideCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button { Tag: MobileCategorySelectionItem item })
        {
            return;
        }

        e.Handled = true;

        // Kategoriyi gizle
        await _viewModel.HideGroupCommand.ExecuteAsync(item.Name);

        // Undo snackbar göster (eğer premium ise)
        if (_viewModel.IsPremium)
        {
            ShowUndoSnackbar(item.Name);
        }
    }

    private DispatcherTimer? _undoTimer;
    private string? _lastHiddenCategory;

    private void ShowUndoSnackbar(string categoryName)
    {
        _lastHiddenCategory = categoryName;
        UndoSnackbar.IsVisible = true;

        // Localization'dan formatla — StatusMessage paylaşmalı property olduğu için race condition riski var
        string text = categoryName;
        if (Avalonia.Application.Current is App app && app.Services is not null)
        {
            var loc = app.Services.GetService<ILocalizationService>();
            if (loc is not null)
            {
                text = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    loc.GetString("Mobile.Categories.HiddenFormat"),
                    categoryName);
            }
        }
        UndoSnackbarText.Text = text;

        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _undoTimer.Tick += (_, _) =>
        {
            _undoTimer.Stop();
            UndoSnackbar.IsVisible = false;
            _lastHiddenCategory = null;
        };
        _undoTimer.Start();
    }

    private async void UndoHide_Click(object? sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();
        UndoSnackbar.IsVisible = false;

        if (!string.IsNullOrEmpty(_lastHiddenCategory) && _viewModel is not null)
        {
            await _viewModel.UnhideGroupCommand.ExecuteAsync(_lastHiddenCategory);
        }

        _lastHiddenCategory = null;
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CategorySearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _categorySearchQuery = CategorySearchTextBox.Text?.Trim() ?? string.Empty;
        ClearCategorySearchButton.IsVisible = _categorySearchQuery.Length > 0;
        RefreshCategories();
    }

    private void ClearCategorySearch_Click(object? sender, RoutedEventArgs e)
    {
        CategorySearchTextBox.Text = string.Empty;
        CategorySearchTextBox.Focus();
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } listBox)
        {
            listBox.SelectedIndex = -1;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Groups))
        {
            AttachGroupsCollection();
            QueueRefreshCategories();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedGroup))
        {
            QueueRefreshCategories();
        }
    }

    private void Groups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRefreshCategories();

    private void QueueRefreshCategories()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshCategories();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshCategories);
        }
    }

    private void RefreshCategories()
    {
        if (_viewModel is null)
        {
            Categories.ReplaceAll(Array.Empty<MobileCategorySelectionItem>());
            UpdateEmptyState();
            return;
        }

        AllCategoriesCheck.IsVisible = string.IsNullOrWhiteSpace(_viewModel.SelectedGroup);
        var groups = _viewModel.Groups.AsEnumerable();
        if (_categorySearchQuery.Length > 0)
        {
            groups = groups.Where(group =>
                group.Contains(_categorySearchQuery, StringComparison.CurrentCultureIgnoreCase));
        }

        var items = groups.Select(group =>
            new MobileCategorySelectionItem(
                group,
                string.Equals(group, _viewModel.SelectedGroup, StringComparison.Ordinal)));
        Categories.ReplaceAll(items);
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var isEmpty = Categories.Count == 0;
        EmptyStatePanel.IsVisible = isEmpty;
        CategoryListBox.IsVisible = !isEmpty;
    }

    private void CategorySearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Search tuşu: klavyeyi kapat, formda no-op bırakma
            // Avalonia'da doğrudan Focus() ile başka bir elemana odaklanarak klavyeyi kapatabiliriz
            CategoryListBox?.Focus();
            e.Handled = true;
        }
    }

    private void ResetCategorySearch()
    {
        _categorySearchQuery = string.Empty;
        CategorySearchTextBox.Text = string.Empty;
        ClearCategorySearchButton.IsVisible = false;
    }

    private void AttachGroupsCollection()
    {
        if (_groupsCollection is not null)
        {
            _groupsCollection.CollectionChanged -= Groups_CollectionChanged;
        }

        _groupsCollection = _viewModel?.Groups;
        if (_groupsCollection is not null)
        {
            _groupsCollection.CollectionChanged += Groups_CollectionChanged;
        }
    }

    private void DetachViewModel()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _lastHiddenCategory = null;

        if (_groupsCollection is not null)
        {
            _groupsCollection.CollectionChanged -= Groups_CollectionChanged;
            _groupsCollection = null;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }
    }
}
