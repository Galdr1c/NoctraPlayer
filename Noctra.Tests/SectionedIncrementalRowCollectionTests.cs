using Noctra.Core.Collections;

namespace Noctra.Tests;

public sealed class SectionedIncrementalRowCollectionTests
{
    [Fact]
    public void Rebuild_FlattensNonEmptySectionsInStableHeaderAndRowOrder()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();

        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1, 2, 3], 2),
            new SectionedRowSource<string, int>("empty", [], 3),
            new SectionedRowSource<string, int>("vod", [10, 11], 3)
        ]);

        Assert.Collection(
            projection.Rows,
            row => AssertHeader(row, "live"),
            row => Assert.Equal([1, 2], AssertItems(row, "live")),
            row => Assert.Equal([3], AssertItems(row, "live")),
            row => AssertHeader(row, "vod"),
            row => Assert.Equal([10, 11], AssertItems(row, "vod")));
    }

    [Fact]
    public void TryAppend_ToEarlierSection_PreservesUnchangedRowsAndInsertsBeforeNextHeader()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1, 2], 2),
            new SectionedRowSource<string, int>("vod", [10, 11], 2)
        ]);
        var liveHeader = projection.Rows[0];
        var liveFirstRow = projection.Rows[1];
        var vodHeader = projection.Rows[2];
        var vodFirstRow = projection.Rows[3];

        var appended = projection.TryAppend("live", 2, [3, 4], columns: 2);

        Assert.True(appended);
        Assert.Same(liveHeader, projection.Rows[0]);
        Assert.Same(liveFirstRow, projection.Rows[1]);
        Assert.Equal([3, 4], AssertItems(projection.Rows[2], "live"));
        Assert.Same(vodHeader, projection.Rows[3]);
        Assert.Same(vodFirstRow, projection.Rows[4]);
        Assert.Equal(1, projection.FullRebuildCount);
    }

    [Fact]
    public void TryAppend_CompletesOnlyTailAndPreservesOtherSections()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1], 2),
            new SectionedRowSource<string, int>("series", [20], 2)
        ]);
        var oldTail = projection.Rows[1];
        var seriesHeader = projection.Rows[2];
        var seriesRow = projection.Rows[3];

        Assert.True(projection.TryAppend("live", 1, [2, 3], columns: 2));

        Assert.NotSame(oldTail, projection.Rows[1]);
        Assert.Equal([1, 2], AssertItems(projection.Rows[1], "live"));
        Assert.Equal([3], AssertItems(projection.Rows[2], "live"));
        Assert.Same(seriesHeader, projection.Rows[3]);
        Assert.Same(seriesRow, projection.Rows[4]);
    }

    [Fact]
    public void TryAppend_TenThousandItemsAcrossPages_DoesNotRebuildProjection()
    {
        const int pageSize = 30;
        const int totalItems = 10_000;
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", Enumerable.Range(0, pageSize), 3),
            new SectionedRowSource<string, int>("vod", [20_000], 3)
        ]);
        var firstRow = projection.Rows[1];
        var vodHeader = projection.Rows[^2];

        for (var start = pageSize; start < totalItems; start += pageSize)
        {
            var count = Math.Min(pageSize, totalItems - start);
            Assert.True(projection.TryAppend(
                "live",
                start,
                Enumerable.Range(start, count),
                columns: 3));
        }

        Assert.Equal(1, projection.FullRebuildCount);
        Assert.Same(firstRow, projection.Rows[1]);
        Assert.Same(vodHeader, projection.Rows[^2]);
        Assert.Equal(totalItems, projection.GetItemCount("live"));
    }

    [Fact]
    public void TryAppend_RejectsUnknownOrNonContiguousSectionWithoutMutation()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1, 2], 2)
        ]);
        var rows = projection.Rows.ToArray();

        Assert.False(projection.TryAppend("missing", 0, [9], columns: 2));
        Assert.False(projection.TryAppend("live", 1, [9], columns: 2));

        Assert.Equal(rows, projection.Rows);
    }

    [Fact]
    public void TrySynchronizeSection_ReordersAndRemovesOnlyTargetSectionRows()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1, 2, 3, 4], 2),
            new SectionedRowSource<string, int>("vod", [10, 11], 2)
        ]);
        var liveHeader = projection.Rows[0];
        var vodHeader = projection.Rows[3];
        var vodRow = projection.Rows[4];
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        projection.Rows.CollectionChanged += (_, change) => changes.Add(change);

        Assert.True(projection.TrySynchronizeSection("live", [4, 1, 3], columns: 2));

        Assert.Same(liveHeader, projection.Rows[0]);
        Assert.Equal([4, 1], AssertItems(projection.Rows[1], "live"));
        Assert.Equal([3], AssertItems(projection.Rows[2], "live"));
        Assert.Same(vodHeader, projection.Rows[3]);
        Assert.Same(vodRow, projection.Rows[4]);
        Assert.Equal(1, projection.FullRebuildCount);
        Assert.DoesNotContain(
            changes,
            change => change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void TrySynchronizeSection_UnchangedItems_EmitsNoRowEvents()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1, 2, 3], 2)
        ]);
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        projection.Rows.CollectionChanged += (_, change) => changes.Add(change);

        Assert.True(projection.TrySynchronizeSection("live", [1, 2, 3], columns: 2));

        Assert.Empty(changes);
        Assert.Equal(1, projection.FullRebuildCount);
    }

    [Fact]
    public void TrySynchronizeSection_OneChangedTailPreservesUnaffectedRowReference()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("live", [1, 2, 3, 4], 2)
        ]);
        var firstRow = projection.Rows[1];

        Assert.True(projection.TrySynchronizeSection("live", [1, 2, 3, 5], columns: 2));

        Assert.Same(firstRow, projection.Rows[1]);
        Assert.Equal([3, 5], AssertItems(projection.Rows[2], "live"));
    }

    [Fact]
    public void TryAppend_EmptyToVisible_RefreshesFollowingMatchingGroupHeader()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("similar-live", [], 2),
            new SectionedRowSource<string, int>("similar-series", [10], 2)
        ]);
        var oldSeriesHeader = projection.Rows[0];
        var seriesRow = projection.Rows[1];
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        projection.Rows.CollectionChanged += (_, change) => changes.Add(change);

        Assert.True(projection.TryAppend(
            "similar-live",
            startingIndex: 0,
            [1],
            columns: 2,
            shouldRefreshFollowingHeader: static (changed, following) =>
                changed.StartsWith("similar", StringComparison.Ordinal) &&
                following.StartsWith("similar", StringComparison.Ordinal)));

        AssertHeader(projection.Rows[0], "similar-live");
        Assert.Equal([1], AssertItems(projection.Rows[1], "similar-live"));
        AssertHeader(projection.Rows[2], "similar-series");
        Assert.NotSame(oldSeriesHeader, projection.Rows[2]);
        Assert.Same(seriesRow, projection.Rows[3]);
        Assert.DoesNotContain(
            changes,
            change => change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void TrySynchronizeSection_VisibleToEmpty_RefreshesFollowingMatchingGroupHeader()
    {
        var projection = new SectionedIncrementalRowCollection<string, int>();
        projection.Rebuild([
            new SectionedRowSource<string, int>("similar-live", [1], 2),
            new SectionedRowSource<string, int>("similar-series", [10], 2)
        ]);
        var oldSeriesHeader = projection.Rows[2];
        var seriesRow = projection.Rows[3];

        Assert.True(projection.TrySynchronizeSection(
            "similar-live",
            [],
            columns: 2,
            shouldRefreshFollowingHeader: static (_, _) => true));

        AssertHeader(projection.Rows[0], "similar-series");
        Assert.NotSame(oldSeriesHeader, projection.Rows[0]);
        Assert.Same(seriesRow, projection.Rows[1]);
    }

    private static void AssertHeader(
        SectionedCollectionRow<string, int> row,
        string expectedSection)
    {
        Assert.True(row.IsHeader);
        Assert.Equal(expectedSection, row.Section);
        Assert.Empty(row.Items);
    }

    private static IReadOnlyList<int> AssertItems(
        SectionedCollectionRow<string, int> row,
        string expectedSection)
    {
        Assert.False(row.IsHeader);
        Assert.Equal(expectedSection, row.Section);
        return row.Items;
    }
}
