namespace Noctra.Core.Collections;

public sealed record SectionedRowSource<TSection, TItem>(
    TSection Section,
    IEnumerable<TItem> Items,
    int Columns)
    where TSection : notnull;

public sealed record SectionedCollectionRow<TSection, TItem>(
    TSection Section,
    bool IsHeader,
    IReadOnlyList<TItem> Items)
    where TSection : notnull;

/// <summary>
/// Flattens independently grouped sections into one header/row stream. A
/// contiguous append mutates only the affected section tail and inserts new
/// rows before the next section without rebuilding unrelated rows.
/// </summary>
public sealed class SectionedIncrementalRowCollection<TSection, TItem>
    where TSection : notnull
{
    private readonly Dictionary<TSection, SectionState> _states;
    private readonly List<SectionState> _orderedStates = new();

    public SectionedIncrementalRowCollection(IEqualityComparer<TSection>? comparer = null)
    {
        _states = new Dictionary<TSection, SectionState>(comparer);
    }

    public BatchObservableCollection<SectionedCollectionRow<TSection, TItem>> Rows { get; } = new();

    public int FullRebuildCount { get; private set; }

    public void Rebuild(IEnumerable<SectionedRowSource<TSection, TItem>> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        _states.Clear();
        _orderedStates.Clear();
        var flattened = new List<SectionedCollectionRow<TSection, TItem>>();

        foreach (var source in sections)
        {
            ArgumentNullException.ThrowIfNull(source.Items);
            ArgumentOutOfRangeException.ThrowIfLessThan(source.Columns, 1);

            var state = new SectionState(source.Section, source.Columns);
            if (!_states.TryAdd(source.Section, state))
            {
                throw new ArgumentException("Section keys must be unique.", nameof(sections));
            }

            _orderedStates.Add(state);
            foreach (var row in GroupRows(source.Section, source.Items, source.Columns))
            {
                state.ItemRows.Add(row);
                state.ItemCount += row.Items.Count;
            }

            if (state.ItemRows.Count == 0)
            {
                continue;
            }

            flattened.Add(state.Header);
            flattened.AddRange(state.ItemRows);
        }

        FullRebuildCount++;
        Rows.ReplaceAll(flattened);
    }

    public bool TryAppend(
        TSection section,
        int startingIndex,
        IEnumerable<TItem> appendedItems,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(appendedItems);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        if (!_states.TryGetValue(section, out var state) ||
            state.Columns != columns ||
            state.ItemCount != startingIndex)
        {
            return false;
        }

        var additions = appendedItems as IReadOnlyList<TItem> ?? appendedItems.ToList();
        if (additions.Count == 0)
        {
            return true;
        }

        var headerIndex = GetFlatSectionIndex(state);
        var additionIndex = 0;
        var oldRowCount = state.ItemRows.Count;

        if (oldRowCount > 0 && state.ItemRows[^1].Items.Count < columns)
        {
            var completedTail = state.ItemRows[^1].Items.ToList();
            while (completedTail.Count < columns && additionIndex < additions.Count)
            {
                completedTail.Add(additions[additionIndex++]);
            }

            var replacement = CreateItemRow(section, completedTail);
            state.ItemRows[^1] = replacement;
            Rows[headerIndex + oldRowCount] = replacement;
        }

        var newRows = new List<SectionedCollectionRow<TSection, TItem>>();
        while (additionIndex < additions.Count)
        {
            var take = Math.Min(columns, additions.Count - additionIndex);
            var items = new TItem[take];
            for (var index = 0; index < take; index++)
            {
                items[index] = additions[additionIndex++];
            }

            newRows.Add(CreateItemRow(section, items));
        }

        if (oldRowCount == 0)
        {
            state.ItemRows.AddRange(newRows);
            Rows.InsertRange(headerIndex, new[] { state.Header }.Concat(newRows));
        }
        else if (newRows.Count > 0)
        {
            state.ItemRows.AddRange(newRows);
            Rows.InsertRange(headerIndex + 1 + oldRowCount, newRows);
        }

        state.ItemCount += additions.Count;
        return true;
    }

    public int GetItemCount(TSection section)
        => _states.TryGetValue(section, out var state)
            ? state.ItemCount
            : 0;

    private int GetFlatSectionIndex(SectionState target)
    {
        var index = 0;
        foreach (var state in _orderedStates)
        {
            if (ReferenceEquals(state, target))
            {
                return index;
            }

            if (state.ItemRows.Count > 0)
            {
                index += 1 + state.ItemRows.Count;
            }
        }

        return index;
    }

    private static IEnumerable<SectionedCollectionRow<TSection, TItem>> GroupRows(
        TSection section,
        IEnumerable<TItem> items,
        int columns)
    {
        var row = new List<TItem>(columns);
        foreach (var item in items)
        {
            row.Add(item);
            if (row.Count != columns)
            {
                continue;
            }

            yield return CreateItemRow(section, row);
            row.Clear();
        }

        if (row.Count > 0)
        {
            yield return CreateItemRow(section, row);
        }
    }

    private static SectionedCollectionRow<TSection, TItem> CreateItemRow(
        TSection section,
        IEnumerable<TItem> items)
        => new(section, IsHeader: false, items.ToArray());

    private sealed class SectionState
    {
        public SectionState(TSection section, int columns)
        {
            Section = section;
            Columns = columns;
            Header = new SectionedCollectionRow<TSection, TItem>(
                section,
                IsHeader: true,
                Array.Empty<TItem>());
        }

        public TSection Section { get; }
        public int Columns { get; }
        public int ItemCount { get; set; }
        public SectionedCollectionRow<TSection, TItem> Header { get; }
        public List<SectionedCollectionRow<TSection, TItem>> ItemRows { get; } = new();
    }
}
