using System.Collections.Specialized;
using Noctra.Core.Collections;

namespace Noctra.Tests;

public sealed class IdentityCollectionSynchronizerTests
{
    [Fact]
    public void Synchronize_SameReferencesAndOrder_EmitsNoEvents()
    {
        var first = new Item(1, 10, "a");
        var second = new Item(1, 20, "b");
        var collection = new BatchObservableCollection<Item>([first, second]);
        var changes = Capture(collection);

        IdentityCollectionSynchronizer.Synchronize(
            collection,
            [first, second],
            static item => (item.PlaylistId, item.Id));

        Assert.Empty(changes);
    }

    [Fact]
    public void Synchronize_AppendedPage_EmitsOneIndexedRangeAddWithoutReset()
    {
        var first = new Item(1, 10, "a");
        var second = new Item(1, 20, "b");
        var third = new Item(1, 30, "c");
        var collection = new BatchObservableCollection<Item>([first]);
        var changes = Capture(collection);

        IdentityCollectionSynchronizer.Synchronize(
            collection,
            [first, second, third],
            static item => (item.PlaylistId, item.Id));

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
        Assert.Equal([second, third], change.NewItems!.Cast<Item>());
    }

    [Fact]
    public void Synchronize_ReorderRemoveAndReplacement_UsesIndexedEventsWithoutReset()
    {
        var first = new Item(1, 10, "a");
        var second = new Item(1, 20, "b");
        var third = new Item(1, 30, "c");
        var replacement = new Item(1, 30, "c2");
        var collection = new BatchObservableCollection<Item>([first, second, third]);
        var changes = Capture(collection);

        IdentityCollectionSynchronizer.Synchronize(
            collection,
            [replacement, first],
            static item => (item.PlaylistId, item.Id));

        Assert.Equal([replacement, first], collection);
        Assert.Contains(changes, change => change.Action == NotifyCollectionChangedAction.Move);
        Assert.Contains(changes, change => change.Action == NotifyCollectionChangedAction.Replace);
        Assert.Contains(changes, change => change.Action == NotifyCollectionChangedAction.Remove);
        Assert.DoesNotContain(changes, change => change.Action == NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void Synchronize_UsesPlaylistAndItemIdAsCompositeIdentity()
    {
        var oldItem = new Item(1, 10, "old");
        var otherPlaylist = new Item(2, 10, "new");
        var collection = new BatchObservableCollection<Item>([oldItem]);
        var changes = Capture(collection);

        IdentityCollectionSynchronizer.Synchronize(
            collection,
            [otherPlaylist],
            static item => (item.PlaylistId, item.Id));

        Assert.Same(otherPlaylist, Assert.Single(collection));
        Assert.DoesNotContain(changes, change => change.Action == NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void Synchronize_MiddleInsertPreservesFollowingExistingReference()
    {
        var first = new Item(1, 10, "a");
        var third = new Item(1, 30, "c");
        var second = new Item(1, 20, "b");
        var collection = new BatchObservableCollection<Item>([first, third]);

        IdentityCollectionSynchronizer.Synchronize(
            collection,
            [first, second, third],
            static item => (item.PlaylistId, item.Id));

        Assert.Same(third, collection[2]);
    }

    private static List<NotifyCollectionChangedEventArgs> Capture<T>(BatchObservableCollection<T> collection)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, change) => changes.Add(change);
        return changes;
    }

    private sealed record Item(int PlaylistId, int Id, string Value);
}
