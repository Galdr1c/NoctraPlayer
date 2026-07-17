using Noctra.Core.Collections;

namespace Noctra.Tests;

public sealed class IncrementalRowCollectionTests
{
    [Fact]
    public void TryAppend_ReplacesOnlyIncompleteTailAndPreservesCompletedRows()
    {
        var rows = CreateRows();
        rows.Rebuild([0, 1, 2, 3, 4], columns: 3);
        var firstRow = rows.Rows[0];
        var incompleteTail = rows.Rows[1];

        var appended = rows.TryAppend(startingIndex: 5, [5, 6], columns: 3);

        Assert.True(appended);
        Assert.Same(firstRow, rows.Rows[0]);
        Assert.NotSame(incompleteTail, rows.Rows[1]);
        Assert.Equal([3, 4, 5], rows.Rows[1].Items);
        Assert.Equal([6], rows.Rows[2].Items);
        Assert.Equal(7, rows.ItemCount);
    }

    [Fact]
    public void TryAppend_TenThousandItemsInPages_DoesNotTriggerAnotherFullRebuild()
    {
        const int pageSize = 30;
        const int totalItems = 10_000;
        var rows = CreateRows();
        rows.Rebuild(Enumerable.Range(0, pageSize), columns: 3);
        var firstRow = rows.Rows[0];

        for (var start = pageSize; start < totalItems; start += pageSize)
        {
            var count = Math.Min(pageSize, totalItems - start);
            Assert.True(rows.TryAppend(start, Enumerable.Range(start, count), columns: 3));
        }

        Assert.Equal(totalItems, rows.ItemCount);
        Assert.Equal(1, rows.FullRebuildCount);
        Assert.Same(firstRow, rows.Rows[0]);
        Assert.Equal((totalItems + 2) / 3, rows.Rows.Count);
    }

    [Fact]
    public void TryAppend_RejectsNonContiguousChangesWithoutMutatingRows()
    {
        var rows = CreateRows();
        rows.Rebuild([0, 1, 2], columns: 3);
        var firstRow = rows.Rows[0];

        var appended = rows.TryAppend(startingIndex: 1, [9], columns: 3);

        Assert.False(appended);
        Assert.Equal(3, rows.ItemCount);
        Assert.Single(rows.Rows);
        Assert.Same(firstRow, rows.Rows[0]);
    }

    private static IncrementalRowCollection<int, TestRow> CreateRows()
        => new(items => new TestRow(items));

    private sealed record TestRow(IReadOnlyList<int> Items);
}
