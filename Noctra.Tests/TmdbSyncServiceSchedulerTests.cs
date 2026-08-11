using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class TmdbSyncServiceSchedulerTests
{
    [Fact]
    public async Task OverlappingBatches_ShareInjectedSchedulerConcurrency()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"Noctra-TmdbScheduler-{Guid.NewGuid():N}.db");
        try
        {
            var factory = CreateM3uFactory(databasePath);
            await using var scheduler = new TmdbEnrichmentScheduler(
                new TmdbEnrichmentSchedulerOptions(
                    PendingCapacity: 32,
                    MaxConcurrency: 2,
                    InterRequestDelay: TimeSpan.Zero));
            using var cancellation = new CancellationTokenSource();
            var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
            var dispatcher = new Mock<IDispatcherService>(MockBehavior.Strict);
            var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var active = 0;
            var peak = 0;
            var started = 0;

            metadata
                .Setup(service => service.SearchSeriesAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (string _, string? _, CancellationToken token) =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref peak, current);
                    if (Interlocked.Increment(ref started) == 2)
                    {
                        twoStarted.TrySetResult();
                    }

                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        return new ChannelMetadata();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                });

            var service = new TmdbSyncService(
                factory,
                metadata.Object,
                dispatcher.Object,
                scheduler);
            var firstBatch = CreateSeries(100, 4);
            var secondBatch = CreateSeries(200, 4);

            var first = service.EnrichSeriesBatchAsync(firstBatch, cancellation.Token);
            var second = service.EnrichSeriesBatchAsync(secondBatch, cancellation.Token);

            await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, Volatile.Read(ref peak));

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2)));

            Assert.Equal(2, Volatile.Read(ref started));
            Assert.Equal(0, Volatile.Read(ref active));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CancellationBeforeQueuedDispatcherCommit_RejectsStaleModelMutation()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"Noctra-TmdbCommit-{Guid.NewGuid():N}.db");
        try
        {
            var factory = CreateM3uFactory(databasePath);
            using (var db = factory.CreateDbContext())
            {
                db.Series.Add(new Series
                {
                    Id = 301,
                    PlaylistId = 7,
                    Name = "Queued callback series",
                    GroupTitle = "Series"
                });
                db.SaveChanges();
            }

            await using var scheduler = new TmdbEnrichmentScheduler(
                new TmdbEnrichmentSchedulerOptions(
                    PendingCapacity: 8,
                    MaxConcurrency: 1,
                    InterRequestDelay: TimeSpan.Zero));
            using var cancellation = new CancellationTokenSource();
            var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
            metadata
                .Setup(service => service.SearchSeriesAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChannelMetadata
                {
                    TmdbId = 9301,
                    Title = "TMDB title",
                    PosterUrl = "https://image.test/poster.jpg"
                });

            var queuedCommit = new TaskCompletionSource<Func<Task>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcherRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = new Mock<IDispatcherService>(MockBehavior.Strict);
            dispatcher
                .Setup(service => service.BeginInvoke(It.IsAny<Action>()))
                .Callback<Action>(action => queuedCommit.TrySetResult(() =>
                {
                    action();
                    return Task.CompletedTask;
                }));
            dispatcher
                .Setup(service => service.InvokeAsync(It.IsAny<Func<Task>>()))
                .Returns((Func<Task> action) =>
                {
                    queuedCommit.TrySetResult(action);
                    return dispatcherRelease.Task;
                });

            var service = new TmdbSyncService(
                factory,
                metadata.Object,
                dispatcher.Object,
                scheduler);
            var visibleSeries = new Series
            {
                Id = 301,
                PlaylistId = 7,
                Name = "Queued callback series",
                GroupTitle = "Series"
            };

            var enrichment = service.EnrichSeriesBatchAsync([visibleSeries], cancellation.Token);
            var commit = await queuedCommit.Task.WaitAsync(TimeSpan.FromSeconds(2));

            cancellation.Cancel();
            await commit();
            dispatcherRelease.TrySetResult();
            try
            {
                await enrichment.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
                // Expected when the scheduler observes the cancelled navigation token.
            }

            Assert.Null(visibleSeries.TmdbId);
            Assert.Null(visibleSeries.CoverUrl);
            Assert.Null(visibleSeries.LastTmdbSync);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ItemFailure_PropagatesToSchedulerTaskForFailureTelemetry()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"Noctra-TmdbFailure-{Guid.NewGuid():N}.db");
        try
        {
            var factory = CreateM3uFactory(databasePath);
            await using var scheduler = new TmdbEnrichmentScheduler(
                new TmdbEnrichmentSchedulerOptions(
                    PendingCapacity: 8,
                    MaxConcurrency: 1,
                    InterRequestDelay: TimeSpan.Zero));
            var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
            metadata
                .Setup(service => service.SearchSeriesAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("TMDB test failure"));
            var service = new TmdbSyncService(
                factory,
                metadata.Object,
                new Mock<IDispatcherService>(MockBehavior.Strict).Object,
                scheduler);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.EnrichSeriesBatchAsync(CreateSeries(500, 1)));

            Assert.Equal("TMDB test failure", failure.Message);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static IDbContextFactory<AppDbContext> CreateM3uFactory(string databasePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var account = new ProviderAccount
        {
            Id = 1,
            Name = "M3U account",
            Type = ProfileType.M3U,
            Url = "https://playlist.test/list.m3u"
        };
        var profile = new Profile
        {
            Id = 1,
            Name = "M3U profile",
            ProviderAccount = account
        };
        db.Playlists.Add(new Playlist
        {
            Id = 7,
            Name = "M3U playlist",
            Profile = profile
        });
        db.SaveChanges();
        return new FileDbContextFactory(options);
    }

    private static List<Series> CreateSeries(int firstId, int count)
        => Enumerable.Range(firstId, count)
            .Select(id => new Series
            {
                Id = id,
                PlaylistId = 7,
                Name = $"Series {id}",
                GroupTitle = "Series"
            })
            .ToList();

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

    private sealed class FileDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
            => new(options);
    }
}
