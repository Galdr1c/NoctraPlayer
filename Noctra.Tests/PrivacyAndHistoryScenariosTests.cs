using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests
{
    public class PrivacyAndHistoryScenariosTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public PrivacyAndHistoryScenariosTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new AppDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        [Fact]
        public async Task Should_Not_Record_History_When_SaveWatchHistory_Is_Disabled()
        {
            // Arrange
            var contextFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            contextFactoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(_options));

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.Settings).Returns(new AppSettings { SaveWatchHistory = false });

            var service = new WatchHistoryService(contextFactoryMock.Object, settingsServiceMock.Object);
            int profileId = 1;
            int channelId = 101;

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "..." };
                context.ProviderAccounts.Add(account);
                var profile = new Profile { Id = profileId, Name = "Test", ProviderAccount = account };
                context.Profiles.Add(profile);
                var playlist = new Playlist { Id = 1, ProfileId = profileId, Name = "PL", Url = "http://test.com" };
                context.Playlists.Add(playlist);
                context.Channels.Add(new Channel { Id = channelId, PlaylistId = 1, Name = "CH", StreamUrl = "..." });
                await context.SaveChangesAsync();
            }

            // Act
            await service.TrackWatchAsync(profileId, channelId, null, TimeSpan.FromMinutes(5));

            // Assert
            using var contextRead = new AppDbContext(_options);
            var historyCount = await contextRead.WatchHistories.CountAsync();
            Assert.Equal(0, historyCount);
        }

        [Fact]
        public async Task Should_Cleanup_Old_History_Based_On_Retention_Policy()
        {
            // Arrange
            var contextFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            contextFactoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(_options));
            
            var settingsServiceMock = new Mock<ISettingsService>();
            var service = new WatchHistoryService(contextFactoryMock.Object, settingsServiceMock.Object);
            
            int profileId = 1;
            var now = DateTime.UtcNow;

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "..." };
                context.ProviderAccounts.Add(account);
                var profile = new Profile { Id = profileId, Name = "Test", ProviderAccount = account };
                context.Profiles.Add(profile);
                await context.SaveChangesAsync();

                // Entry from 10 days ago
                context.WatchHistories.Add(new WatchHistory 
                { 
                    ProfileId = profileId, 
                    ChannelId = null, 
                    WatchedAt = now.AddDays(-10)
                });
                
                // Entry from today
                context.WatchHistories.Add(new WatchHistory 
                { 
                    ProfileId = profileId, 
                    ChannelId = null, 
                    WatchedAt = now
                });
                
                await context.SaveChangesAsync();
            }

            // Act: Cleanup older than 7 days
            await service.CleanupOlderThanDaysAsync(profileId, 7);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                var remaining = await context.WatchHistories.ToListAsync();
                Assert.Single(remaining);
                Assert.True(remaining[0].WatchedAt > now.AddDays(-1));
            }
        }

        [Fact]
        public async Task Should_Clear_All_History_And_Reset_Progress_Markers()
        {
            // Arrange
            var contextFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            contextFactoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(_options));
            
            var settingsServiceMock = new Mock<ISettingsService>();
            var service = new WatchHistoryService(contextFactoryMock.Object, settingsServiceMock.Object);
            
            int profileId = 1;

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "..." };
                context.ProviderAccounts.Add(account);
                var profile = new Profile { Id = profileId, Name = "Test", ProviderAccount = account };
                context.Profiles.Add(profile);
                
                // Add history
                context.WatchHistories.Add(new WatchHistory { ProfileId = profileId, WatchedAt = DateTime.UtcNow });
                
                // Add channel progress
                var playlist = new Playlist { Name = "Test", ProfileId = profileId, Url = "http://test.com" };
                context.Playlists.Add(playlist);
                context.Channels.Add(new Channel 
                { 
                    Playlist = playlist, 
                    Name = "TV", 
                    StreamUrl = "...",
                    WatchedPosition = TimeSpan.FromMinutes(10),
                    IsCompleted = true 
                });

                await context.SaveChangesAsync();
            }

            // Act
            await service.DeleteProfileHistoryAsync(1);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                Assert.Empty(await context.WatchHistories.ToListAsync());
                
                var channel = await context.Channels.FirstAsync();
                Assert.Equal(TimeSpan.Zero, channel.WatchedPosition);
                Assert.False(channel.IsCompleted);
                Assert.Null(channel.LastWatched);
            }
        }
    }
}
