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

        [Fact]
        public async Task DeleteProfileHistory_OnlyAffectsCurrentProfile()
        {
            // Arrange
            var contextFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            contextFactoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(_options));
            
            var settingsServiceMock = new Mock<ISettingsService>();
            var service = new WatchHistoryService(contextFactoryMock.Object, settingsServiceMock.Object);
            
            int profile1Id = 1;
            int profile2Id = 2;

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "..." };
                context.ProviderAccounts.Add(account);
                
                var profile1 = new Profile { Id = profile1Id, Name = "User 1", ProviderAccount = account };
                var profile2 = new Profile { Id = profile2Id, Name = "User 2", ProviderAccount = account };
                context.Profiles.AddRange(profile1, profile2);
                
                var pl1 = new Playlist { Name = "PL1", ProfileId = profile1Id, Url = "http://test.com/1" };
                var pl2 = new Playlist { Name = "PL2", ProfileId = profile2Id, Url = "http://test.com/2" };
                context.Playlists.AddRange(pl1, pl2);

                // Profile 1 data
                context.WatchHistories.Add(new WatchHistory { ProfileId = profile1Id, WatchedAt = DateTime.UtcNow });
                context.Channels.Add(new Channel { Playlist = pl1, Name = "CH1", StreamUrl = "...", WatchedPosition = TimeSpan.FromMinutes(10) });

                // Profile 2 data (should be preserved)
                context.WatchHistories.Add(new WatchHistory { ProfileId = profile2Id, WatchedAt = DateTime.UtcNow });
                context.Channels.Add(new Channel { Playlist = pl2, Name = "CH2", StreamUrl = "...", WatchedPosition = TimeSpan.FromMinutes(20), IsCompleted = true });

                await context.SaveChangesAsync();
            }

            // Act: Delete history for Profile 1 only
            await service.DeleteProfileHistoryAsync(profile1Id);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                // Profile 1 history should be gone
                var p1History = await context.WatchHistories.Where(h => h.ProfileId == profile1Id).ToListAsync();
                Assert.Empty(p1History);

                // Profile 2 history should STILL EXIST
                var p2History = await context.WatchHistories.Where(h => h.ProfileId == profile2Id).ToListAsync();
                Assert.Single(p2History);

                // Profile 1 channel progress should be reset
                var ch1 = await context.Channels.FirstAsync(c => c.Name == "CH1");
                Assert.Equal(TimeSpan.Zero, ch1.WatchedPosition);

                // Profile 2 channel progress should be UNTOUCHED
                var ch2 = await context.Channels.FirstAsync(c => c.Name == "CH2");
                Assert.Equal(TimeSpan.FromMinutes(20), ch2.WatchedPosition);
                Assert.True(ch2.IsCompleted);
            }
        }
    }
}
