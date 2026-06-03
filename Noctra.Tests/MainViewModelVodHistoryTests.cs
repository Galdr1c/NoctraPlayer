using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class MainViewModelVodHistoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        private readonly Mock<IPlaylistService> _playlistServiceMock = new();
        private readonly Mock<IEpgService> _epgServiceMock = new();
        private readonly Mock<ISettingsService> _settingsServiceMock = new();
        private readonly Mock<IDispatcherService> _dispatcherServiceMock = new();
        private readonly Mock<IDbContextFactory<AppDbContext>> _contextFactoryMock = new();
        private readonly Mock<ILogger<MainViewModel>> _loggerMock = new();
        private readonly Mock<IWatchHistoryService> _historyServiceMock = new();
        private readonly Mock<IMediaService> _mediaServiceMock = new();
        private readonly Mock<ITmdbSyncService> _tmdbServiceMock = new();
        private readonly Mock<IContentDownloadService> _downloadServiceMock = new();
        private readonly Mock<IMetadataService> _metadataServiceMock = new();
        private readonly Mock<IDialogService> _dialogServiceMock = new();
        private readonly Mock<IChannelService> _channelServiceMock = new();
        private readonly Mock<IXtreamCodesService> _xtreamServiceMock = new();
        private readonly Mock<IStalkerPortalService> _stalkerServiceMock = new();
        private readonly Mock<ISecurityService> _securityServiceMock = new();
        private readonly Mock<ILicenseService> _licenseServiceMock = new();
        private readonly Mock<IUpdateService> _updateServiceMock = new();

        public MainViewModelVodHistoryTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new AppDbContext(_options);
            context.Database.EnsureCreated();

            // Simple dispatcher that executes immediately
            _dispatcherServiceMock.Setup(d => d.Invoke(It.IsAny<Action>()))
                .Callback<Action>(a => a());

            _settingsServiceMock.Setup(s => s.Settings).Returns(new AppSettings());

            _contextFactoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(_options));
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        private MainViewModel CreateViewModel()
        {
            return new MainViewModel(
                _settingsServiceMock.Object,
                _downloadServiceMock.Object,
                _metadataServiceMock.Object,
                _dispatcherServiceMock.Object,
                _dialogServiceMock.Object,
                null!, // WatermarkViewModel
                _channelServiceMock.Object,
                _mediaServiceMock.Object,
                _epgServiceMock.Object,
                _playlistServiceMock.Object,
                _historyServiceMock.Object,
                _xtreamServiceMock.Object,
                _stalkerServiceMock.Object,
                null!, // LanguageDetectionService
                null!, // EpgSourceResolver
                _contextFactoryMock.Object,
                _securityServiceMock.Object,
                _tmdbServiceMock.Object,
                _licenseServiceMock.Object,
                _updateServiceMock.Object,
                new Mock<ILocalizationService>().Object,
                _loggerMock.Object
            );
        }

        [Fact]
        public async Task UpdateContinueWatchingRailAsync_ShouldApplyDurationFallbackForVODs()
        {
            // Arrange
            var vm = CreateViewModel();
            int profileId = 1;
            int playlistId = 10;
            
            // Bypass login flow and set active state manually
            var propertyInfo = typeof(MainViewModel).GetProperty("CurrentProfileId");
            propertyInfo?.SetValue(vm, profileId);

            var selectedPlaylistField = typeof(MainViewModel).GetProperty("SelectedPlaylist");
            selectedPlaylistField?.SetValue(vm, new Playlist { Id = playlistId, Name = "Test Playlist", ProfileId = profileId });

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "..." };
                context.ProviderAccounts.Add(account);
                var profile = new Profile { Id = profileId, Name = "Test", ProviderAccount = account };
                context.Profiles.Add(profile);
                
                var playlist = new Playlist { Id = playlistId, ProfileId = profileId, Name = "PL", Url = "http://test.com" };
                context.Playlists.Add(playlist);

                var vodChannel = new Channel 
                { 
                    Id = 101, 
                    PlaylistId = playlistId, 
                    Name = "Test Movie", 
                    Type = ChannelType.VOD, 
                    StreamUrl = "...",
                    Duration = null // No duration initially (common for M3U)
                };
                context.Channels.Add(vodChannel);

                // Add to watch history: stopped at 20 minutes
                context.WatchHistories.Add(new WatchHistory 
                { 
                    ProfileId = profileId, 
                    ChannelId = 101, 
                    WatchedAt = DateTime.UtcNow,
                    StoppedAt = TimeSpan.FromMinutes(20),
                    Completed = false
                });

                await context.SaveChangesAsync();
            }

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateContinueWatchingRailAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, null)!;
            await task;

            // Wait briefly for debounce (100ms in actual code)
            await Task.Delay(150);

            // Assert
            // Due to the fallback logic, a channel with no Duration but with > 0 WatchedPosition
            // will be assigned Duration = WatchedPosition + 30 mins, and therefore pass IsContinueWatchingCandidate.
            var continueWatchingChannels = vm.ContinueWatching.ToList();
            Assert.Single(continueWatchingChannels);
            
            var channel = continueWatchingChannels.First();
            Assert.Equal("Test Movie", channel.Name);
            Assert.NotNull(channel.Duration);
            Assert.Equal(TimeSpan.FromMinutes(50), channel.Duration); // 20 mins watched + 30 mins fallback
        }

        [Fact]
        public async Task UpdateHistoryBucketsAsync_ShouldUsePassedSourceChannelsAndNotRace()
        {
            // Arrange
            var vm = CreateViewModel();

            var vodChannel = new Channel 
            { 
                Id = 1, 
                Type = ChannelType.VOD, 
                Name = "History Movie" 
            };

            var liveChannel = new Channel 
            { 
                Id = 2, 
                Type = ChannelType.Live, 
                Name = "History Live" 
            };

            var initialChannels = new List<Channel> { vodChannel, liveChannel };

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateHistoryBucketsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            
            // Invoke with the sourceChannels parameter
            var task = (Task)method.Invoke(vm, new object[] { initialChannels })!;
            await task;

            // Assert
            // Even though vm.HistoryChannels is empty (simulating the UI thread delay),
            // the buckets should be populated because we passed the DB results directly.
            Assert.Empty(vm.HistoryChannels); // Simulating UI didn't update yet
            
            Assert.Single(vm.HistoryVodChannels);
            Assert.Equal("History Movie", vm.HistoryVodChannels.First().Name);

            Assert.Single(vm.HistoryLiveChannels);
            Assert.Equal("History Live", vm.HistoryLiveChannels.First().Name);
        }

        [Fact]
        public async Task UpdateContinueWatchingRailAsync_ShouldCarrySeriesProgressToNewProviderEpisodes()
        {
            var vm = CreateViewModel();
            const int profileId = 1;
            const int playlistId = 10;
            const int seriesId = 20;

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.CurrentProfileId))?.SetValue(vm, profileId);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.SelectedPlaylist))?.SetValue(
                vm,
                new Playlist { Id = playlistId, Name = "New Provider", ProfileId = profileId });

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "http://new-provider.test" };
                var profile = new Profile { Id = profileId, Name = "Test", ProviderAccount = account };
                var playlist = new Playlist { Id = playlistId, ProfileId = profileId, Name = "New Provider", Url = "http://new-provider.test/list.m3u" };
                var series = new Series { Id = seriesId, Playlist = playlist, Name = "Breaking Bad" };
                var season = new Season { Series = series, SeasonNumber = 1 };
                var episode = new Episode
                {
                    Season = season,
                    EpisodeNumber = 1,
                    Name = "Pilot",
                    StreamUrl = "http://new-provider.test/series/breaking-bad-s01e01.mp4",
                    Duration = TimeSpan.FromMinutes(50)
                };

                context.ProviderAccounts.Add(account);
                context.Profiles.Add(profile);
                context.Playlists.Add(playlist);
                context.Series.Add(series);
                context.Seasons.Add(season);
                context.Episodes.Add(episode);
                context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
                {
                    ProfileId = profileId,
                    SeriesKey = "breaking bad",
                    SeriesTitle = "Breaking Bad",
                    SeasonNumber = 1,
                    EpisodeNumber = 1,
                    LastWatchedAt = DateTime.UtcNow,
                    StoppedAt = TimeSpan.FromMinutes(20),
                    Duration = TimeSpan.FromMinutes(50),
                    Completed = false
                });
                await context.SaveChangesAsync();
            }

            SetAllSeriesCache(vm, new List<Series>
            {
                new() { Id = seriesId, PlaylistId = playlistId, Name = "Breaking Bad" }
            });

            var method = typeof(MainViewModel).GetMethod("UpdateContinueWatchingRailAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            await (Task)method.Invoke(vm, null)!;
            await Task.Delay(150);

            var item = Assert.Single(vm.ContinueWatching);
            Assert.Equal("Pilot", item.Name);
            Assert.Equal(TimeSpan.FromMinutes(20), item.WatchedPosition);
            Assert.False(item.IsCompleted);
            Assert.Equal("Breaking Bad", item.GroupTitle);
        }

        [Fact]
        public async Task UpdateHistoryBucketsAsync_ShouldCarrySeriesProgressToNewProviderSeries()
        {
            var vm = CreateViewModel();
            const int profileId = 1;
            const int playlistId = 10;
            const int seriesId = 20;

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.CurrentProfileId))?.SetValue(vm, profileId);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.SelectedPlaylist))?.SetValue(
                vm,
                new Playlist { Id = playlistId, Name = "New Provider", ProfileId = profileId });

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "http://new-provider.test" };
                var profile = new Profile { Id = profileId, Name = "Test", ProviderAccount = account };
                var playlist = new Playlist { Id = playlistId, ProfileId = profileId, Name = "New Provider", Url = "http://new-provider.test/list.m3u" };
                context.ProviderAccounts.Add(account);
                context.Profiles.Add(profile);
                context.Playlists.Add(playlist);
                context.Series.Add(new Series { Id = seriesId, Playlist = playlist, Name = "Breaking Bad" });
                context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
                {
                    ProfileId = profileId,
                    SeriesKey = "breaking bad",
                    SeriesTitle = "Breaking Bad",
                    SeasonNumber = 1,
                    EpisodeNumber = 1,
                    LastWatchedAt = DateTime.UtcNow,
                    StoppedAt = TimeSpan.FromMinutes(50),
                    Duration = TimeSpan.FromMinutes(50),
                    Completed = true
                });
                await context.SaveChangesAsync();
            }

            SetAllSeriesCache(vm, new List<Series>
            {
                new() { Id = seriesId, PlaylistId = playlistId, Name = "Breaking Bad" }
            });

            var method = typeof(MainViewModel).GetMethod("UpdateHistoryBucketsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            await (Task)method.Invoke(vm, new object[] { Enumerable.Empty<Channel>() })!;

            var item = Assert.Single(vm.HistorySeriesItems);
            Assert.Equal("Breaking Bad", item.Name);
            Assert.True(item.LastWatchedEpisodeAt.HasValue);
        }

        [Fact]
        public async Task ApplyProfileProgressAsync_ShouldApplySeriesProgressToHdFhdAndLanguageVariants()
        {
            var vm = CreateViewModel();
            const int profileId = 1;

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.CurrentProfileId))?.SetValue(vm, profileId);

            var fhdSeries = BuildSeriesVariant("TR | Breaking Bad FHD", "Pilot FHD", "http://provider-a/series/bb-s01e01-fhd.mp4");
            var hdSeries = BuildSeriesVariant("EN | Breaking Bad HD", "Pilot HD", "http://provider-a/series/bb-s01e01-hd.mp4");

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Acc", Url = "http://provider-a" };
                context.ProviderAccounts.Add(account);
                context.Profiles.Add(new Profile { Id = profileId, Name = "Test", ProviderAccount = account });
                context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
                {
                    ProfileId = profileId,
                    SeriesKey = "breaking bad",
                    SeriesTitle = "Breaking Bad",
                    SeasonNumber = 1,
                    EpisodeNumber = 1,
                    LastWatchedAt = DateTime.UtcNow,
                    StoppedAt = TimeSpan.FromMinutes(24),
                    Duration = TimeSpan.FromMinutes(50),
                    Completed = false
                });
                await context.SaveChangesAsync();
            }

            using (var context = new AppDbContext(_options))
            {
                var method = typeof(MainViewModel).GetMethod("ApplyProfileProgressAsync", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method);

                await (Task)method.Invoke(vm, new object[] { fhdSeries, context })!;
                await (Task)method.Invoke(vm, new object[] { hdSeries, context })!;
            }

            var fhdEpisode = Assert.Single(fhdSeries.Seasons.Single().Episodes);
            Assert.Equal(TimeSpan.FromMinutes(24), fhdEpisode.WatchedPosition);
            Assert.Equal(TimeSpan.FromMinutes(50), fhdEpisode.Duration);
            Assert.False(fhdEpisode.IsCompleted);

            var hdEpisode = Assert.Single(hdSeries.Seasons.Single().Episodes);
            Assert.Equal(TimeSpan.FromMinutes(24), hdEpisode.WatchedPosition);
            Assert.Equal(TimeSpan.FromMinutes(50), hdEpisode.Duration);
            Assert.False(hdEpisode.IsCompleted);
        }

        private static void SetAllSeriesCache(MainViewModel vm, List<Series> series)
        {
            var cacheField = typeof(MainViewModel).GetField("_allSeriesCache", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(cacheField);
            cacheField.SetValue(vm, series);
        }

        private static Series BuildSeriesVariant(string seriesName, string episodeName, string streamUrl)
        {
            var series = new Series { Name = seriesName };
            var season = new Season { Series = series, SeasonNumber = 1 };
            var episode = new Episode
            {
                Season = season,
                EpisodeNumber = 1,
                Name = episodeName,
                StreamUrl = streamUrl
            };

            season.Episodes.Add(episode);
            series.Seasons.Add(season);
            return series;
        }
    }
}
