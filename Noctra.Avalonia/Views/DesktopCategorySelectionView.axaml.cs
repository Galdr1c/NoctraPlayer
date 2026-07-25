using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noctra.Avalonia.Localization;
using Noctra.Core.Collections;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public sealed class DesktopCategorySelectionItem
{
    public DesktopCategorySelectionItem(string name, bool isSelected)
    {
        Name = name;
        IsSelected = isSelected;
    }

    public string Name { get; }
    public bool IsSelected { get; }
}

public partial class DesktopCategorySelectionView : UserControl
{
    private MainViewModel? _viewModel;
    private INotifyCollectionChanged? _groupsCollection;
    private string _searchQuery = string.Empty;
    private DispatcherTimer? _undoTimer;
    private DispatcherTimer? _errorTimer;
    private string? _lastHiddenCategory;

    public DesktopCategorySelectionView()
    {
        InitializeComponent();
        DataContext = this;
    }

    public BatchObservableCollection<DesktopCategorySelectionItem> Categories { get; } = new();
    public event EventHandler? CloseRequested;

    public void Show(MainViewModel viewModel, string title)
    {
        DetachViewModel();
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AttachGroupsCollection();
        TitleTextBlock.Text = title;
        ResetSearch();
        RefreshCategories();
        IsVisible = true;
        CategorySearchTextBox.Focus();
    }

    public bool TryClose()
    {
        if (!IsVisible)
        {
            return false;
        }

        IsVisible = false;
        _undoTimer?.Stop();
        _errorTimer?.Stop();
        UndoSnackbar.IsVisible = false;
        ErrorSnackbar.IsVisible = false;
        _lastHiddenCategory = null;
        DetachViewModel();
        return true;
    }

    private void SelectCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SelectedGroup = sender is Button { Tag: DesktopCategorySelectionItem item }
            ? item.Name
            : null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void HideCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button { Tag: DesktopCategorySelectionItem item })
        {
            return;
        }

        e.Handled = true;
        try
        {
            await _viewModel.HideGroupCommand.ExecuteAsync(item.Name);
            if (_viewModel.IsPremium)
            {
                ShowUndoSnackbar(item.Name);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DesktopCategorySelection] Hide failed: {ex}");
            ShowErrorSnackbar(LocalizationSource.Instance["Common.Error"]);
        }
    }

    private void ShowUndoSnackbar(string categoryName)
    {
        _lastHiddenCategory = categoryName;
        var format = LocalizationSource.Instance["Mobile.Categories.HiddenFormat"];
        UndoSnackbarText.Text = string.Format(CultureInfo.CurrentCulture, format, categoryName);
        UndoSnackbar.IsVisible = true;
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
        try
        {
            if (!string.IsNullOrWhiteSpace(_lastHiddenCategory) && _viewModel is not null)
            {
                await _viewModel.UnhideGroupCommand.ExecuteAsync(_lastHiddenCategory);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DesktopCategorySelection] Undo failed: {ex}");
            ShowErrorSnackbar(LocalizationSource.Instance["Common.Error"]);
        }
        _lastHiddenCategory = null;
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CategorySearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchQuery = CategorySearchTextBox.Text?.Trim() ?? string.Empty;
        ClearCategorySearchButton.IsVisible = _searchQuery.Length > 0;
        RefreshCategories();
    }

    private void ClearCategorySearch_Click(object? sender, RoutedEventArgs e)
    {
        CategorySearchTextBox.Text = string.Empty;
        CategorySearchTextBox.Focus();
    }

    private void CategorySearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_searchQuery.Length > 0)
            {
                CategorySearchTextBox.Text = string.Empty;
            }
            else
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CategoryListBox.Focus();
            e.Handled = true;
        }
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
            QueueRefresh();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedGroup))
        {
            QueueRefresh();
        }
    }

    private void Groups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRefresh();

    private void QueueRefresh()
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
            Categories.ReplaceAll(Array.Empty<DesktopCategorySelectionItem>());
            UpdateEmptyState();
            return;
        }

        AllCategoriesCheck.IsVisible = string.IsNullOrWhiteSpace(_viewModel.SelectedGroup);
        var groups = _viewModel.Groups.AsEnumerable();
        if (_searchQuery.Length > 0)
        {
            groups = groups.Where(group =>
                group.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase));
        }

        Categories.ReplaceAll(groups.Select(group =>
            new DesktopCategorySelectionItem(
                group,
                string.Equals(group, _viewModel.SelectedGroup, StringComparison.Ordinal))));
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var empty = Categories.Count == 0;
        EmptyStatePanel.IsVisible = empty;
        CategoryListBox.IsVisible = !empty;
    }

    private void ResetSearch()
    {
        _searchQuery = string.Empty;
        CategorySearchTextBox.Text = string.Empty;
        ClearCategorySearchButton.IsVisible = false;
    }

    private void ShowErrorSnackbar(string message)
    {
        _errorTimer?.Stop();
        ErrorSnackbarText.Text = message;
        ErrorSnackbar.IsVisible = true;
        _errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _errorTimer.Tick += (_, _) =>
        {
            _errorTimer.Stop();
            ErrorSnackbar.IsVisible = false;
        };
        _errorTimer.Start();
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
