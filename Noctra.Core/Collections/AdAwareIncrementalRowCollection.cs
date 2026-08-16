namespace Noctra.Core.Collections;

/// <summary>
/// Incremental virtualized row projection that keeps advertising rows out of the
/// domain collection. Ad anchors are real-content counts and must land on complete
/// row boundaries.
/// </summary>
public sealed class AdAwareIncrementalRowCollection<TItem, TRow>
{
    private readonly Func<IReadOnlyList<TItem>, TRow> _contentRowFactory;
    private readonly Func<int, TRow> _adRowFactory;
    private readonly List<TItem> _incompleteTail = new();
    private int[] _anchors = Array.Empty<int>();
    private int _columns;

    public AdAwareIncrementalRowCollection(
        Func<IReadOnlyList<TItem>, TRow> contentRowFactory,
        Func<int, TRow> adRowFactory)
    {
        _contentRowFactory = contentRowFactory
            ?? throw new ArgumentNullException(nameof(contentRowFactory));
        _adRowFactory = adRowFactory
            ?? throw new ArgumentNullException(nameof(adRowFactory));
    }

    public BatchObservableCollection<TRow> Rows { get; } = new();
    public int ItemCount { get; private set; }
    public int FullRebuildCount { get; private set; }

    public void Rebuild(
        IEnumerable<TItem> source,
        int columns,
        IReadOnlyList<int>? adAnchors = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));

        _columns = columns;
        _anchors = NormalizeAnchors(adAnchors, columns);
        _incompleteTail.Clear();

        var rows = new List<TRow>();
        var buffer = new List<TItem>(columns);
        var contentCount = 0;

        foreach (var item in source)
        {
            buffer.Add(item);
            contentCount++;

            if (buffer.Count != columns)
                continue;

            rows.Add(_contentRowFactory(buffer.ToArray()));
            buffer.Clear();
            AddAdIfAnchored(rows, contentCount);
        }

        if (buffer.Count > 0)
        {
            _incompleteTail.AddRange(buffer);
            rows.Add(_contentRowFactory(buffer.ToArray()));
        }

        ItemCount = contentCount;
        FullRebuildCount++;
        Rows.ReplaceAll(rows);
    }

    public bool TryAppend(
        int startingIndex,
        IReadOnlyList<TItem> appendedItems,
        int columns,
        IReadOnlyList<int>? adAnchors = null)
    {
        ArgumentNullException.ThrowIfNull(appendedItems);

        if (appendedItems.Count == 0)
            return true;

        var normalizedAnchors = NormalizeAnchors(adAnchors, columns);
        if (columns <= 0 ||
            columns != _columns ||
            startingIndex != ItemCount ||
            !_anchors.SequenceEqual(normalizedAnchors))
        {
            return false;
        }

        var offset = 0;

        if (_incompleteTail.Count > 0)
        {
            var needed = columns - _incompleteTail.Count;
            var take = Math.Min(needed, appendedItems.Count);
            for (var index = 0; index < take; index++)
            {
                _incompleteTail.Add(appendedItems[index]);
            }

            offset += take;
            ItemCount += take;

            if (_incompleteTail.Count < columns)
            {
                Rows[^1] = _contentRowFactory(_incompleteTail.ToArray());
                return true;
            }

            Rows[^1] = _contentRowFactory(_incompleteTail.ToArray());
            _incompleteTail.Clear();

            if (TryGetAdOrdinal(ItemCount, out var tailAdOrdinal))
            {
                Rows.Add(_adRowFactory(tailAdOrdinal));
            }
        }

        var pendingRows = new List<TRow>();
        while (offset + columns <= appendedItems.Count)
        {
            var rowItems = new TItem[columns];
            for (var column = 0; column < columns; column++)
            {
                rowItems[column] = appendedItems[offset + column];
            }

            offset += columns;
            ItemCount += columns;
            pendingRows.Add(_contentRowFactory(rowItems));
            AddAdIfAnchored(pendingRows, ItemCount);
        }

        if (offset < appendedItems.Count)
        {
            _incompleteTail.Clear();
            for (; offset < appendedItems.Count; offset++)
            {
                _incompleteTail.Add(appendedItems[offset]);
                ItemCount++;
            }

            pendingRows.Add(_contentRowFactory(_incompleteTail.ToArray()));
        }

        Rows.AddRange(pendingRows);
        return true;
    }

    private void AddAdIfAnchored(List<TRow> rows, int contentCount)
    {
        if (TryGetAdOrdinal(contentCount, out var ordinal))
        {
            rows.Add(_adRowFactory(ordinal));
        }
    }

    private bool TryGetAdOrdinal(int contentCount, out int ordinal)
    {
        ordinal = Array.BinarySearch(_anchors, contentCount);
        return ordinal >= 0;
    }

    private static int[] NormalizeAnchors(
        IReadOnlyList<int>? anchors,
        int columns)
    {
        if (anchors is null || anchors.Count == 0 || columns <= 0)
            return Array.Empty<int>();

        return anchors
            .Where(anchor => anchor > 0 && anchor % columns == 0)
            .Distinct()
            .OrderBy(anchor => anchor)
            .ToArray();
    }
}
