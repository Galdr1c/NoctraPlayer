using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Diagnostics;

namespace Noctra.Mobile.Services;

internal readonly record struct SharedImageLoadResult<TValue>(bool IsAdmitted, TValue? Value);

internal sealed class SharedImageLoadCoordinator<TKey, TValue>
    where TKey : notnull
{
    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly int _capacity;
    private int _activeLoadCount;
    private int _peakActiveLoadCount;
    private int _runningLoadCount;
    private int _queuedLoadCount;
    private long _consumerCancellationCount;
    private long _underlyingCancellationCount;
    private long _overflowRejectionCount;

    public SharedImageLoadCoordinator(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _entries = new Dictionary<TKey, Entry>(comparer);
    }

    public int EntryCount
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public int ActiveLoadCount
    {
        get
        {
            lock (_sync)
            {
                return _activeLoadCount;
            }
        }
    }

    public int PeakActiveLoadCount
    {
        get
        {
            lock (_sync)
            {
                return _peakActiveLoadCount;
            }
        }
    }

    public int RunningLoadCount
    {
        get
        {
            lock (_sync)
            {
                return _runningLoadCount;
            }
        }
    }

    public int QueuedLoadCount
    {
        get
        {
            lock (_sync)
            {
                return _queuedLoadCount;
            }
        }
    }

    public long ConsumerCancellationCount => Interlocked.Read(ref _consumerCancellationCount);
    public long UnderlyingCancellationCount => Interlocked.Read(ref _underlyingCancellationCount);
    public long OverflowRejectionCount => Interlocked.Read(ref _overflowRejectionCount);

    public async Task<SharedImageLoadResult<TValue>> GetOrLoadAsync(
        TKey key,
        Func<CancellationToken, Task<TValue>> loader,
        CancellationToken consumerToken)
    {
        ArgumentNullException.ThrowIfNull(loader);
        consumerToken.ThrowIfCancellationRequested();

        Entry? entry = null;
        var shouldStart = false;
        var rejected = false;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                entry = existing;
            }
            else
            {
                if (_activeLoadCount >= _capacity)
                {
                    rejected = true;
                }
                else
                {
                    entry = new Entry();
                    _entries.Add(key, entry);
                    _activeLoadCount++;
                    _queuedLoadCount++;
                    _peakActiveLoadCount = Math.Max(_peakActiveLoadCount, _activeLoadCount);
                    shouldStart = true;
                }
            }

            if (!rejected)
            {
                entry!.ConsumerCount++;
            }
        }

        if (rejected)
        {
            var count = Interlocked.Increment(ref _overflowRejectionCount);
            PerformanceTrace.Mark("ImageOverflowRejected", count);
            return new SharedImageLoadResult<TValue>(false, default);
        }

        if (shouldStart)
        {
            ReportLoadCounts();
            _ = Task.Run(() => RunLoaderAsync(key, entry!, loader));
        }

        var acquiredEntry = entry!;

        try
        {
            var value = await acquiredEntry.Completion.Task
                .WaitAsync(consumerToken)
                .ConfigureAwait(false);
            return new SharedImageLoadResult<TValue>(true, value);
        }
        catch (OperationCanceledException) when (consumerToken.IsCancellationRequested)
        {
            var count = Interlocked.Increment(ref _consumerCancellationCount);
            PerformanceTrace.Mark("ImageConsumerCancelled", count);
            throw;
        }
        finally
        {
            ReleaseConsumer(key, acquiredEntry);
        }
    }

    private async Task RunLoaderAsync(
        TKey key,
        Entry entry,
        Func<CancellationToken, Task<TValue>> loader)
    {
        try
        {
            TransitionToRunning();
            var value = await loader(entry.Lifetime.Token).ConfigureAwait(false);
            entry.Completion.TrySetResult(value);
        }
        catch (OperationCanceledException) when (entry.Lifetime.IsCancellationRequested)
        {
            entry.Completion.TrySetCanceled(entry.Lifetime.Token);
        }
        catch (Exception ex)
        {
            entry.Completion.TrySetException(ex);
        }
        finally
        {
            CompleteEntry(key, entry);
            entry.Lifetime.Dispose();
        }
    }

    private void ReleaseConsumer(TKey key, Entry entry)
    {
        var cancelUnderlying = false;
        lock (_sync)
        {
            if (entry.ConsumerCount <= 0)
            {
                return;
            }

            entry.ConsumerCount--;
            if (entry.ConsumerCount == 0 &&
                !entry.Completion.Task.IsCompleted &&
                _entries.TryGetValue(key, out var current) &&
                ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                cancelUnderlying = true;
            }
        }

        if (cancelUnderlying)
        {
            var count = Interlocked.Increment(ref _underlyingCancellationCount);
            PerformanceTrace.Mark("ImageUnderlyingCancelled", count);
            entry.Lifetime.Cancel();
        }
    }

    private void TransitionToRunning()
    {
        lock (_sync)
        {
            _queuedLoadCount--;
            _runningLoadCount++;
        }

        ReportLoadCounts();
    }

    private void CompleteEntry(TKey key, Entry entry)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
            }

            _runningLoadCount--;
            _activeLoadCount--;
        }

        ReportLoadCounts();
    }

    private void ReportLoadCounts()
    {
        int active;
        int queued;
        lock (_sync)
        {
            active = _activeLoadCount;
            queued = _queuedLoadCount;
        }

        PerformanceTrace.Mark("ImageDistinctActive", active);
        PerformanceTrace.Mark("ImageDistinctQueued", queued);
    }

    private sealed class Entry
    {
        public CancellationTokenSource Lifetime { get; } = new();
        public TaskCompletionSource<TValue> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ConsumerCount { get; set; }
    }
}
