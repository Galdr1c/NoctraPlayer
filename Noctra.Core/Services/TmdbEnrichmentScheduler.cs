using Microsoft.Extensions.Logging;
using Noctra.Diagnostics;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public sealed record TmdbEnrichmentSchedulerOptions(
    int PendingCapacity = 64,
    int MaxConcurrency = 3,
    TimeSpan? InterRequestDelay = null);

public sealed class TmdbEnrichmentScheduler : ITmdbEnrichmentScheduler, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan DefaultInterRequestDelay = TimeSpan.FromMilliseconds(750);

    private readonly object _gate = new();
    private readonly LinkedList<WorkItem> _pending = new();
    private readonly Dictionary<string, WorkItem> _workByKey = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _workSignal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private readonly int _pendingCapacity;
    private readonly TimeSpan _interRequestDelay;
    private readonly ILogger<TmdbEnrichmentScheduler>? _logger;

    private int _disposeState;
    private int _activeWorkers;
    private int _activeHighWater;
    private int _pendingHighWater;
    private long _scheduledCount;
    private long _startedCount;
    private long _completedCount;
    private long _cancelledCount;
    private long _droppedCount;
    private long _duplicateCount;
    private long _failedCount;

    public TmdbEnrichmentScheduler(
        TmdbEnrichmentSchedulerOptions? options = null,
        ILogger<TmdbEnrichmentScheduler>? logger = null)
    {
        options ??= new TmdbEnrichmentSchedulerOptions();
        if (options.PendingCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Pending capacity must be positive.");
        }

        if (options.MaxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Max concurrency must be positive.");
        }

        var delay = options.InterRequestDelay ?? DefaultInterRequestDelay;
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Inter-request delay cannot be negative.");
        }

        _pendingCapacity = options.PendingCapacity;
        _interRequestDelay = delay;
        _logger = logger;
        _workers = Enumerable.Range(0, options.MaxConcurrency)
            .Select(index => Task.Run(() => RunWorkerAsync(index)))
            .ToArray();
    }

    public Task ScheduleAsync(
        string key,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(work);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        WorkItem item;
        WorkItem? dropped = null;
        List<WorkItem>? cancelledPending = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

            if (_workByKey.TryGetValue(key, out var existing) &&
                !existing.CancellationToken.IsCancellationRequested)
            {
                PerformanceTrace.Mark(
                    "tmdb.queue.duplicate.count",
                    Interlocked.Increment(ref _duplicateCount),
                    GetKeyScope(key));
                return existing.Completion.Task;
            }

            if (existing != null)
            {
                var wasPending = !existing.IsActive && existing.Node?.List != null;
                RemovePendingLocked(existing);
                if (_workByKey.TryGetValue(key, out var current) && ReferenceEquals(current, existing))
                {
                    _workByKey.Remove(key);
                }

                if (wasPending)
                {
                    (cancelledPending ??= new List<WorkItem>()).Add(existing);
                }
            }

            RemoveCancelledPendingLocked(ref cancelledPending);
            if (_pending.Count >= _pendingCapacity)
            {
                dropped = _pending.First!.Value;
                RemovePendingLocked(dropped);
                if (_workByKey.TryGetValue(dropped.Key, out var current) && ReferenceEquals(current, dropped))
                {
                    _workByKey.Remove(dropped.Key);
                }
            }

            item = new WorkItem(key, work, cancellationToken);
            item.Node = _pending.AddLast(item);
            _workByKey[key] = item;
            item.CancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    var registrationState = (CancellationState)state!;
                    registrationState.Owner.CancelItem(registrationState.Item);
                },
                new CancellationState(this, item));

            var pendingCount = _pending.Count;
            UpdateMaximum(ref _pendingHighWater, pendingCount);
            PerformanceTrace.Mark("tmdb.queue.pending.high_water", Volatile.Read(ref _pendingHighWater));
            PerformanceTrace.Mark(
                "tmdb.queue.scheduled.count",
                Interlocked.Increment(ref _scheduledCount),
                GetKeyScope(key));

            // Signal while admission and shutdown are mutually exclusive. Once this lock is
            // released, DisposeAsync may complete and dispose the semaphore immediately.
            _workSignal.Release();
        }

        if (cancelledPending != null)
        {
            foreach (var cancelled in cancelledPending)
            {
                CompleteCancelled(cancelled);
                cancelled.CancellationRegistration.Unregister();
            }
        }

        if (dropped != null)
        {
            CompleteDropped(dropped);
        }

        return item.Completion.Task;
    }

    private async Task RunWorkerAsync(int workerIndex)
    {
        try
        {
            while (true)
            {
                await _workSignal.WaitAsync(_shutdown.Token).ConfigureAwait(false);

                WorkItem? item;
                List<CancellationTokenRegistration>? cancelledRegistrations;
                lock (_gate)
                {
                    item = TakeNextPendingLocked(out cancelledRegistrations);
                    if (item != null)
                    {
                        item.IsActive = true;
                    }
                }

                if (cancelledRegistrations != null)
                {
                    foreach (var registration in cancelledRegistrations)
                    {
                        registration.Dispose();
                    }
                }

                if (item == null)
                {
                    continue;
                }

                if (item.CancellationToken.IsCancellationRequested)
                {
                    CompleteCancelled(item);
                    FinishItem(item);
                    continue;
                }

                var active = Interlocked.Increment(ref _activeWorkers);
                UpdateMaximum(ref _activeHighWater, active);
                PerformanceTrace.Mark("tmdb.queue.active.high_water", Volatile.Read(ref _activeHighWater));
                PerformanceTrace.Mark(
                    "tmdb.queue.started.count",
                    Interlocked.Increment(ref _startedCount),
                    GetKeyScope(item.Key));

                try
                {
                    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        item.CancellationToken,
                        _shutdown.Token);
                    await item.Work(linkedCancellation.Token).ConfigureAwait(false);
                    if (_interRequestDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(_interRequestDelay, linkedCancellation.Token).ConfigureAwait(false);
                    }

                    CompleteSucceeded(item);
                }
                catch (OperationCanceledException)
                    when (item.CancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
                {
                    CompleteCancelled(item);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "TMDB enrichment worker {Worker} failed for {Key}", workerIndex, item.Key);
                    CompleteFailed(item, ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeWorkers);
                    FinishItem(item);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }

    private WorkItem? TakeNextPendingLocked(
        out List<CancellationTokenRegistration>? cancelledRegistrations)
    {
        cancelledRegistrations = null;
        while (_pending.First != null)
        {
            var item = _pending.First.Value;
            RemovePendingLocked(item);
            if (item.CancellationToken.IsCancellationRequested)
            {
                CompleteCancelled(item);
                if (_workByKey.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
                {
                    _workByKey.Remove(item.Key);
                }
                (cancelledRegistrations ??= new()).Add(item.CancellationRegistration);
                continue;
            }

            return item;
        }

        return null;
    }

    private void CancelItem(WorkItem item)
    {
        lock (_gate)
        {
            // A started item's returned task represents the complete worker lifetime.
            // Its linked token will stop the work; the worker owns terminal completion.
            if (item.IsActive || item.Node?.List == null)
            {
                return;
            }

            RemovePendingLocked(item);
            if (_workByKey.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
            {
                _workByKey.Remove(item.Key);
            }
        }

        CompleteCancelled(item);
    }

    private void RemoveCancelledPendingLocked(ref List<WorkItem>? cancelledPending)
    {
        var node = _pending.First;
        while (node != null)
        {
            var next = node.Next;
            var item = node.Value;
            if (item.CancellationToken.IsCancellationRequested)
            {
                RemovePendingLocked(item);
                if (_workByKey.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
                {
                    _workByKey.Remove(item.Key);
                }

                (cancelledPending ??= new List<WorkItem>()).Add(item);
            }

            node = next;
        }
    }

    private void RemovePendingLocked(WorkItem item)
    {
        if (item.Node?.List != null)
        {
            _pending.Remove(item.Node);
        }

        item.Node = null;
    }

    private void FinishItem(WorkItem item)
    {
        lock (_gate)
        {
            item.IsActive = false;
            if (_workByKey.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
            {
                _workByKey.Remove(item.Key);
            }
        }

        item.CancellationRegistration.Dispose();
    }

    private void CompleteSucceeded(WorkItem item)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }

        item.Completion.TrySetResult();
        PerformanceTrace.Mark(
            "tmdb.queue.completed.count",
            Interlocked.Increment(ref _completedCount),
            GetKeyScope(item.Key));
    }

    private void CompleteCancelled(WorkItem item)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }

        item.Completion.TrySetCanceled(item.CancellationToken);
        PerformanceTrace.Mark(
            "tmdb.queue.cancelled.count",
            Interlocked.Increment(ref _cancelledCount),
            GetKeyScope(item.Key));
    }

    private void CompleteDropped(WorkItem item)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }

        item.CancellationRegistration.Dispose();
        item.Completion.TrySetResult();
        PerformanceTrace.Mark(
            "tmdb.queue.dropped.count",
            Interlocked.Increment(ref _droppedCount),
            GetKeyScope(item.Key));
    }

    private void CompleteFailed(WorkItem item, Exception exception)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }

        item.Completion.TrySetException(exception);
        PerformanceTrace.Mark(
            "tmdb.queue.failed.count",
            Interlocked.Increment(ref _failedCount),
            GetKeyScope(item.Key));
    }

    private static string GetKeyScope(string key)
    {
        var separator = key.IndexOf(':');
        return separator > 0 ? key[..separator] : key;
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        BeginShutdown();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            BeginShutdown();
        }

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Expected during shutdown.
        }

        _workSignal.Dispose();
        _shutdown.Dispose();
    }

    private void BeginShutdown()
    {
        List<WorkItem> retained;
        lock (_gate)
        {
            retained = _pending.ToList();
            _pending.Clear();
            foreach (var item in retained)
            {
                item.Node = null;
                if (_workByKey.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
                {
                    _workByKey.Remove(item.Key);
                }
            }
        }

        foreach (var item in retained)
        {
            CompleteCancelled(item);
            item.CancellationRegistration.Dispose();
        }

        _shutdown.Cancel();
    }

    private sealed class WorkItem
    {
        private int _terminalState;

        public WorkItem(
            string key,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            Key = key;
            Work = work;
            CancellationToken = cancellationToken;
        }

        public string Key { get; }
        public Func<CancellationToken, Task> Work { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<WorkItem>? Node { get; set; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public bool IsActive { get; set; }

        public bool TrySetTerminal()
            => Interlocked.Exchange(ref _terminalState, 1) == 0;
    }

    private sealed record CancellationState(TmdbEnrichmentScheduler Owner, WorkItem Item);
}
