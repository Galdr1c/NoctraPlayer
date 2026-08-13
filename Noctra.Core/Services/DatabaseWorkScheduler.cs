using Noctra.Diagnostics;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public enum DatabaseWorkLane
{
    Read,
    Write
}

public enum DatabaseWorkPriority
{
    Interactive,
    Background
}

public sealed record DatabaseWorkSchedulerOptions(
    int PendingCapacity = 128,
    int ReadConcurrency = 3,
    int WriteConcurrency = 1,
    int InteractiveBurstLimit = 4);

public sealed class DatabaseWorkScheduler : IDatabaseWorkScheduler, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly LaneState _readLane = new();
    private readonly LaneState _writeLane = new();
    private readonly SemaphoreSlim _readSignal = new(0);
    private readonly SemaphoreSlim _writeSignal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private readonly int _pendingCapacity;
    private readonly int _interactiveBurstLimit;

    private int _disposeState;
    private int _asyncCleanupState;
    private int _pendingCount;
    private int _pendingHighWater;
    private int _activeReads;
    private int _activeWrites;
    private int _activeReadHighWater;
    private int _activeWriteHighWater;
    private long _scheduledCount;
    private long _completedCount;
    private long _cancelledCount;
    private long _failedCount;
    private long _rejectedCount;
    private long _shutdownCallbackFailureCount;

    public DatabaseWorkScheduler(DatabaseWorkSchedulerOptions? options = null)
    {
        options ??= new DatabaseWorkSchedulerOptions();
        if (options.PendingCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Pending capacity must be positive.");
        }
        if (options.ReadConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Read concurrency must be positive.");
        }
        if (options.WriteConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Write concurrency must be positive.");
        }
        if (options.InteractiveBurstLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Interactive burst limit must be positive.");
        }

        _pendingCapacity = options.PendingCapacity;
        _interactiveBurstLimit = options.InteractiveBurstLimit;
        _workers = Enumerable.Range(0, options.ReadConcurrency)
            .Select(_ => Task.Run(() => RunWorkerAsync(DatabaseWorkLane.Read)))
            .Concat(Enumerable.Range(0, options.WriteConcurrency)
                .Select(_ => Task.Run(() => RunWorkerAsync(DatabaseWorkLane.Write))))
            .ToArray();
    }

    internal static DatabaseWorkScheduler Shared { get; } = new();

    public Task<T> ScheduleAsync<T>(
        DatabaseWorkLane lane,
        DatabaseWorkPriority priority,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!Enum.IsDefined(lane))
        {
            throw new ArgumentOutOfRangeException(nameof(lane));
        }
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var item = new WorkItem<T>(lane, priority, work, cancellationToken);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return Task.FromException<T>(new ObjectDisposedException(nameof(DatabaseWorkScheduler)));
            }

            if (_pendingCount >= _pendingCapacity)
            {
                PerformanceTrace.Mark(
                    "db.queue.rejected.count",
                    Interlocked.Increment(ref _rejectedCount),
                    GetScope(lane, priority));
                return Task.FromException<T>(new InvalidOperationException("Database work queue capacity was reached."));
            }

            var laneState = GetLaneState(lane);
            var queue = priority == DatabaseWorkPriority.Interactive
                ? laneState.Interactive
                : laneState.Background;
            item.Node = queue.AddLast(item);
            _pendingCount++;
            UpdateMaximum(ref _pendingHighWater, _pendingCount);
            item.CancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    var cancellationState = (CancellationState)state!;
                    cancellationState.Owner.CancelPending(cancellationState.Item);
                },
                new CancellationState(this, item));

            PerformanceTrace.Mark("db.queue.pending.high_water", Volatile.Read(ref _pendingHighWater));
            PerformanceTrace.Mark(
                "db.queue.scheduled.count",
                Interlocked.Increment(ref _scheduledCount),
                GetScope(lane, priority));

            GetSignal(lane).Release();
        }

        return item.TypedCompletion.Task;
    }

    private async Task RunWorkerAsync(DatabaseWorkLane lane)
    {
        try
        {
            while (true)
            {
                await GetSignal(lane).WaitAsync(_shutdown.Token).ConfigureAwait(false);

                WorkItem? item;
                lock (_gate)
                {
                    item = TakeNextLocked(GetLaneState(lane));
                    if (item != null)
                    {
                        item.IsActive = true;
                        _pendingCount--;
                    }
                }

                if (item == null)
                {
                    continue;
                }

                item.CancellationRegistration.Unregister();
                if (item.CancellationToken.IsCancellationRequested)
                {
                    CompleteCancelled(item, item.CancellationToken);
                    continue;
                }

                var active = lane == DatabaseWorkLane.Read
                    ? Interlocked.Increment(ref _activeReads)
                    : Interlocked.Increment(ref _activeWrites);
                if (lane == DatabaseWorkLane.Read)
                {
                    UpdateMaximum(ref _activeReadHighWater, active);
                    PerformanceTrace.Mark("db.queue.read.active.high_water", Volatile.Read(ref _activeReadHighWater));
                }
                else
                {
                    UpdateMaximum(ref _activeWriteHighWater, active);
                    PerformanceTrace.Mark("db.queue.write.active.high_water", Volatile.Read(ref _activeWriteHighWater));
                }

                try
                {
                    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        item.CancellationToken,
                        _shutdown.Token);
                    await item.ExecuteAsync(linkedCancellation.Token).ConfigureAwait(false);
                    CompleteSucceeded(item);
                }
                catch (OperationCanceledException)
                    when (item.CancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
                {
                    CompleteCancelled(
                        item,
                        item.CancellationToken.IsCancellationRequested
                            ? item.CancellationToken
                            : _shutdown.Token);
                }
                catch (Exception exception)
                {
                    CompleteFailed(item, exception);
                }
                finally
                {
                    item.IsActive = false;
                    if (lane == DatabaseWorkLane.Read)
                    {
                        Interlocked.Decrement(ref _activeReads);
                    }
                    else
                    {
                        Interlocked.Decrement(ref _activeWrites);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Expected application shutdown.
        }
    }

    private WorkItem? TakeNextLocked(LaneState lane)
    {
        while (true)
        {
            LinkedList<WorkItem>? queue;
            if (lane.Interactive.Count > 0 &&
                (lane.Background.Count == 0 || lane.InteractiveBurst < _interactiveBurstLimit))
            {
                queue = lane.Interactive;
                lane.InteractiveBurst++;
            }
            else if (lane.Background.Count > 0)
            {
                queue = lane.Background;
                lane.InteractiveBurst = 0;
            }
            else if (lane.Interactive.Count > 0)
            {
                queue = lane.Interactive;
                lane.InteractiveBurst++;
            }
            else
            {
                return null;
            }

            var item = queue.First!.Value;
            queue.RemoveFirst();
            item.Node = null;
            if (!item.CancellationToken.IsCancellationRequested)
            {
                return item;
            }

            _pendingCount--;
            item.CancellationRegistration.Unregister();
            CompleteCancelled(item, item.CancellationToken);
        }
    }

    private void CancelPending(WorkItem item)
    {
        var removed = false;
        lock (_gate)
        {
            if (!item.IsActive && item.Node?.List != null)
            {
                item.Node.List.Remove(item.Node);
                item.Node = null;
                _pendingCount--;
                removed = true;
            }
        }

        if (removed)
        {
            CompleteCancelled(item, item.CancellationToken);
            item.CancellationRegistration.Unregister();
        }
    }

    private void CompleteSucceeded(WorkItem item)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }
        item.CompleteResult();
        PerformanceTrace.Mark(
            "db.queue.completed.count",
            Interlocked.Increment(ref _completedCount),
            GetScope(item.Lane, item.Priority));
    }

    private void CompleteCancelled(WorkItem item, CancellationToken cancellationToken)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }
        item.CompleteCancelled(cancellationToken);
        PerformanceTrace.Mark(
            "db.queue.cancelled.count",
            Interlocked.Increment(ref _cancelledCount),
            GetScope(item.Lane, item.Priority));
    }

    private void CompleteFailed(WorkItem item, Exception exception)
    {
        if (!item.TrySetTerminal())
        {
            return;
        }
        item.CompleteException(exception);
        PerformanceTrace.Mark(
            "db.queue.failed.count",
            Interlocked.Increment(ref _failedCount),
            GetScope(item.Lane, item.Priority));
    }

    private LaneState GetLaneState(DatabaseWorkLane lane)
        => lane == DatabaseWorkLane.Read ? _readLane : _writeLane;

    private SemaphoreSlim GetSignal(DatabaseWorkLane lane)
        => lane == DatabaseWorkLane.Read ? _readSignal : _writeSignal;

    private static string GetScope(DatabaseWorkLane lane, DatabaseWorkPriority priority)
        => $"{lane.ToString().ToLowerInvariant()}:{priority.ToString().ToLowerInvariant()}";

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
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            BeginShutdown();
        }
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
            // Expected application shutdown.
        }

        if (Interlocked.Exchange(ref _asyncCleanupState, 1) == 0)
        {
            _readSignal.Dispose();
            _writeSignal.Dispose();
            _shutdown.Dispose();
        }
    }

    private void BeginShutdown()
    {
        List<WorkItem> pending;
        lock (_gate)
        {
            pending = _readLane.Interactive
                .Concat(_readLane.Background)
                .Concat(_writeLane.Interactive)
                .Concat(_writeLane.Background)
                .ToList();
            _readLane.Clear();
            _writeLane.Clear();
            _pendingCount = 0;
            foreach (var item in pending)
            {
                item.Node = null;
            }
        }

        try
        {
            _shutdown.Cancel();
        }
        catch (Exception exception)
        {
            // A consumer can register a callback on the linked worker token that
            // throws during cancellation. Shutdown must still terminalize every
            // item already removed from the queues; never let callback behavior
            // strand a task outside both the queue and a worker.
            PerformanceTrace.Mark(
                "db.queue.shutdown_callback_failed.count",
                Interlocked.Increment(ref _shutdownCallbackFailureCount),
                exception.GetType().Name);
        }
        finally
        {
            foreach (var item in pending)
            {
                CompleteCancelled(item, _shutdown.Token);
                item.CancellationRegistration.Unregister();
            }
        }
    }

    private sealed class LaneState
    {
        public LinkedList<WorkItem> Interactive { get; } = new();
        public LinkedList<WorkItem> Background { get; } = new();
        public int InteractiveBurst { get; set; }

        public void Clear()
        {
            Interactive.Clear();
            Background.Clear();
        }
    }

    private abstract class WorkItem
    {
        private int _terminalState;

        protected WorkItem(
            DatabaseWorkLane lane,
            DatabaseWorkPriority priority,
            CancellationToken cancellationToken)
        {
            Lane = lane;
            Priority = priority;
            CancellationToken = cancellationToken;
        }

        public DatabaseWorkLane Lane { get; }
        public DatabaseWorkPriority Priority { get; }
        public CancellationToken CancellationToken { get; }
        public LinkedListNode<WorkItem>? Node { get; set; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public bool IsActive { get; set; }

        public bool TrySetTerminal() => Interlocked.Exchange(ref _terminalState, 1) == 0;
        public abstract Task ExecuteAsync(CancellationToken cancellationToken);
        public abstract void CompleteResult();
        public abstract void CompleteCancelled(CancellationToken cancellationToken);
        public abstract void CompleteException(Exception exception);
    }

    private sealed class WorkItem<T> : WorkItem
    {
        private readonly Func<CancellationToken, Task<T>> _work;
        private T? _result;

        public WorkItem(
            DatabaseWorkLane lane,
            DatabaseWorkPriority priority,
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken)
            : base(lane, priority, cancellationToken)
        {
            _work = work;
        }

        public TaskCompletionSource<T> TypedCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task ExecuteAsync(CancellationToken cancellationToken)
            => _result = await _work(cancellationToken).ConfigureAwait(false);

        public override void CompleteResult() => TypedCompletion.TrySetResult(_result!);
        public override void CompleteCancelled(CancellationToken cancellationToken)
            => TypedCompletion.TrySetCanceled(cancellationToken);
        public override void CompleteException(Exception exception)
            => TypedCompletion.TrySetException(exception);
    }

    private sealed record CancellationState(DatabaseWorkScheduler Owner, WorkItem Item);
}
