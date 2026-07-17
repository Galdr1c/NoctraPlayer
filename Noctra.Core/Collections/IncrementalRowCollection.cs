namespace Noctra.Core.Collections;

/// <summary>
/// Groups a sequential source into rows while retaining only the incomplete tail.
/// Contiguous appends update the tail and add new rows without enumerating the old source.
/// </summary>
public sealed class IncrementalRowCollection<TItem, TRow>
{
    private readonly Func<IReadOnlyList<TItem>, TRow> _rowFactory;
    private IReadOnlyList<TItem> _incompleteTail = Array.Empty<TItem>();
    private int _columns;

    public IncrementalRowCollection(Func<IReadOnlyList<TItem>, TRow> rowFactory)
    {
        _rowFactory = rowFactory ?? throw new ArgumentNullException(nameof(rowFactory));
    }

    public BatchObservableCollection<TRow> Rows { get; } = new();

    public int ItemCount { get; private set; }

    public int FullRebuildCount { get; private set; }

    public void Rebuild(IEnumerable<TItem> source, int columns)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        var rows = new List<TRow>();
        var rowItems = new List<TItem>(columns);
        var itemCount = 0;

        foreach (var item in source)
        {
            rowItems.Add(item);
            itemCount++;
            if (rowItems.Count != columns)
            {
                continue;
            }

            rows.Add(CreateRow(rowItems));
            rowItems.Clear();
        }

        if (rowItems.Count > 0)
        {
            rows.Add(CreateRow(rowItems));
            _incompleteTail = rowItems.ToArray();
        }
        else
        {
            _incompleteTail = Array.Empty<TItem>();
        }

        _columns = columns;
        ItemCount = itemCount;
        FullRebuildCount++;
        Rows.ReplaceAll(rows);
    }

    public bool TryAppend(int startingIndex, IEnumerable<TItem> appendedItems, int columns)
    {
        ArgumentNullException.ThrowIfNull(appendedItems);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        if (columns != _columns || startingIndex != ItemCount)
        {
            return false;
        }

        var additions = appendedItems as IReadOnlyList<TItem> ?? appendedItems.ToList();
        if (additions.Count == 0)
        {
            return true;
        }

        var additionIndex = 0;
        if (_incompleteTail.Count > 0)
        {
            var completedTail = new List<TItem>(_incompleteTail);
            while (completedTail.Count < columns && additionIndex < additions.Count)
            {
                completedTail.Add(additions[additionIndex++]);
            }

            Rows[^1] = CreateRow(completedTail);
            _incompleteTail = completedTail.Count < columns
                ? completedTail.ToArray()
                : Array.Empty<TItem>();
        }

        var newRows = new List<TRow>();
        while (additionIndex < additions.Count)
        {
            var take = Math.Min(columns, additions.Count - additionIndex);
            var rowItems = new TItem[take];
            for (var index = 0; index < take; index++)
            {
                rowItems[index] = additions[additionIndex++];
            }

            newRows.Add(_rowFactory(rowItems));
            _incompleteTail = take < columns
                ? rowItems
                : Array.Empty<TItem>();
        }

        if (newRows.Count > 0)
        {
            Rows.AddRange(newRows);
        }

        ItemCount += additions.Count;
        return true;
    }

    private TRow CreateRow(IEnumerable<TItem> items)
        => _rowFactory(items.ToArray());
}
