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
}
