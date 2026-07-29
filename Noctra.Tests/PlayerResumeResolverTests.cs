using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class PlayerResumeResolverTests
{
    [Fact]
    public async Task ResolveAsync_VodHistoryTakesPriorityOverCardProgress()
    {
        var history = new FakeWatchHistoryService
        {
            Latest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromMinutes(20),
                Completed = false
            }
        };
        var channel = new Channel
        {
            Id = 42,
            Type = ChannelType.VOD,
            StreamUrl = "https://example.test/movie.mp4",
            Duration = TimeSpan.FromHours(2),
            WatchedPosition = TimeSpan.FromMinutes(10)
        };

        var result = await ResolveAsync(history, 7, channel, episode: null);

        Assert.Equal(TimeSpan.FromMinutes(20).TotalSeconds, result);
        Assert.Equal((7, 42, (int?)null), history.LastLookup);
    }

    [Theory]
    [InlineData(120, 6000, false, true, false)]
    [InlineData(600, 6000, true, true, false)]
    [InlineData(5700, 6000, false, true, false)]
    [InlineData(5980, 6000, false, true, false)]
    [InlineData(600, 6000, false, false, false)]
    [InlineData(600, 6000, false, true, true)]
    public async Task ResolveAsync_VodAppliesResumeEligibilityPolicy(
        double positionSeconds,
        double durationSeconds,
        bool completed,
        bool hasRealWatchSignal,
        bool expectedToResume)
    {
        var channel = new Channel
        {
            Id = 42,
            Type = ChannelType.VOD,
            StreamUrl = "https://example.test/movie.mp4",
            Duration = TimeSpan.FromSeconds(durationSeconds),
            WatchedPosition = TimeSpan.FromSeconds(positionSeconds),
            IsCompleted = completed,
            LastWatched = hasRealWatchSignal ? DateTime.UtcNow : null
        };

        var result = await ResolveAsync(
            new FakeWatchHistoryService(),
            7,
            channel,
            episode: null);

        Assert.Equal(expectedToResume, result.HasValue);
    }

    [Fact]
    public async Task ResolveAsync_SeriesUsesEpisodeHistoryWhenStreamsMatch()
    {
        var history = new FakeWatchHistoryService
        {
            Latest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromMinutes(18),
                Completed = false
            }
        };
        var channel = new Channel
        {
            Id = 88,
            Type = ChannelType.Series,
            StreamUrl = "https://example.test/episode-4.mp4",
            Duration = TimeSpan.FromMinutes(50)
        };
        var episode = new Episode
        {
            Id = 104,
            StreamUrl = channel.StreamUrl,
            Duration = TimeSpan.FromMinutes(50),
            WatchedPosition = TimeSpan.FromMinutes(5)
        };

        var result = await ResolveAsync(history, 7, channel, episode);

        Assert.Equal(TimeSpan.FromMinutes(18).TotalSeconds, result);
        Assert.Equal((7, (int?)null, 104), history.LastLookup);
    }

    [Fact]
    public async Task ResolveAsync_CompletedEpisodeHistoryDoesNotFallBackToChannelProgress()
    {
        var history = new FakeWatchHistoryService
        {
            EpisodeLatest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromMinutes(49),
                Completed = true
            },
            ChannelLatest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromMinutes(12),
                Completed = false
            }
        };
        var channel = new Channel
        {
            Id = 88,
            Type = ChannelType.Series,
            StreamUrl = "https://example.test/episode-4.mp4",
            Duration = TimeSpan.FromMinutes(50),
            WatchedPosition = TimeSpan.FromMinutes(12),
            LastWatched = DateTime.UtcNow
        };
        var episode = new Episode
        {
            Id = 104,
            StreamUrl = channel.StreamUrl,
            Duration = TimeSpan.FromMinutes(50)
        };

        var result = await ResolveAsync(history, 7, channel, episode);

        Assert.Null(result);
        Assert.Equal((7, (int?)null, 104), history.LastLookup);
    }

    [Theory]
    [InlineData(2980)]
    [InlineData(2850)]
    public async Task ResolveAsync_SeriesUsesChannelDurationWhenEpisodeDurationIsMissing(
        double positionSeconds)
    {
        var history = new FakeWatchHistoryService
        {
            EpisodeLatest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromSeconds(positionSeconds),
                Completed = false
            },
            ChannelLatest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromMinutes(12),
                Completed = false
            }
        };
        var channel = new Channel
        {
            Id = 88,
            Type = ChannelType.Series,
            StreamUrl = "https://example.test/episode-4.mp4",
            Duration = TimeSpan.FromMinutes(50),
            WatchedPosition = TimeSpan.FromMinutes(12),
            LastWatched = DateTime.UtcNow
        };
        var episode = new Episode
        {
            Id = 104,
            StreamUrl = channel.StreamUrl,
            Duration = null
        };

        var result = await ResolveAsync(history, 7, channel, episode);

        Assert.Null(result);
        Assert.Equal((7, (int?)null, 104), history.LastLookup);
    }

    [Fact]
    public async Task ResolveAsync_SeriesRejectsStaleEpisodeContext()
    {
        var history = new FakeWatchHistoryService
        {
            Latest = new WatchHistory
            {
                StoppedAt = TimeSpan.FromMinutes(18),
                Completed = false
            }
        };
        var channel = new Channel
        {
            Id = 88,
            Type = ChannelType.Series,
            StreamUrl = "https://example.test/current.mp4",
            Duration = TimeSpan.FromMinutes(50),
            WatchedPosition = TimeSpan.FromMinutes(12),
            LastWatched = DateTime.UtcNow
        };
        var staleEpisode = new Episode
        {
            Id = 104,
            StreamUrl = "https://example.test/previous.mp4",
            Duration = TimeSpan.FromMinutes(50),
            WatchedPosition = TimeSpan.FromMinutes(18),
            LastWatched = DateTime.UtcNow
        };

        var result = await ResolveAsync(history, 7, channel, staleEpisode);

        Assert.Null(result);
        Assert.Null(history.LastLookup);
    }

    [Fact]
    public async Task ResolveAsync_HistoryFailureFallsBackToCardWithRealWatchSignal()
    {
        var history = new FakeWatchHistoryService
        {
            LookupException = new InvalidOperationException("database unavailable")
        };
        var channel = new Channel
        {
            Id = 42,
            Type = ChannelType.VOD,
            StreamUrl = "https://example.test/movie.mp4",
            Duration = TimeSpan.FromHours(2),
            WatchedPosition = TimeSpan.FromMinutes(14),
            LastWatched = DateTime.UtcNow
        };

        var result = await ResolveAsync(history, 7, channel, episode: null);

        Assert.Equal(TimeSpan.FromMinutes(14).TotalSeconds, result);
    }

    private static async Task<double?> ResolveAsync(
        IWatchHistoryService history,
        int? profileId,
        Channel channel,
        Episode? episode)
    {
        var resolverType = typeof(Channel).Assembly.GetType(
            "Noctra.Services.PlayerResumeResolver");

        Assert.NotNull(resolverType);

        var resolver = Activator.CreateInstance(resolverType!, history);
        Assert.NotNull(resolver);

        var method = resolverType!.GetMethod("ResolveAsync");
        Assert.NotNull(method);

        var task = method!.Invoke(
            resolver,
            [profileId, channel, episode, CancellationToken.None]);

        return await Assert.IsType<Task<double?>>(task);
    }

    private sealed class FakeWatchHistoryService : IWatchHistoryService
    {
        public WatchHistory? Latest { get; init; }
        public WatchHistory? ChannelLatest { get; init; }
        public WatchHistory? EpisodeLatest { get; init; }
        public Exception? LookupException { get; init; }
        public (int ProfileId, int? ChannelId, int? EpisodeId)? LastLookup { get; private set; }

        public Task<WatchHistory?> GetLatestForMediaAsync(
            int profileId,
            int? channelId,
            int? episodeId,
            CancellationToken ct = default)
        {
            LastLookup = (profileId, channelId, episodeId);
            if (LookupException is not null)
            {
                return Task.FromException<WatchHistory?>(LookupException);
            }

            return Task.FromResult(
                episodeId.HasValue
                    ? EpisodeLatest ?? Latest
                    : ChannelLatest ?? Latest);
        }

        public Task TrackWatchAsync(
            int profileId,
            int? channelId,
            int? episodeId,
            TimeSpan position,
            bool completed = false,
            TimeSpan? duration = null,
            TimeSpan? incrementDelta = null,
            bool allowReset = false,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<WatchHistory>> GetHistoryAsync(
            int profileId,
            int skip = 0,
            int take = 50,
            CancellationToken ct = default) => Task.FromResult(new List<WatchHistory>());

        public Task DeleteProfileHistoryAsync(
            int profileId,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveFromHistoryAsync(
            int profileId,
            int? channelId,
            int? seriesId,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task CleanupOlderThanDaysAsync(
            int profileId,
            int days,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
