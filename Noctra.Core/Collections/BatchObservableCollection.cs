using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Noctra.Core.Collections;

/// <summary>
/// ObservableCollection that supports batch AddRange and ReplaceAll operations
/// that fire a single NotifyCollectionChangedAction.Reset event instead of
/// N+1 per-item CollectionChanged events. This prevents N+1 UI layout passes
/// when adding or replacing many items at once.
/// </summary>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    public BatchObservableCollection() : base() { }

    public BatchObservableCollection(IEnumerable<T> collection) : base(collection) { }

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
        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
