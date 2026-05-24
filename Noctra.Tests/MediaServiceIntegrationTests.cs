using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Noctra.Tests
{
    public class MediaServiceIntegrationTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly MediaService _service;

        public MediaServiceIntegrationTests()
        {
            _connection = new SqliteConnection("Data Source=MediaServiceTests;Mode=Memory;Cache=Shared");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using (var context = new AppDbContext(_options))
            {
                context.Database.EnsureCreated();
            }

            _contextFactory = new SimpleDbContextFactory(_options);
            _service = new MediaService(_contextFactory);
        }

        public void Dispose()
        {
            _connection.Close();
        }

        [Fact]
        public async Task AggregateContentAsync_ShouldGroupChannelsIntoSeries()
        {
            // Arrange
            int playlistId = 1;
            using (var context = new AppDbContext(_options))
            {
                context.Playlists.Add(new Playlist { Id = playlistId, Name = "Test", IsActive = true });
                context.Channels.AddRange(new List<Channel>
                {
                    new Channel { PlaylistId = playlistId, Name = "The Office S01E01", Type = ChannelType.Series, StreamUrl = "s1e1" },
                    new Channel { PlaylistId = playlistId, Name = "The Office S01E02", Type = ChannelType.Series, StreamUrl = "s1e2" },
                    new Channel { PlaylistId = playlistId, Name = "Breaking Bad S01E01", Type = ChannelType.Series, StreamUrl = "bb1" }
                });
                await context.SaveChangesAsync();
            }

            // Act
            await _service.AggregateContentAsync(playlistId);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                var series = await context.Series.Include(s => s.Seasons).ThenInclude(sn => sn.Episodes).ToListAsync();
                Assert.Equal(2, series.Count);
                
                var theOffice = series.First(s => s.Name == "The Office");
                Assert.Single(theOffice.Seasons);
                Assert.Equal(2, theOffice.Seasons.First().Episodes.Count);

                var breakingBad = series.First(s => s.Name == "Breaking Bad");
                Assert.Single(breakingBad.Seasons);
                Assert.Single(breakingBad.Seasons.First().Episodes);
            }
        }

        [Fact]
        public async Task AggregateContentAsync_ShouldNotCreateDuplicateSeriesOnRerun()
        {
            // Arrange
            int playlistId = 2;
            using (var context = new AppDbContext(_options))
            {
                context.Playlists.Add(new Playlist { Id = playlistId, Name = "Test 2", IsActive = true });
                context.Channels.Add(new Channel { PlaylistId = playlistId, Name = "Series S01E01", Type = ChannelType.Series, StreamUrl = "url1" });
                await context.SaveChangesAsync();
            }

            // Act - First Run
            await _service.AggregateContentAsync(playlistId);

            // Act - Second Run (with same data)
            await _service.AggregateContentAsync(playlistId);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                Assert.Equal(1, await context.Series.CountAsync(s => s.PlaylistId == playlistId));
                Assert.Equal(1, await context.Seasons.CountAsync());
                Assert.Equal(1, await context.Episodes.CountAsync());
            }
        }

        [Fact]
        public async Task AggregateContentAsync_ShouldHandleNewEpisodesInExistingSeries()
        {
            // Arrange
            int playlistId = 3;
            using (var context = new AppDbContext(_options))
            {
                context.Playlists.Add(new Playlist { Id = playlistId, Name = "Test 3", IsActive = true });
                context.Channels.Add(new Channel { PlaylistId = playlistId, Name = "Series S01E01", Type = ChannelType.Series, StreamUrl = "url1" });
                await context.SaveChangesAsync();
            }

            await _service.AggregateContentAsync(playlistId);

            // Add new channel for the same series
            using (var context = new AppDbContext(_options))
            {
                context.Channels.Add(new Channel { PlaylistId = playlistId, Name = "Series S01E02", Type = ChannelType.Series, StreamUrl = "url2" });
                await context.SaveChangesAsync();
            }

            // Act
            await _service.AggregateContentAsync(playlistId);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                var series = await context.Series.Include(s => s.Seasons).ThenInclude(sn => sn.Episodes).FirstAsync(s => s.PlaylistId == playlistId);
                Assert.Single(series.Seasons);
                Assert.Equal(2, series.Seasons.First().Episodes.Count);
            }
        }

        [Fact]
        public async Task AggregateContentAsync_WhenNoSeriesChannels_RemovesStaleSeriesMetadata()
        {
            int playlistId = 4;
            using (var context = new AppDbContext(_options))
            {
                context.Playlists.Add(new Playlist { Id = playlistId, Name = "Test 4", IsActive = true });
                context.Series.Add(new Series
                {
                    PlaylistId = playlistId,
                    Name = "e",
                    Seasons = new List<Season>
                    {
                        new()
                        {
                            SeasonNumber = 1,
                            Episodes = new List<Episode>
                            {
                                new() { Name = "e - Ep 1", EpisodeNumber = 1, StreamUrl = "https://example.com/cnbce/master.m3u8" }
                            }
                        }
                    }
                });
                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "CNBC-e",
                    Type = ChannelType.Live,
                    StreamUrl = "https://example.com/cnbce/master.m3u8"
                });
                await context.SaveChangesAsync();
            }

            await _service.AggregateContentAsync(playlistId);

            using (var context = new AppDbContext(_options))
            {
                Assert.Equal(0, await context.Series.CountAsync(s => s.PlaylistId == playlistId));
            }
        }
    }
}
