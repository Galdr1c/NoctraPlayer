using System.Collections.Concurrent;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class TmdbEnrichmentSchedulerTests
{
    [Fact]
    public async Task MultipleBatches_ShareOneGlobalConcurrencyLimit()
    {
        await using var scheduler = new TmdbEnrichmentScheduler(
            new TmdbEnrichmentSchedulerOptions(
                PendingCapacity: 32,
                MaxConcurrency: 3,
                InterRequestDelay: TimeSpan.Zero));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var threeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        var started = 0;

        var tasks = Enumerable.Range(0, 12)
            .Select(index => scheduler.ScheduleAsync(
                $"batch:{index % 4}:item:{index}",
                async token =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref peak, current);
                    if (Interlocked.Increment(ref started) == 3)
                    {
                        threeStarted.TrySetResult();
                    }

                    try
                    {
                        await release.Task.WaitAsync(token);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }))
            .ToArray();

        await threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(3, Volatile.Read(ref peak));

        release.TrySetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(12, Volatile.Read(ref started));
        Assert.Equal(0, Volatile.Read(ref active));
    }

    [Fact]
    public async Task FullQueue_DropsOldestPendingItemAndRunsNewestWork()
    {
        await using var scheduler = new TmdbEnrichmentScheduler(
            new TmdbEnrichmentSchedulerOptions(
                PendingCapacity: 2,
                MaxConcurrency: 1,
                InterRequestDelay: TimeSpan.Zero));
        var activeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new ConcurrentQueue<int>();

        var active = scheduler.ScheduleAsync("item:1", async token =>
        {
            executed.Enqueue(1);
            activeStarted.TrySetResult();
            await releaseActive.Task.WaitAsync(token);
        });
        await activeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var oldestPending = scheduler.ScheduleAsync("item:2", _ =>
        {
            executed.Enqueue(2);
            return Task.CompletedTask;
        });
        var middlePending = scheduler.ScheduleAsync("item:3", _ =>
        {
            executed.Enqueue(3);
            return Task.CompletedTask;
        });
        var newestPending = scheduler.ScheduleAsync("item:4", _ =>
        {
            executed.Enqueue(4);
            return Task.CompletedTask;
        });

        await oldestPending.WaitAsync(TimeSpan.FromSeconds(2));
        releaseActive.TrySetResult();
        await Task.WhenAll(active, middlePending, newestPending).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([1, 3, 4], executed.ToArray());
    }

    [Fact]
    public async Task Cancellation_RemovesQueuedWorkAndStopsStartedWork()
    {
        await using var scheduler = new TmdbEnrichmentScheduler(
            new TmdbEnrichmentSchedulerOptions(
                PendingCapacity: 4,
                MaxConcurrency: 1,
                InterRequestDelay: TimeSpan.Zero));
        using var startedCancellation = new CancellationTokenSource();
        using var queuedCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedTokenCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedExecuted = 0;

        var startedTask = scheduler.ScheduleAsync("started", async token =>
        {
            using var registration = token.Register(() => startedTokenCancelled.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, startedCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var queuedTask = scheduler.ScheduleAsync("queued", _ =>
        {
            Interlocked.Exchange(ref queuedExecuted, 1);
            return Task.CompletedTask;
        }, queuedCancellation.Token);

        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await queuedTask.WaitAsync(TimeSpan.FromSeconds(2)));

        startedCancellation.Cancel();
        await startedTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await startedTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(0, Volatile.Read(ref queuedExecuted));
    }

    [Fact]
    public async Task StartedCancellation_CompletesReturnedTaskOnlyAfterWorkerUnwinds()
    {
        await using var scheduler = new TmdbEnrichmentScheduler(
            new TmdbEnrichmentSchedulerOptions(
                PendingCapacity: 4,
                MaxConcurrency: 1,
                InterRequestDelay: TimeSpan.Zero));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var scheduled = scheduler.ScheduleAsync("started-cleanup", async token =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                await allowCleanup.Task;
                throw;
            }
        }, cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var completedBeforeCleanup = scheduled.IsCompleted;
        allowCleanup.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await scheduled.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(completedBeforeCleanup);
    }

    [Fact]
    public async Task ConcurrentScheduleAndDispose_NeverLeaksDisposedSignalException()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var scheduler = new TmdbEnrichmentScheduler(
                new TmdbEnrichmentSchedulerOptions(
                    PendingCapacity: 8,
                    MaxConcurrency: 1,
                    InterRequestDelay: TimeSpan.Zero));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new ConcurrentQueue<ObjectDisposedException>();

            var scheduling = Enumerable.Range(0, 16)
                .Select(index => Task.Run(async () =>
                {
                    await start.Task;
                    try
                    {
                        var admitted = scheduler.ScheduleAsync(
                            $"dispose-race:{iteration}:{index}",
                            token => release.Task.WaitAsync(token));
                        await admitted;
                    }
                    catch (OperationCanceledException)
                    {
                        // Disposal cancels work that won admission.
                    }
                    catch (ObjectDisposedException ex)
                    {
                        errors.Enqueue(ex);
                    }
                }))
                .ToArray();

            var disposing = Task.Run(async () =>
            {
                await start.Task;
                await scheduler.DisposeAsync();
            });

            start.TrySetResult();
            release.TrySetResult();
            await Task.WhenAll(scheduling.Append(disposing)).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.DoesNotContain(errors, error =>
                string.Equals(
                    error.ObjectName,
                    typeof(SemaphoreSlim).FullName,
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PendingCancellationRacingWorkerDequeue_DoesNotDeadlock()
    {
        var scheduler = new TmdbEnrichmentScheduler(
            new TmdbEnrichmentSchedulerOptions(
                PendingCapacity: 4,
                MaxConcurrency: 1,
                InterRequestDelay: TimeSpan.Zero));
        var activeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = scheduler.ScheduleAsync("deadlock-active", async token =>
        {
            activeStarted.TrySetResult();
            await releaseActive.Task.WaitAsync(token);
        });
        await activeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var blockerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseBlocker = new ManualResetEventSlim();
        using var blockerRegistration = cancellation.Token.Register(() =>
        {
            blockerEntered.TrySetResult();
            releaseBlocker.Wait();
        });
        var pending = scheduler.ScheduleAsync(
            "deadlock-pending",
            _ => Task.CompletedTask,
            cancellation.Token);

        var cancelling = Task.Run(cancellation.Cancel);
        await blockerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseActive.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(2)));

        var cancellationFinished = false;
        try
        {
            releaseBlocker.Set();
            await cancelling.WaitAsync(TimeSpan.FromSeconds(2));
            cancellationFinished = true;
        }
        finally
        {
            releaseBlocker.Set();
            if (cancellationFinished)
            {
                await active.WaitAsync(TimeSpan.FromSeconds(2));
                await scheduler.DisposeAsync();
            }
        }

        Assert.True(cancellationFinished);
    }

    [Fact]
    public async Task CancelledPendingKeyReplacedDuringCallbackWait_CompletesOldTask()
    {
        await using var scheduler = new TmdbEnrichmentScheduler(
            new TmdbEnrichmentSchedulerOptions(
                PendingCapacity: 4,
                MaxConcurrency: 1,
                InterRequestDelay: TimeSpan.Zero));
        var activeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = scheduler.ScheduleAsync("replacement-active", async token =>
        {
            activeStarted.TrySetResult();
            await releaseActive.Task.WaitAsync(token);
        });
        await activeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var oldTask = scheduler.ScheduleAsync(
            "replacement-key",
            _ => Task.CompletedTask,
            cancellation.Token);
        var gateField = typeof(TmdbEnrichmentScheduler).GetField(
            "_gate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(gateField);
        var gate = gateField.GetValue(scheduler);
        Assert.NotNull(gate);

        Task? cancelling = null;
        Task? replacement = null;
        Monitor.Enter(gate);
        try
        {
            cancelling = Task.Run(cancellation.Cancel);
            Assert.True(SpinWait.SpinUntil(
                () => cancellation.IsCancellationRequested,
                TimeSpan.FromSeconds(2)));
            replacement = scheduler.ScheduleAsync(
                "replacement-key",
                _ => Task.CompletedTask);
        }
        finally
        {
            Monitor.Exit(gate);
        }

        await cancelling.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await oldTask.WaitAsync(TimeSpan.FromSeconds(2)));

        releaseActive.TrySetResult();
        await Task.WhenAll(active, replacement).WaitAsync(TimeSpan.FromSeconds(2));
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
