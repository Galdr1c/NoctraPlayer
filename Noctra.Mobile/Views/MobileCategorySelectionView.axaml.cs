using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    public MobileCategorySelectionView()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<MobileCategorySelectionItem> Categories { get; } = new();

    public event EventHandler? CloseRequested;

    public void Show(MainViewModel viewModel, string title)
    {
        DetachViewModel();
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AttachGroupsCollection();

        TitleTextBlock.Text = title;
        RefreshCategories();
        CategoryScrollViewer.Offset = default;
        IsVisible = true;
    }

    public bool TryClose()
    {
        if (!IsVisible)
        {
            return false;
        }

        IsVisible = false;
        DetachViewModel();
        return true;
    }

    public void ApplySafeArea(Thickness safeArea)
    {
        HeaderContent.Margin = new Thickness(safeArea.Left, safeArea.Top, safeArea.Right, 0);
        CategoryScrollViewer.Margin = new Thickness(safeArea.Left, 0, safeArea.Right, safeArea.Bottom);
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
        await _viewModel.HideGroupCommand.ExecuteAsync(item.Name);
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

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
            Categories.Clear();
            return;
        }

        AllCategoriesCheck.IsVisible = string.IsNullOrWhiteSpace(_viewModel.SelectedGroup);
        Categories.Clear();
        foreach (var group in _viewModel.Groups)
        {
            Categories.Add(new MobileCategorySelectionItem(
                group,
                string.Equals(group, _viewModel.SelectedGroup, StringComparison.Ordinal)));
        }
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
