using Noctra.Services;

namespace Noctra.Tests;

public sealed class DatabaseWorkSchedulerTests
{
    [Fact]
    public async Task ReadAndWriteLanes_RespectIndependentConcurrencyLimits()
    {
        await using var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(ReadConcurrency: 3, WriteConcurrency: 1));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeReads = 0;
        var peakReads = 0;
        var activeWrites = 0;
        var peakWrites = 0;
        var readStarts = 0;

        var reads = Enumerable.Range(0, 8).Select(_ => scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                var active = Interlocked.Increment(ref activeReads);
                UpdateMaximum(ref peakReads, active);
                if (Interlocked.Increment(ref readStarts) == 3)
                {
                    readsStarted.TrySetResult();
                }
                await release.Task.WaitAsync(token);
                Interlocked.Decrement(ref activeReads);
                return 1;
            })).ToArray();
        var writes = Enumerable.Range(0, 4).Select(_ => scheduler.ScheduleAsync(
            DatabaseWorkLane.Write,
            DatabaseWorkPriority.Background,
            async token =>
            {
                var active = Interlocked.Increment(ref activeWrites);
                UpdateMaximum(ref peakWrites, active);
                writeStarted.TrySetResult();
                await release.Task.WaitAsync(token);
                Interlocked.Decrement(ref activeWrites);
                return 1;
            })).ToArray();

        await Task.WhenAll(
            readsStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(3, peakReads);
        Assert.Equal(1, peakWrites);

        release.TrySetResult();
        await Task.WhenAll(reads.Concat(writes));
        Assert.Equal(3, peakReads);
        Assert.Equal(1, peakWrites);
    }

    [Fact]
    public async Task InteractivePriority_OvertakesBackground_ButDoesNotStarveIt()
    {
        await using var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(
                ReadConcurrency: 1,
                WriteConcurrency: 1,
                InteractiveBurstLimit: 2));
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var orderGate = new object();

        var first = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(token);
                return 0;
            });
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<int> Queue(string name, DatabaseWorkPriority priority)
            => scheduler.ScheduleAsync(
                DatabaseWorkLane.Read,
                priority,
                _ =>
                {
                    lock (orderGate)
                    {
                        order.Add(name);
                    }
                    return Task.FromResult(1);
                });

        var background = Queue("background", DatabaseWorkPriority.Background);
        var interactive1 = Queue("interactive-1", DatabaseWorkPriority.Interactive);
        var interactive2 = Queue("interactive-2", DatabaseWorkPriority.Interactive);
        var interactive3 = Queue("interactive-3", DatabaseWorkPriority.Interactive);
        releaseFirst.TrySetResult();

        await Task.WhenAll(first, background, interactive1, interactive2, interactive3);
        Assert.Equal("interactive-1", order[0]);
        Assert.True(order.IndexOf("background") <= 2, string.Join(",", order));
    }

    [Fact]
    public async Task CancelledPendingWork_NeverInvokesDelegate()
    {
        await using var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(ReadConcurrency: 1, WriteConcurrency: 1));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return 0;
            });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var invoked = 0;
        var cancelled = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            _ =>
            {
                Interlocked.Increment(ref invoked);
                return Task.FromResult(1);
            },
            cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(0, Volatile.Read(ref invoked));
        release.TrySetResult();
        await blocker;
    }

    [Fact]
    public async Task ActiveCancellationAndFailure_PropagateFromWorkerLifetime()
    {
        await using var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(ReadConcurrency: 1, WriteConcurrency: 1));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            },
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);

        var failed = scheduler.ScheduleAsync<int>(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            _ => throw new InvalidOperationException("db-failure"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.Equal("db-failure", exception.Message);
    }

    [Fact]
    public async Task DisposeRace_TerminalizesActivePendingAndAdmissionWaiters()
    {
        var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(
                ReadConcurrency: 1,
                WriteConcurrency: 1,
                PendingCapacity: 1));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var pending = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Background,
            _ => Task.FromResult(1));
        var admissionWaiter = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            _ => Task.FromResult(2));

        await scheduler.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<Exception>(() => admissionWaiter);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            _ => Task.FromResult(3)));
    }

    [Fact]
    public async Task ConcurrentDisposeAsync_IsIdempotent_AndEveryScheduledTaskTerminates()
    {
        var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(
                ReadConcurrency: 2,
                WriteConcurrency: 1,
                PendingCapacity: 64));
        var tasks = Enumerable.Range(0, 48)
            .Select(index => scheduler.ScheduleAsync(
                index % 4 == 0 ? DatabaseWorkLane.Write : DatabaseWorkLane.Read,
                index % 3 == 0
                    ? DatabaseWorkPriority.Background
                    : DatabaseWorkPriority.Interactive,
                async token =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), token);
                    return index;
                }))
            .ToArray();

        await Task.WhenAll(scheduler.DisposeAsync().AsTask(), scheduler.DisposeAsync().AsTask());

        foreach (var task in tasks)
        {
            var terminal = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(task, terminal);
            Assert.True(task.IsCompleted);
        }
    }

    [Fact]
    public async Task InvalidLaneOrPriority_IsRejectedBeforeAdmission()
    {
        await using var scheduler = new DatabaseWorkScheduler();

        Action invalidLane = () =>
        {
            _ = scheduler.ScheduleAsync(
                (DatabaseWorkLane)99,
                DatabaseWorkPriority.Interactive,
                _ => Task.FromResult(1));
        };
        Action invalidPriority = () =>
        {
            _ = scheduler.ScheduleAsync(
                DatabaseWorkLane.Read,
                (DatabaseWorkPriority)99,
                _ => Task.FromResult(1));
        };

        Assert.Throws<ArgumentOutOfRangeException>(invalidLane);
        Assert.Throws<ArgumentOutOfRangeException>(invalidPriority);
    }

    [Fact]
    public async Task ThrowingShutdownCallback_CannotStrandPendingWork()
    {
        var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(ReadConcurrency: 1, WriteConcurrency: 1));
        var activeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                using var registration = token.Register(
                    static () => throw new InvalidOperationException("bad cancellation callback"));
                activeStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            });
        await activeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var pendingInvocations = 0;
        var pending = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Background,
            _ =>
            {
                Interlocked.Increment(ref pendingInvocations);
                return Task.FromResult(1);
            });

        var disposeException = Record.Exception(scheduler.Dispose);

        Assert.Null(disposeException);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.Equal(0, Volatile.Read(ref pendingInvocations));
        await scheduler.DisposeAsync();
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
}
