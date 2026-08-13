using Noctra.Core.Collections;

namespace Noctra.Tests;

public sealed class ByteBudgetLruCacheTests
{
    [Fact]
    public void TryAdd_EvictsLeastRecentlyUsedEntriesToStayWithinByteBudget()
    {
        var cache = new ByteBudgetLruCache<string, string>(maxBytes: 10, maxEntries: 10);
        Assert.True(cache.TryAdd("old", "old-value", sizeBytes: 4));
        Assert.True(cache.TryAdd("kept", "kept-value", sizeBytes: 4));
        Assert.True(cache.TryGet("old", out _));

        Assert.True(cache.TryAdd("new", "new-value", sizeBytes: 4));

        Assert.True(cache.TryGet("old", out _));
        Assert.False(cache.TryGet("kept", out _));
        Assert.True(cache.TryGet("new", out _));
        Assert.Equal(8, cache.CurrentBytes);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void TryAdd_AlsoHonorsEntryLimit()
    {
        var cache = new ByteBudgetLruCache<int, string>(maxBytes: 100, maxEntries: 2);
        cache.TryAdd(1, "one", 1);
        cache.TryAdd(2, "two", 1);

        cache.TryAdd(3, "three", 1);

        Assert.False(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(3, out _));
    }

    [Fact]
    public void TryAdd_DoesNotCacheSingleEntryLargerThanBudget()
    {
        var cache = new ByteBudgetLruCache<string, string>(maxBytes: 5, maxEntries: 2);

        Assert.False(cache.TryAdd("large", "value", sizeBytes: 6));

        Assert.Empty(cache.Keys);
        Assert.Equal(0, cache.CurrentBytes);
    }

    [Fact]
    public void TryAdd_DuplicateKey_DoesNotDoubleCountBudget()
    {
        var cache = new ByteBudgetLruCache<string, string>(maxBytes: 10, maxEntries: 2);

        Assert.True(cache.TryAdd("same", "first", sizeBytes: 4));
        Assert.False(cache.TryAdd("same", "second", sizeBytes: 7));

        Assert.Equal(4, cache.CurrentBytes);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("same", out var value));
        Assert.Equal("first", value);
    }

    [Fact]
    public void Eviction_ReleasesRemovedValueAfterCacheMutation()
    {
        var released = new List<string>();
        var cache = new ByteBudgetLruCache<string, string>(
            maxBytes: 4,
            maxEntries: 1,
            onValueRemoved: value => released.Add(value));

        Assert.True(cache.TryAdd("old", "old-value", sizeBytes: 4));
        Assert.True(cache.TryAdd("new", "new-value", sizeBytes: 4));

        Assert.Equal(["old-value"], released);
        Assert.Equal(["new"], cache.Keys);
    }

    [Fact]
    public void ProjectedLookup_AcquiresConsumerBeforeEvictionReleasesCacheOwner()
    {
        var releases = 0;
        var resource = new SharedImageResource<string>("bitmap", 4, _ => releases++);
        var cacheLease = resource.AcquireCacheLease();
        var cache = new ByteBudgetLruCache<string, SharedImageLease<string>>(
            maxBytes: 4,
            maxEntries: 1,
            onValueRemoved: lease => lease.Dispose());
        Assert.True(cache.TryAdd("image", cacheLease, 4));
        resource.Dispose();

        Assert.True(cache.TryGet(
            "image",
            cached => resource.AcquireConsumerLease(),
            out var consumer));

        Assert.True(cache.TryAdd(
            "replacement",
            new SharedImageResource<string>("other", 4, _ => { }).AcquireCacheLease(),
            4));

        Assert.Equal(0, releases);
        Assert.Equal("bitmap", consumer.Value);

        consumer.Dispose();
        Assert.Equal(1, releases);
    }

    [Fact]
    public void RemovalCallbackFailure_DoesNotMisreportCompletedCacheMutation()
    {
        var cache = new ByteBudgetLruCache<string, string>(
            maxBytes: 1,
            maxEntries: 1,
            onValueRemoved: _ => throw new InvalidOperationException("cleanup failed"));
        Assert.True(cache.TryAdd("old", "old-value", 1));

        var added = cache.TryAdd("new", "new-value", 1);

        Assert.True(added);
        Assert.False(cache.TryGet("old", out _));
        Assert.True(cache.TryGet("new", out var current));
        Assert.Equal("new-value", current);
    }
}
