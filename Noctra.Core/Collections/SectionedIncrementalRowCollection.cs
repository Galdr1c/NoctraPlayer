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
/// contiguous append or identity-preserving section update mutates only the
/// affected rows without rebuilding unrelated sections.
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
        int columns,
        Func<TSection, TSection, bool>? shouldRefreshFollowingHeader = null)
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
        if (oldRowCount == 0)
        {
            RefreshFollowingHeaderPresentation(state, shouldRefreshFollowingHeader);
        }

        return true;
    }

    public bool TrySynchronizeSection(
        TSection section,
        IEnumerable<TItem> items,
        int columns,
        Func<TSection, TSection, bool>? shouldRefreshFollowingHeader = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        if (!_states.TryGetValue(section, out var state) || state.Columns != columns)
        {
            return false;
        }

        var nextItems = items as IReadOnlyList<TItem> ?? items.ToList();
        if (SectionItemsEqual(state, nextItems))
        {
            return true;
        }

        var nextRows = GroupRows(section, nextItems, columns).ToList();
        var headerIndex = GetFlatSectionIndex(state);
        var oldRowCount = state.ItemRows.Count;
        var visibilityChanged = (oldRowCount == 0) != (nextRows.Count == 0);
        var sharedCount = Math.Min(oldRowCount, nextRows.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (RowItemsEqual(state.ItemRows[index], nextRows[index]))
            {
                continue;
            }

            state.ItemRows[index] = nextRows[index];
            Rows[headerIndex + 1 + index] = nextRows[index];
        }

        if (nextRows.Count > oldRowCount)
        {
            var addedRows = nextRows.Skip(oldRowCount).ToArray();
            state.ItemRows.AddRange(addedRows);
            if (oldRowCount == 0)
            {
                Rows.InsertRange(headerIndex, new[] { state.Header }.Concat(addedRows));
            }
            else
            {
                Rows.InsertRange(headerIndex + 1 + oldRowCount, addedRows);
            }
        }
        else if (nextRows.Count < oldRowCount)
        {
            var removeCount = oldRowCount - nextRows.Count;
            state.ItemRows.RemoveRange(nextRows.Count, removeCount);
            Rows.RemoveRange(headerIndex + 1 + nextRows.Count, removeCount);
            if (nextRows.Count == 0)
            {
                Rows.RemoveAt(headerIndex);
            }
        }

        state.ItemCount = nextItems.Count;
        if (visibilityChanged)
        {
            RefreshFollowingHeaderPresentation(state, shouldRefreshFollowingHeader);
        }

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

    private void RefreshFollowingHeaderPresentation(
        SectionState changed,
        Func<TSection, TSection, bool>? shouldRefresh)
    {
        if (shouldRefresh is null)
        {
            return;
        }

        var changedIndex = _orderedStates.IndexOf(changed);
        for (var index = changedIndex + 1; index < _orderedStates.Count; index++)
        {
            var following = _orderedStates[index];
            if (following.ItemRows.Count == 0)
            {
                continue;
            }

            if (shouldRefresh(changed.Section, following.Section))
            {
                following.Header = CreateHeader(following.Section);
                Rows[GetFlatSectionIndex(following)] = following.Header;
            }

            return;
        }
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

    private static bool SectionItemsEqual(SectionState state, IReadOnlyList<TItem> next)
    {
        if (state.ItemCount != next.Count)
        {
            return false;
        }

        var itemIndex = 0;
        foreach (var row in state.ItemRows)
        {
            foreach (var item in row.Items)
            {
                if (!EqualityComparer<TItem>.Default.Equals(item, next[itemIndex++]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RowItemsEqual(
        SectionedCollectionRow<TSection, TItem> current,
        SectionedCollectionRow<TSection, TItem> next)
    {
        if (current.Items.Count != next.Items.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Items.Count; index++)
        {
            if (!EqualityComparer<TItem>.Default.Equals(current.Items[index], next.Items[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static SectionedCollectionRow<TSection, TItem> CreateItemRow(
        TSection section,
        IEnumerable<TItem> items)
        => new(section, IsHeader: false, items.ToArray());

    private static SectionedCollectionRow<TSection, TItem> CreateHeader(TSection section)
        => new(section, IsHeader: true, Array.Empty<TItem>());

    private sealed class SectionState
    {
        public SectionState(TSection section, int columns)
        {
            Section = section;
            Columns = columns;
            Header = CreateHeader(section);
        }

        public TSection Section { get; }
        public int Columns { get; }
        public int ItemCount { get; set; }
        public SectionedCollectionRow<TSection, TItem> Header { get; set; }
        public List<SectionedCollectionRow<TSection, TItem>> ItemRows { get; } = new();
    }
}
