using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Noctra.Core.Collections;

/// <summary>
/// ObservableCollection that supports range mutations with one collection
/// notification instead of N+1 per-item layout passes.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    private readonly Func<T, bool>? _countPredicate;
    private int _countedItemCount;

    /// <summary>
    /// Gets the number of items that match the count predicate.
    /// When no predicate is set, returns <see cref="Count"/>.
    /// When a predicate is set, returns the O(1) tracked count
    /// of items matching the predicate — avoids O(n) enumeration.
    /// </summary>
    public int CountedItemCount => _countPredicate != null ? _countedItemCount : Count;

    public BatchObservableCollection() : base()
    {
    }

    public BatchObservableCollection(Func<T, bool>? countPredicate) : base()
    {
        _countPredicate = countPredicate;
    }

    public BatchObservableCollection(IEnumerable<T> collection) : base(collection)
    {
        RecomputeCountedCount();
    }

    public BatchObservableCollection(IEnumerable<T> collection, Func<T, bool>? countPredicate) : base(collection)
    {
        _countPredicate = countPredicate;
        RecomputeCountedCount();
    }

    /// <summary>
    /// Adds multiple items to the collection and fires one indexed Add notification.
    /// Use this instead of foreach + Add to avoid N+1 UI layout passes.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var list = items.ToList();
        if (list.Count == 0) return;

        var startingIndex = Items.Count;
        foreach (var item in list)
        {
            Items.Add(item);
            UpdateCountedCountOnAdd(item);
        }

        RaiseCountNotifications();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            (System.Collections.IList)list,
            startingIndex));
    }

    /// <summary>
    /// Inserts multiple items at one index and publishes one indexed Add event.
    /// </summary>
    public void InsertRange(int index, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)Items.Count);

        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        for (var offset = 0; offset < list.Count; offset++)
        {
            var item = list[offset];
            Items.Insert(index + offset, item);
            UpdateCountedCountOnAdd(item);
        }

        RaiseCountNotifications();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            (System.Collections.IList)list,
            index));
    }

    /// <summary>
    /// Removes a contiguous range and publishes one indexed Remove event.
    /// </summary>
    public void RemoveRange(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (index > Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (count > Items.Count - index)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        var removed = new List<T>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var item = Items[index];
            removed.Add(item);
            Items.RemoveAt(index);
            UpdateCountedCountOnRemove(item);
        }

        RaiseCountNotifications();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            (System.Collections.IList)removed,
            index));
    }

    /// <summary>
    /// Replaces all items in the collection and fires a single Reset notification.
    /// Use this instead of Clear() + foreach Add to avoid N+1 UI layout passes.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var list = items.ToList();

        Items.Clear();
        _countedItemCount = 0;

        foreach (var item in list)
        {
            Items.Add(item);
            UpdateCountedCountOnAdd(item);
        }

        RaiseCountNotifications();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        UpdateCountedCountOnAdd(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CountedItemCount)));
    }

    protected override void RemoveItem(int index)
    {
        var item = Items[index];
        base.RemoveItem(index);
        UpdateCountedCountOnRemove(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CountedItemCount)));
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        _countedItemCount = 0;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CountedItemCount)));
    }

    protected override void SetItem(int index, T item)
    {
        var oldItem = Items[index];
        base.SetItem(index, item);
        UpdateCountedCountOnRemove(oldItem);
        UpdateCountedCountOnAdd(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CountedItemCount)));
    }

    private void RaiseCountNotifications()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CountedItemCount)));
    }

    private void UpdateCountedCountOnAdd(T item)
    {
        if (_countPredicate != null && _countPredicate(item))
        {
            _countedItemCount++;
        }
    }

    private void UpdateCountedCountOnRemove(T item)
    {
        if (_countPredicate != null && _countPredicate(item))
        {
            _countedItemCount--;
        }
    }

    private void RecomputeCountedCount()
    {
        _countedItemCount = 0;
        if (_countPredicate == null) return;

        foreach (var item in Items)
        {
            if (_countPredicate(item))
            {
                _countedItemCount++;
            }
        }
    }
}
