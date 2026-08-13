using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class ContentQueryServiceSchedulingTests
{
    [Fact]
    public async Task PendingCancelledQuery_DoesNotInvokeProvider()
    {
        await using var scheduler = new DatabaseWorkScheduler(
            new DatabaseWorkSchedulerOptions(ReadConcurrency: 1, WriteConcurrency: 1));
        var blockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = scheduler.ScheduleAsync(
            DatabaseWorkLane.Read,
            DatabaseWorkPriority.Interactive,
            async token =>
            {
                blockerStarted.TrySetResult();
                await releaseBlocker.Task.WaitAsync(token);
                return 0;
            });
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var providerInvocations = 0;
        var playlistService = new Mock<IPlaylistService>();
        playlistService
            .Setup(service => service.GetChannelGroupMetadataAsync(17, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref providerInvocations);
                return Task.FromResult((
                    0,
                    new List<string>(),
                    new List<string>(),
                    new List<string>(),
                    new List<string>()));
            });
        var service = CreateService(playlistService.Object, scheduler);
        using var cancellation = new CancellationTokenSource();

        var query = service.GetChannelGroupMetadataAsync(17, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.Equal(0, Volatile.Read(ref providerInvocations));
        releaseBlocker.TrySetResult();
        await blocker;
    }

    [Fact]
    public async Task GetChannelGroupMetadataAsync_RunsSqliteWorkOffTheCallerThread()
    {
        var invocationThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var playlistService = new Mock<IPlaylistService>();
        playlistService
            .Setup(service => service.GetChannelGroupMetadataAsync(17, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                invocationThread.TrySetResult(Environment.CurrentManagedThreadId);
                await releaseQuery.Task;
                return (0, new List<string>(), new List<string>(), new List<string>(), new List<string>());
            });

        await using var scheduler = new DatabaseWorkScheduler();
        var service = CreateService(playlistService.Object, scheduler);

        var callReturned = new ManualResetEventSlim();
        Task? queryTask = null;
        var callerThreadId = 0;
        var callerThread = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            queryTask = service.GetChannelGroupMetadataAsync(17);
            callReturned.Set();
        });

        callerThread.Start();
        Assert.True(callReturned.Wait(TimeSpan.FromSeconds(2)));

        try
        {
            var queryThreadId = await invocationThread.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual(callerThreadId, queryThreadId);
        }
        finally
        {
            releaseQuery.TrySetResult(true);
            if (queryTask is not null)
            {
                await queryTask;
            }

            callerThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private static ContentQueryService CreateService(
        IPlaylistService playlistService,
        IDatabaseWorkScheduler scheduler)
        => new(
            playlistService,
            Mock.Of<IMediaService>(),
            Mock.Of<ISettingsService>(),
            Mock.Of<IDbContextFactory<AppDbContext>>(),
            scheduler);
}
