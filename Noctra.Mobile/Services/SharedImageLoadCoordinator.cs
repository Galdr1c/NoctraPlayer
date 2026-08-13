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
        => await GetOrLoadCoreAsync(
                key,
                loader,
                static (value, _) => Task.FromResult(value),
                releaseValue: null,
                consumerToken)
            .ConfigureAwait(false);

    public async Task<SharedImageLoadResult<TResult>> GetOrLoadAsync<TResult>(
        TKey key,
        Func<CancellationToken, Task<TValue>> loader,
        Func<TValue, TResult> projector,
        Action<TValue> releaseValue,
        CancellationToken consumerToken)
        => await GetOrLoadCoreAsync(
                key,
                loader,
                (value, _) => Task.FromResult(projector(value)),
                releaseValue,
                consumerToken)
            .ConfigureAwait(false);

    public async Task<SharedImageLoadResult<TResult>> GetOrLoadAsync<TResult>(
        TKey key,
        Func<CancellationToken, Task<TValue>> loader,
        Func<TValue, CancellationToken, Task<TResult>> projector,
        Action<TValue> releaseValue,
        CancellationToken consumerToken)
        => await GetOrLoadCoreAsync(
                key,
                loader,
                projector,
                releaseValue,
                consumerToken)
            .ConfigureAwait(false);

    private async Task<SharedImageLoadResult<TResult>> GetOrLoadCoreAsync<TResult>(
        TKey key,
        Func<CancellationToken, Task<TValue>> loader,
        Func<TValue, CancellationToken, Task<TResult>> projector,
        Action<TValue>? releaseValue,
        CancellationToken consumerToken)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(projector);
        consumerToken.ThrowIfCancellationRequested();

        Entry? entry = null;
        var shouldStart = false;
        var rejected = false;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                entry = existing;
                if ((entry.ReleaseValue is null) != (releaseValue is null))
                {
                    throw new InvalidOperationException(
                        "Consumers for one shared key must use the same value-lifetime mode.");
                }
            }
            else
            {
                if (_activeLoadCount >= _capacity)
                {
                    rejected = true;
                }
                else
                {
                    entry = new Entry(releaseValue);
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
            return new SharedImageLoadResult<TResult>(false, default);
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
            consumerToken.ThrowIfCancellationRequested();
            var projected = await projector(value, consumerToken).ConfigureAwait(false);
            return new SharedImageLoadResult<TResult>(true, projected);
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
            var releaseImmediately = false;
            lock (_sync)
            {
                entry.ProducedValue = value;
                entry.HasProducedValue = true;
                if (entry.ConsumerCount == 0 && entry.ReleaseValue is not null)
                {
                    entry.ValueReleased = true;
                    releaseImmediately = true;
                }
            }

            entry.Completion.TrySetResult(value);
            if (releaseImmediately)
            {
                ReleaseValueSafely(entry, value);
            }
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
            if (entry.LifetimeState.MarkLoaderCompleted())
            {
                entry.Lifetime.Dispose();
            }
        }
    }

    private void ReleaseConsumer(TKey key, Entry entry)
    {
        var cancelUnderlying = false;
        var releaseProducedValue = false;
        TValue? producedValue = default;
        lock (_sync)
        {
            if (entry.ConsumerCount <= 0)
            {
                return;
            }

            entry.ConsumerCount--;
            if (entry.ConsumerCount == 0)
            {
                if (_entries.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, entry))
                {
                    _entries.Remove(key);
                    cancelUnderlying = !entry.Completion.Task.IsCompleted;
                }

                if (entry.HasProducedValue &&
                    !entry.ValueReleased &&
                    entry.ReleaseValue is not null)
                {
                    entry.ValueReleased = true;
                    producedValue = entry.ProducedValue;
                    releaseProducedValue = true;
                }
            }
        }

        if (cancelUnderlying && entry.LifetimeState.BeginCancellation())
        {
            var count = Interlocked.Increment(ref _underlyingCancellationCount);
            PerformanceTrace.Mark("ImageUnderlyingCancelled", count);
            try
            {
                entry.Lifetime.Cancel();
            }
            finally
            {
                if (entry.LifetimeState.FinishCancellation())
                {
                    entry.Lifetime.Dispose();
                }
            }
        }

        if (releaseProducedValue)
        {
            ReleaseValueSafely(entry, producedValue!);
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
            if (entry.ConsumerCount == 0 &&
                _entries.TryGetValue(key, out var current) &&
                ReferenceEquals(current, entry))
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

    private static void ReleaseValueSafely(Entry entry, TValue value)
    {
        try
        {
            entry.ReleaseValue?.Invoke(value);
        }
        catch
        {
            // Resource cleanup must not fault a successful consumer or a
            // fire-and-forget loader completion.
        }
    }

    private sealed class Entry
    {
        public Entry(Action<TValue>? releaseValue)
        {
            ReleaseValue = releaseValue;
        }

        public CancellationTokenSource Lifetime { get; } = new();
        public SharedImageLifetimeState LifetimeState { get; } = new();
        public TaskCompletionSource<TValue> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ConsumerCount { get; set; }
        public Action<TValue>? ReleaseValue { get; }
        public TValue? ProducedValue { get; set; }
        public bool HasProducedValue { get; set; }
        public bool ValueReleased { get; set; }
    }
}

internal sealed class SharedImageLifetimeState
{
    private readonly object _sync = new();
    private bool _loaderCompleted;
    private bool _cancellationInProgress;
    private bool _disposed;

    public bool BeginCancellation()
    {
        lock (_sync)
        {
            if (_loaderCompleted || _disposed)
            {
                return false;
            }

            _cancellationInProgress = true;
            return true;
        }
    }

    public bool MarkLoaderCompleted()
    {
        lock (_sync)
        {
            _loaderCompleted = true;
            return TryClaimDisposeLocked();
        }
    }

    public bool FinishCancellation()
    {
        lock (_sync)
        {
            _cancellationInProgress = false;
            return TryClaimDisposeLocked();
        }
    }

    private bool TryClaimDisposeLocked()
    {
        if (!_loaderCompleted || _cancellationInProgress || _disposed)
        {
            return false;
        }

        _disposed = true;
        return true;
    }
}
