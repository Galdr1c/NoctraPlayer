using Noctra.Core.Collections;

namespace Noctra.Tests.Collections;

public sealed class AdAwareIncrementalRowCollectionTests
{
    private sealed record TestRow(bool IsAd, IReadOnlyList<int> Items, int AdOrdinal = -1);

    [Fact]
    public void Rebuild_InsertsAdsOnlyAfterCompleteContentRows()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 30),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.Equal(30, rows.ItemCount);
        Assert.Equal(12, rows.Rows.Count);
        Assert.True(rows.Rows[5].IsAd);
        Assert.Equal(0, rows.Rows[5].AdOrdinal);
        Assert.True(rows.Rows[11].IsAd);
        Assert.Equal(1, rows.Rows[11].AdOrdinal);
        Assert.Equal(30, rows.Rows.Where(row => !row.IsAd).Sum(row => row.Items.Count));
    }


    [Fact]
    public void EmptySource_HasNoRowsAndNoAds()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Array.Empty<int>(),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.Empty(rows.Rows);
        Assert.Equal(0, rows.ItemCount);
    }

    [Fact]
    public void BelowThreshold_HasNoAdRow()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 14),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.DoesNotContain(rows.Rows, row => row.IsAd);
        Assert.Equal(14, rows.ItemCount);
    }

    [Fact]
    public void Append_ThatCompletesAnchor_InsertsAdWithoutFullRebuild()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 13),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        var rebuilds = rows.FullRebuildCount;

        var appended = rows.TryAppend(
            startingIndex: 13,
            appendedItems: new[] { 14, 15 },
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.True(appended);
        Assert.Equal(rebuilds, rows.FullRebuildCount);
        Assert.Equal(15, rows.ItemCount);
        Assert.True(rows.Rows[^1].IsAd);
        Assert.Equal(0, rows.Rows[^1].AdOrdinal);
    }

    [Fact]
    public void Paging_PreservesRealItemCount_AndAddsSecondAdIncrementally()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 15),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        var appended = rows.TryAppend(
            startingIndex: 15,
            appendedItems: Enumerable.Range(16, 15).ToArray(),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.True(appended);
        Assert.Equal(30, rows.ItemCount);
        Assert.Equal(2, rows.Rows.Count(row => row.IsAd));
        Assert.Equal(30, rows.Rows.Where(row => !row.IsAd).Sum(row => row.Items.Count));
    }

    [Fact]
    public void Append_WithWrongDomainIndex_IsRejected()
    {
        var rows = CreateRows();
        rows.Rebuild(
            Enumerable.Range(1, 12),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        var appended = rows.TryAppend(
            startingIndex: 13,
            appendedItems: new[] { 13 },
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.False(appended);
        Assert.Equal(12, rows.ItemCount);
    }

    [Fact]
    public void ChangedColumnsOrAnchors_RequireFullRebuild()
    {
        var rows = CreateRows();
        rows.Rebuild(
            Enumerable.Range(1, 12),
            columns: 3,
            adAnchors: new[] { 15, 30 });

        Assert.False(rows.TryAppend(12, new[] { 13 }, 4, new[] { 16, 32 }));
        Assert.False(rows.TryAppend(12, new[] { 13 }, 3, new[] { 18, 36 }));
    }

    private static AdAwareIncrementalRowCollection<int, TestRow> CreateRows()
        => new(
            items => new TestRow(false, items),
            ordinal => new TestRow(true, Array.Empty<int>(), ordinal));
}
