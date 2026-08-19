namespace Noctra.Core.Collections;

/// <summary>
/// Incremental virtualized row projection that groups items into fixed-width rows.
/// </summary>
public sealed class IncrementalRowCollection<TItem, TRow>
{
    private readonly Func<IReadOnlyList<TItem>, TRow> _contentRowFactory;
    private readonly List<TItem> _incompleteTail = new();
    private int _columns;

    public IncrementalRowCollection(
        Func<IReadOnlyList<TItem>, TRow> contentRowFactory)
    {
        _contentRowFactory = contentRowFactory
            ?? throw new ArgumentNullException(nameof(contentRowFactory));
    }

    public BatchObservableCollection<TRow> Rows { get; } = new();
    public int ItemCount { get; private set; }
    public int FullRebuildCount { get; private set; }

    public void Rebuild(
        IEnumerable<TItem> source,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));

        _columns = columns;
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
        int columns)
    {
        ArgumentNullException.ThrowIfNull(appendedItems);

        if (appendedItems.Count == 0)
            return true;

        if (columns <= 0 ||
            columns != _columns ||
            startingIndex != ItemCount)
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
}
