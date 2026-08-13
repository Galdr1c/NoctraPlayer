namespace Noctra.Core.Collections;

/// <summary>
/// Thread-safe LRU cache constrained by both an approximate byte budget and
/// an entry count. An optional removal callback can release cache ownership;
/// callbacks execute after the cache gate is released.
/// </summary>
public sealed class ByteBudgetLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly int _maxEntries;
    private readonly Action<TValue>? _onValueRemoved;
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly LinkedList<TKey> _lru = new();
    private long _currentBytes;

    public ByteBudgetLruCache(
        long maxBytes,
        int maxEntries,
        IEqualityComparer<TKey>? comparer = null,
        Action<TValue>? onValueRemoved = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxBytes = maxBytes;
        _maxEntries = maxEntries;
        _onValueRemoved = onValueRemoved;
        _entries = new Dictionary<TKey, Entry>(comparer);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long CurrentBytes
    {
        get
        {
            lock (_gate)
            {
                return _currentBytes;
            }
        }
    }

    public IReadOnlyList<TKey> Keys
    {
        get
        {
            lock (_gate)
            {
                return _lru.ToList();
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                value = default!;
                return false;
            }

            _lru.Remove(entry.Node);
            _lru.AddLast(entry.Node);
            value = entry.Value;
            return true;
        }
    }

    public bool TryGet<TResult>(
        TKey key,
        Func<TValue, TResult> projector,
        out TResult result)
    {
        ArgumentNullException.ThrowIfNull(projector);

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                result = default!;
                return false;
            }

            _lru.Remove(entry.Node);
            _lru.AddLast(entry.Node);
            result = projector(entry.Value);
            return true;
        }
    }

    public bool TryAdd(TKey key, TValue value, long sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        List<TValue>? removedValues = null;
        bool added;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing.Node);
                _lru.AddLast(existing.Node);
                return false;
            }

            if (sizeBytes > _maxBytes)
            {
                return false;
            }

            while (_entries.Count > 0 &&
                   (_entries.Count >= _maxEntries || _currentBytes + sizeBytes > _maxBytes))
            {
                (removedValues ??= []).Add(RemoveLeastRecentlyUsed());
            }

            var node = _lru.AddLast(key);
            _entries.Add(key, new Entry(value, sizeBytes, node));
            _currentBytes += sizeBytes;
            added = true;
        }

        ReleaseRemovedValues(removedValues);
        return added;
    }

    public void Clear()
    {
        List<TValue>? removedValues;
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            removedValues = _entries.Values.Select(entry => entry.Value).ToList();
            _entries.Clear();
            _lru.Clear();
            _currentBytes = 0;
        }

        ReleaseRemovedValues(removedValues);
    }

    private TValue RemoveLeastRecentlyUsed()
    {
        var node = _lru.First!;
        _lru.RemoveFirst();
        var entry = _entries[node.Value];
        _entries.Remove(node.Value);
        _currentBytes -= entry.SizeBytes;
        return entry.Value;
    }

    private void ReleaseRemovedValues(List<TValue>? values)
    {
        if (_onValueRemoved is null || values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            try
            {
                _onValueRemoved(value);
            }
            catch
            {
                // The cache mutation is already complete. Cleanup failures
                // must not make an inserted value appear rejected.
            }
        }
    }

    private sealed record Entry(
        TValue Value,
        long SizeBytes,
        LinkedListNode<TKey> Node);
}
