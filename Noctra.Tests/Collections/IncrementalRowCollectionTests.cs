using Noctra.Core.Collections;

namespace Noctra.Tests.Collections;

public sealed class IncrementalRowCollectionTests
{
    private sealed record TestRow(IReadOnlyList<int> Items);

    [Fact]
    public void Rebuild_GroupsItemsIntoFixedWidthRows()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 30),
            columns: 3);

        Assert.Equal(30, rows.ItemCount);
        Assert.Equal(10, rows.Rows.Count);
        Assert.All(rows.Rows, row => Assert.Equal(3, row.Items.Count));
        Assert.Equal(30, rows.Rows.Sum(row => row.Items.Count));
    }

    [Fact]
    public void EmptySource_HasNoRows()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Array.Empty<int>(),
            columns: 3);

        Assert.Empty(rows.Rows);
        Assert.Equal(0, rows.ItemCount);
    }

    [Fact]
    public void PartialTail_FormsFinalIncompleteRow()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 14),
            columns: 3);

        Assert.Equal(14, rows.ItemCount);
        Assert.Equal(5, rows.Rows.Count);
        Assert.Equal(3, rows.Rows[^2].Items.Count);
        Assert.Equal(2, rows.Rows[^1].Items.Count);
    }

    [Fact]
    public void Append_FillsIncompleteTail_Incrementally()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 13),
            columns: 3);
        var rebuilds = rows.FullRebuildCount;

        var appended = rows.TryAppend(
            startingIndex: 13,
            appendedItems: new[] { 14, 15, 16 },
            columns: 3);

        Assert.True(appended);
        Assert.Equal(rebuilds, rows.FullRebuildCount);
        Assert.Equal(16, rows.ItemCount);
        Assert.Equal(6, rows.Rows.Count);
        Assert.Equal(16, rows.Rows.Sum(row => row.Items.Count));
    }

    [Fact]
    public void Paging_AppendsCompleteRows_Incrementally()
    {
        var rows = CreateRows();

        rows.Rebuild(
            Enumerable.Range(1, 15),
            columns: 3);

        var appended = rows.TryAppend(
            startingIndex: 15,
            appendedItems: Enumerable.Range(16, 15).ToArray(),
            columns: 3);

        Assert.True(appended);
        Assert.Equal(30, rows.ItemCount);
        Assert.Equal(10, rows.Rows.Count);
        Assert.Equal(30, rows.Rows.Sum(row => row.Items.Count));
    }

    [Fact]
    public void TenThousandItemsInPages_DoesNotTriggerAnotherFullRebuild()
    {
        const int pageSize = 30;
        const int totalItems = 10_000;
        var rows = CreateRows();
        rows.Rebuild(Enumerable.Range(0, pageSize), columns: 3);
        var firstRow = rows.Rows[0];

        for (var start = pageSize; start < totalItems; start += pageSize)
        {
            var count = Math.Min(pageSize, totalItems - start);
            Assert.True(rows.TryAppend(
                start,
                Enumerable.Range(start, count).ToArray(),
                columns: 3));
        }

        Assert.Equal(totalItems, rows.ItemCount);
        Assert.Equal(1, rows.FullRebuildCount);
        Assert.Same(firstRow, rows.Rows[0]);
        Assert.Equal((totalItems + 2) / 3, rows.Rows.Count);
    }

    [Fact]
    public void Append_WithWrongDomainIndex_IsRejected()
    {
        var rows = CreateRows();
        rows.Rebuild(
            Enumerable.Range(1, 12),
            columns: 3);

        var appended = rows.TryAppend(
            startingIndex: 13,
            appendedItems: new[] { 13 },
            columns: 3);

        Assert.False(appended);
        Assert.Equal(12, rows.ItemCount);
    }

    [Fact]
    public void ChangedColumns_RequireFullRebuild()
    {
        var rows = CreateRows();
        rows.Rebuild(
            Enumerable.Range(1, 12),
            columns: 3);

        Assert.False(rows.TryAppend(12, new[] { 13 }, 4));
        Assert.True(rows.TryAppend(12, new[] { 13 }, 3));
    }

    [Fact]
    public void Append_OfEmptyItems_SucceedsWithoutChanges()
    {
        var rows = CreateRows();
        rows.Rebuild(
            Enumerable.Range(1, 9),
            columns: 3);

        Assert.True(rows.TryAppend(9, Array.Empty<int>(), 3));
        Assert.Equal(9, rows.ItemCount);
        Assert.Equal(3, rows.Rows.Count);
    }

    [Fact]
    public void InvalidColumns_AreRejected()
    {
        var rows = CreateRows();

        Assert.Throws<ArgumentOutOfRangeException>(() => rows.Rebuild(Array.Empty<int>(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rows.Rebuild(Array.Empty<int>(), -1));
        Assert.False(rows.TryAppend(0, new[] { 1 }, 0));
    }

    private static IncrementalRowCollection<int, TestRow> CreateRows()
        => new(items => new TestRow(items));
}