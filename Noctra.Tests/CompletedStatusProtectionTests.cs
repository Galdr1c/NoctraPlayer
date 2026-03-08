using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    public class CompletedStatusProtectionTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly AppDbContext _context;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public CompletedStatusProtectionTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new AppDbContext(_options);
            _context.Database.EnsureCreated();
            _contextFactory = new TestDbContextFactory(_options);
        }

        private async Task<(int profileId, int playlistId)> SeedBaseAsync(string profileSuffix = "")
        {
            var account = new ProviderAccount { Name = $"Account{profileSuffix}", Url = "http://test.com" };
            var profile = new Profile { Name = $"Profile{profileSuffix}", ProviderAccount = account };
            _context.ProviderAccounts.Add(account);
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var playlist = new Playlist { Name = $"Playlist{profileSuffix}", ProfileId = profile.Id };
            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();
            
            return (profile.Id, playlist.Id);
        }

        private async Task<Channel> SeedVodChannelAsync(int playlistId, string name = "Test Movie")
        {
            var ch = new Channel
            {
                Name       = name,
                StreamUrl  = $"http://test.com/vod/{Guid.NewGuid()}.mp4",
                Type       = ChannelType.VOD,
                PlaylistId = playlistId
            };
            _context.Channels.Add(ch);
            await _context.SaveChangesAsync();
            return ch;
        }

        private async Task<(Episode episode, Season season, Series series)> SeedEpisodeAsync(int playlistId, int profileId, int seasonNumber = 1, int episodeNumber = 1, int? tmdbId = null)
        {
            var series = new Series { Name = "Test Series", PlaylistId = playlistId, TmdbId = tmdbId };
            _context.Series.Add(series);
            await _context.SaveChangesAsync();

            var season = new Season { SeasonNumber = seasonNumber, SeriesId = series.Id };
            _context.Seasons.Add(season);
            await _context.SaveChangesAsync();

            var episode = new Episode
            {
                EpisodeNumber = episodeNumber,
                Name          = $"S{seasonNumber:D2}E{episodeNumber:D2}",
                StreamUrl     = $"http://test.com/series/{Guid.NewGuid()}.mp4",
                SeasonId      = season.Id
            };
            _context.Episodes.Add(episode);
            await _context.SaveChangesAsync();
            return (episode, season, series);
        }

        [Fact]
        public async Task WatchHistory_Completed_VideoFailsToLoad_CompletedPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("A");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromHours(2);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.Zero, completed: false, duration: null);

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.Completed);
        }

        [Fact]
        public async Task WatchHistory_Completed_StuckLoading_StoppedAtNotZeroed()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("B");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromHours(1.5);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.Zero, completed: false, duration: null);

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.StoppedAt > TimeSpan.Zero);
            Assert.Equal(duration, history.StoppedAt);
        }

        [Fact]
        public async Task WatchHistory_Completed_ReopenedAndClosedImmediately_CompletedPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("C");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromMinutes(90);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.FromSeconds(5), completed: false, duration: duration);

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.Completed);
            Assert.Equal(duration, history.StoppedAt);
        }

        [Fact]
        public async Task WatchHistory_Completed_NullDurationRepeatedly_StoppedAtPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("D");
            var channel = await SeedVodChannelAsync(playlistId);
            var knownDuration = TimeSpan.FromMinutes(60);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: knownDuration, completed: true, duration: knownDuration);

            for (int i = 0; i < 5; i++)
            {
                await svc.TrackWatchAsync(profileId, channel.Id, null,
                    position: TimeSpan.FromSeconds(i * 2), completed: false, duration: null);
            }

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.Completed);
            Assert.Equal(knownDuration, history.StoppedAt);
        }

        [Fact]
        public async Task WatchHistory_Completed_RapidFireCalls_CompletedAlwaysPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("E");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromHours(2);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            for (int i = 0; i < 10; i++)
            {
                await svc.TrackWatchAsync(profileId, channel.Id, null,
                    position: TimeSpan.FromSeconds(i),
                    completed: false,
                    duration: i % 3 == 0 ? null : duration);
            }

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.Completed);
        }

        [Fact]
        public async Task ChannelEntity_IsCompleted_FailedReload_NotRevertedToFalse()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("F");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromMinutes(90);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.Zero, completed: false, duration: null);

            var dbChannel = await _context.Channels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
            Assert.True(dbChannel.IsCompleted);
        }

        [Fact]
        public async Task ChannelEntity_WatchedPosition_Completed_NotResetToZero()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("G");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromMinutes(90);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.Zero, completed: false, duration: null);

            var dbChannel = await _context.Channels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
            Assert.True(dbChannel.WatchedPosition.HasValue);
            Assert.True(dbChannel.WatchedPosition!.Value > TimeSpan.Zero);
        }

        [Fact]
        public async Task ChannelEntity_Duration_NullOnReload_KnownDurationPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("H");
            var channel = await SeedVodChannelAsync(playlistId);
            var knownDuration = TimeSpan.FromHours(2);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.FromMinutes(30), completed: false, duration: knownDuration);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.FromMinutes(30), completed: false, duration: null);

            var dbChannel = await _context.Channels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
            Assert.Equal(knownDuration, dbChannel.Duration);
        }

        [Fact]
        public async Task EpisodeEntity_IsCompleted_FailedReload_NotRevertedToFalse()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("I");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId);
            var duration = TimeSpan.FromMinutes(45);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.Zero, completed: false, duration: null);

            var dbEpisode = await _context.Episodes.AsNoTracking().FirstAsync(e => e.Id == episode.Id);
            Assert.True(dbEpisode.IsCompleted);
        }

        [Fact]
        public async Task EpisodeEntity_WatchedPosition_Completed_NotZeroed()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("J");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId);
            var duration = TimeSpan.FromMinutes(48);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.Zero, completed: false, duration: null);

            var dbEpisode = await _context.Episodes.AsNoTracking().FirstAsync(e => e.Id == episode.Id);
            Assert.True(dbEpisode.WatchedPosition > TimeSpan.Zero);
        }

        [Fact]
        public async Task EpisodeEntity_Duration_NullOnError_KnownDurationPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("K");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId);
            var knownDuration = TimeSpan.FromMinutes(45);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.FromMinutes(20), completed: false, duration: knownDuration);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.FromMinutes(20), completed: false, duration: null);

            var dbEpisode = await _context.Episodes.AsNoTracking().FirstAsync(e => e.Id == episode.Id);
            Assert.Equal(knownDuration, dbEpisode.Duration);
        }

        [Fact]
        public async Task SeriesProgress_Completed_FailedReload_NotReverted()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("L");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId, seasonNumber: 1, episodeNumber: 5, tmdbId: 99901);
            var duration = TimeSpan.FromMinutes(52);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.Zero, completed: false, duration: null);

            var progress = await _context.SeriesEpisodeProgresses.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProfileId == profileId);
            Assert.NotNull(progress);
            Assert.True(progress!.Completed);
        }

        [Fact]
        public async Task SeriesProgress_StoppedAt_Completed_NullDuration_PreservesOldValue()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("M");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId, seasonNumber: 2, episodeNumber: 3, tmdbId: 99902);
            var knownDuration = TimeSpan.FromMinutes(58);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: knownDuration, completed: true, duration: knownDuration);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.Zero, completed: false, duration: null);

            var progress = await _context.SeriesEpisodeProgresses.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProfileId == profileId);
            Assert.NotNull(progress);
            Assert.Equal(knownDuration, progress!.StoppedAt);
        }

        [Fact]
        public async Task SeriesProgress_ProviderSwitch_TmdbIdMatch_CompletedPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("N");

            var (episodeA, _, _) = await SeedEpisodeAsync(playlistId, profileId, seasonNumber: 1, episodeNumber: 1, tmdbId: 77701);
            var duration = TimeSpan.FromMinutes(47);

            await svc.TrackWatchAsync(profileId, null, episodeA.Id,
                position: duration, completed: true, duration: duration);

            var (episodeB, _, _) = await SeedEpisodeAsync(playlistId, profileId, seasonNumber: 1, episodeNumber: 1, tmdbId: 77701);

            await svc.TrackWatchAsync(profileId, null, episodeB.Id,
                position: TimeSpan.Zero, completed: false, duration: null);

            var progresses = await _context.SeriesEpisodeProgresses.AsNoTracking()
                .Where(p => p.ProfileId == profileId && p.SeasonNumber == 1 && p.EpisodeNumber == 1)
                .ToListAsync();

            Assert.Single(progresses);
            Assert.True(progresses[0].Completed);
        }

        [Fact]
        public async Task AllLayers_Completed_VideoFailsToLoad_AllPreserved()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("O");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId, seasonNumber: 3, episodeNumber: 7, tmdbId: 55501);
            var duration = TimeSpan.FromMinutes(55);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: duration, completed: true, duration: duration);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.Zero, completed: false, duration: null);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                await svc.TrackWatchAsync(profileId, null, episode.Id,
                    position: TimeSpan.Zero, completed: false, duration: null);
            }

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.EpisodeId == episode.Id);
            Assert.True(history.Completed);
            Assert.Equal(duration, history.StoppedAt);

            var dbEpisode = await _context.Episodes.AsNoTracking().FirstAsync(e => e.Id == episode.Id);
            Assert.True(dbEpisode.IsCompleted);
            Assert.True(dbEpisode.WatchedPosition >= duration);
            Assert.Equal(duration, dbEpisode.Duration);

            var progress = await _context.SeriesEpisodeProgresses.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProfileId == profileId && p.SeasonNumber == 3 && p.EpisodeNumber == 7);
            Assert.NotNull(progress);
            Assert.True(progress!.Completed);
            Assert.True(progress.StoppedAt > TimeSpan.Zero);
        }

        [Fact]
        public async Task AllLayers_Completed_FreshContextRead_DataConsistent()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("P");
            var (episode, _, _) = await SeedEpisodeAsync(playlistId, profileId, seasonNumber: 1, episodeNumber: 1, tmdbId: 12345);
            var duration = TimeSpan.FromMinutes(42);

            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: duration, completed: true, duration: duration);
            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.Zero, completed: false, duration: null);
            await svc.TrackWatchAsync(profileId, null, episode.Id,
                position: TimeSpan.FromSeconds(3), completed: false, duration: duration);

            await using var freshContext = new AppDbContext(_options);

            var freshHistory = await freshContext.WatchHistories.AsNoTracking().FirstAsync(h => h.EpisodeId == episode.Id);
            var freshEpisode = await freshContext.Episodes.AsNoTracking().FirstAsync(e => e.Id == episode.Id);
            var freshProgress = await freshContext.SeriesEpisodeProgresses.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProfileId == profileId);

            Assert.True(freshHistory.Completed);
            Assert.True(freshEpisode.IsCompleted);
            Assert.NotNull(freshProgress);
            Assert.True(freshProgress!.Completed);
        }

        [Fact]
        public async Task WatchHistory_ExtremePosition_DoesNotThrow()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("Q");
            var channel = await SeedVodChannelAsync(playlistId);

            var ex = await Record.ExceptionAsync(() =>
                svc.TrackWatchAsync(profileId, channel.Id, null,
                    position: TimeSpan.MaxValue, completed: false, duration: null));

            Assert.Null(ex);
        }

        [Fact]
        public async Task WatchHistory_CompletedOnlyWhenExplicitlySet()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("R");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromHours(1);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.FromSeconds(3204),
                completed: false,
                duration: duration);

            var history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.False(history.Completed);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.FromSeconds(3240),
                completed: true,
                duration: duration);

            history = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.Completed);
        }

        [Fact]
        public async Task WatchHistory_WatchedAt_Updated_CompletedUnchanged()
        {
            var svc = new WatchHistoryService(_contextFactory);
            var (profileId, playlistId) = await SeedBaseAsync("S");
            var channel = await SeedVodChannelAsync(playlistId);
            var duration = TimeSpan.FromMinutes(90);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: duration, completed: true, duration: duration);

            var firstRecord = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);
            var originalWatchedAt = firstRecord.WatchedAt;
            var originalStoppedAt = firstRecord.StoppedAt;

            await Task.Delay(5);

            await svc.TrackWatchAsync(profileId, channel.Id, null,
                position: TimeSpan.FromSeconds(10), completed: false, duration: null);

            var updatedRecord = await _context.WatchHistories.AsNoTracking().FirstAsync(h => h.ChannelId == channel.Id);

            Assert.True(updatedRecord.WatchedAt >= originalWatchedAt);
            Assert.True(updatedRecord.Completed);
            Assert.Equal(originalStoppedAt, updatedRecord.StoppedAt);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Close();
            _connection.Dispose();
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
        {
            private readonly DbContextOptions<AppDbContext> _opts;
            public TestDbContextFactory(DbContextOptions<AppDbContext> opts) => _opts = opts;
            public AppDbContext CreateDbContext() => new AppDbContext(_opts);
        }
    }
}
