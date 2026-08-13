namespace Noctra.Core.Collections;

internal static class IdentityCollectionSynchronizer
{
    public static bool Synchronize<TItem, TKey>(
        BatchObservableCollection<TItem> collection,
        IEnumerable<TItem> nextItems,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(nextItems);
        ArgumentNullException.ThrowIfNull(keySelector);

        comparer ??= EqualityComparer<TKey>.Default;
        var next = nextItems as IReadOnlyList<TItem> ?? nextItems.ToList();
        var nextKeys = new TKey[next.Count];
        var uniqueKeys = new HashSet<TKey>(comparer);
        for (var index = 0; index < next.Count; index++)
        {
            var key = keySelector(next[index]);
            if (!uniqueKeys.Add(key))
            {
                throw new InvalidOperationException("Next collection identities must be unique.");
            }

            nextKeys[index] = key;
        }

        var changed = false;
        for (var targetIndex = 0; targetIndex < next.Count; targetIndex++)
        {
            var targetKey = nextKeys[targetIndex];
            if (targetIndex < collection.Count &&
                comparer.Equals(keySelector(collection[targetIndex]), targetKey))
            {
                if (!ReferenceEquals(collection[targetIndex], next[targetIndex]))
                {
                    collection[targetIndex] = next[targetIndex];
                    changed = true;
                }

                continue;
            }

            var existingIndex = FindIndex(
                collection,
                targetIndex + 1,
                targetKey,
                keySelector,
                comparer);
            if (existingIndex >= 0)
            {
                collection.Move(existingIndex, targetIndex);
                changed = true;
                if (!ReferenceEquals(collection[targetIndex], next[targetIndex]))
                {
                    collection[targetIndex] = next[targetIndex];
                }

                continue;
            }

            if (targetIndex >= collection.Count)
            {
                collection.AddRange(next.Skip(targetIndex));
                changed = true;
                break;
            }

            collection.Insert(targetIndex, next[targetIndex]);
            changed = true;
        }

        if (collection.Count > next.Count)
        {
            collection.RemoveRange(next.Count, collection.Count - next.Count);
            changed = true;
        }

        return changed;
    }

    private static int FindIndex<TItem, TKey>(
        IReadOnlyList<TItem> collection,
        int startIndex,
        TKey key,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        for (var index = Math.Max(0, startIndex); index < collection.Count; index++)
        {
            if (comparer.Equals(keySelector(collection[index]), key))
            {
                return index;
            }
        }

        return -1;
    }
}
