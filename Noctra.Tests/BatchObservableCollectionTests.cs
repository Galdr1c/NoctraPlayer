using System.Collections.Specialized;
using Noctra.Core.Collections;

namespace Noctra.Tests;

public sealed class BatchObservableCollectionTests
{
    [Fact]
    public void InsertRange_EmitsOneIndexedRangeAddNotification()
    {
        var collection = new BatchObservableCollection<int>([1, 4]);
        var events = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => events.Add(args);

        collection.InsertRange(1, [2, 3]);

        var change = Assert.Single(events);
        Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
        Assert.Equal([2, 3], change.NewItems!.Cast<int>());
        Assert.Equal([1, 2, 3, 4], collection);
    }

    [Fact]
    public void AddRange_EmitsOneRangeAddWithStartingIndexAndItems()
    {
        var collection = new BatchObservableCollection<int>([1, 2]);
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.AddRange([3, 4, 5]);

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(2, change.NewStartingIndex);
        Assert.Equal([3, 4, 5], change.NewItems!.Cast<int>());
    }

    [Fact]
    public void ReplaceAll_StillEmitsOneReset()
    {
        var collection = new BatchObservableCollection<int>([1, 2]);
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.ReplaceAll([7, 8]);

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
        Assert.Equal([7, 8], collection);
    }

    [Fact]
    public void AddRange_UpdatesPredicateBackedCountOnceForAllItems()
    {
        var collection = new BatchObservableCollection<int>(value => value % 2 == 0);

        collection.AddRange([1, 2, 4, 5]);

        Assert.Equal(2, collection.CountedItemCount);
    }

    [Fact]
    public void RemoveRange_EmitsOneIndexedRangeRemoveWithoutReset()
    {
        var collection = new BatchObservableCollection<int>([1, 2, 3, 4]);
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.RemoveRange(index: 1, count: 2);

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, change.Action);
        Assert.Equal(1, change.OldStartingIndex);
        Assert.Equal([2, 3], change.OldItems!.Cast<int>());
        Assert.Equal([1, 4], collection);
    }
}
