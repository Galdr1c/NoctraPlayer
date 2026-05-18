using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Noctra.Core.Collections;

/// <summary>
/// ObservableCollection that supports batch AddRange and ReplaceAll operations
/// that fire a single NotifyCollectionChangedAction.Reset event instead of
/// N+1 per-item CollectionChanged events. This prevents N+1 UI layout passes
/// when adding or replacing many items at once.
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
    /// Adds multiple items to the collection and fires a single Reset notification.
    /// Use this instead of foreach + Add to avoid N+1 UI layout passes.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var list = items.ToList();
        if (list.Count == 0) return;

        foreach (var item in list)
        {
            Items.Add(item);
            UpdateCountedCountOnAdd(item);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
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

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        UpdateCountedCountOnAdd(item);
    }

    protected override void RemoveItem(int index)
    {
        var item = Items[index];
        base.RemoveItem(index);
        UpdateCountedCountOnRemove(item);
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        _countedItemCount = 0;
    }

    protected override void SetItem(int index, T item)
    {
        var oldItem = Items[index];
        base.SetItem(index, item);
        UpdateCountedCountOnRemove(oldItem);
        UpdateCountedCountOnAdd(item);
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
