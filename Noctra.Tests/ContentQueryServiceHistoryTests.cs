using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class ContentQueryServiceHistoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ContentQueryServiceHistoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task GetProfilePlaylistIdsAsync_PrefersActivePlaylists()
    {
        await using (var db = new AppDbContext(_options))
        {
            SeedProfile(db, profileId: 3);
            db.Playlists.AddRange(
                new Playlist
                {
                    Id = 10,
                    ProfileId = 3,
                    Name = "Active",
                    Url = "http://active.test/list.m3u",
                    IsActive = true
                },
                new Playlist
                {
                    Id = 11,
                    ProfileId = 3,
                    Name = "Inactive",
                    Url = "http://inactive.test/list.m3u",
                    IsActive = false
                });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.GetProfilePlaylistIdsAsync(3);

        Assert.Equal([10], result);
    }

    [Fact]
    public async Task GetHistoryPageAsync_MapsChannelAndEpisodeProgress()
    {
        var watchedAt = DateTime.UtcNow;
        await using (var db = new AppDbContext(_options))
        {
            SeedProfile(db, profileId: 4);
            var playlist = new Playlist
            {
                Id = 20,
                ProfileId = 4,
                Name = "Playlist",
                Url = "http://provider.test/list.m3u",
                IsActive = true
            };
            var movie = new Channel
            {
                Id = 30,
                Playlist = playlist,
                Name = "Movie",
                StreamUrl = "http://provider.test/movie.mp4",
                Type = ChannelType.VOD
            };
            var series = new Series
            {
                Id = 40,
                Playlist = playlist,
                Name = "Series",
                CoverUrl = "http://provider.test/series.jpg"
            };
            var season = new Season
            {
                Id = 50,
                Series = series,
                SeasonNumber = 1
            };
            var episode = new Episode
            {
                Id = 60,
                Season = season,
                EpisodeNumber = 2,
                Name = "Episode",
                StreamUrl = "http://provider.test/episode.mp4",
                Duration = TimeSpan.FromMinutes(45)
            };

            db.WatchHistories.AddRange(
                new WatchHistory
                {
                    ProfileId = 4,
                    Channel = movie,
                    WatchedAt = watchedAt,
                    StoppedAt = TimeSpan.FromMinutes(12)
                },
                new WatchHistory
                {
                    ProfileId = 4,
                    Episode = episode,
                    WatchedAt = watchedAt.AddMinutes(-1),
                    WatchedDuration = TimeSpan.FromMinutes(8)
                });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.GetHistoryPageAsync(4, [20], skip: 0, take: 10);

        Assert.Collection(
            result,
            movie =>
            {
                Assert.Equal("Movie", movie.Name);
                Assert.Equal(TimeSpan.FromMinutes(12), movie.WatchedPosition);
                Assert.Equal(TimeSpan.FromMinutes(42), movie.Duration);
            },
            episode =>
            {
                Assert.Equal("Episode", episode.Name);
                Assert.Equal(ChannelType.Series, episode.Type);
                Assert.Equal(TimeSpan.FromMinutes(8), episode.WatchedPosition);
                Assert.Equal(TimeSpan.FromMinutes(45), episode.Duration);
            });
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private ContentQueryService CreateService()
    {
        var contextFactory = new Mock<IDbContextFactory<AppDbContext>>();
        contextFactory
            .Setup(factory => factory.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_options));

        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(service => service.Settings).Returns(new AppSettings());

        return new ContentQueryService(
            Mock.Of<IPlaylistService>(),
            Mock.Of<IMediaService>(),
            settingsService.Object,
            contextFactory.Object);
    }

    private static void SeedProfile(AppDbContext db, int profileId)
    {
        db.Profiles.Add(new Profile
        {
            Id = profileId,
            Name = $"Profile {profileId}",
            ProviderAccount = new ProviderAccount
            {
                Name = $"Account {profileId}",
                Url = "http://provider.test"
            }
        });
    }
}
