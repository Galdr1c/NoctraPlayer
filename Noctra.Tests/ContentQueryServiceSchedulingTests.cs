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

        var service = new ContentQueryService(
            playlistService.Object,
            Mock.Of<IMediaService>(),
            Mock.Of<ISettingsService>(),
            Mock.Of<IDbContextFactory<AppDbContext>>());

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
}
