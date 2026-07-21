using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Noctra.Core.Collections;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// Verifies that OnPropertyChanged fires after SetItems for collections that the
    /// MainWindow warmup mechanism monitors (ContinueWatching, MyList, FavoriteChannels,
    /// HistoryLiveChannels, HistoryVodChannels, HistorySeriesItems, HistoryChannels).
    /// </summary>
    public class ViewModelWarmupNotificationsTests : IDisposable
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
        private readonly Mock<IAppVersionService> _appVersionServiceMock = new();
        private readonly Mock<ILocalizationService> _localizationServiceMock = new();

        public ViewModelWarmupNotificationsTests()
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

            _dispatcherServiceMock.Setup(d => d.BeginInvoke(It.IsAny<Action>()))
                .Callback<Action>(a => a());

            _dispatcherServiceMock.Setup(d => d.InvokeAsync(It.IsAny<Func<Task>>()))
                .Returns((Func<Task> f) => f());

            _settingsServiceMock.Setup(s => s.Settings).Returns(new AppSettings());
            _localizationServiceMock.Setup(l => l.GetString(It.IsAny<string>())).Returns("Test");
            _contextFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(() => new AppDbContext(_options));

            _mediaServiceMock.Setup(m => m.GetSeriesListAsync(It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new List<Series>());
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
                _appVersionServiceMock.Object,
                _localizationServiceMock.Object,
                _loggerMock.Object
            );
        }

        /// <summary>
        /// Helper: captures PropertyChanged events into a list.
        /// </summary>
        private static List<string> TrackPropertyChanges(INotifyPropertyChanged source)
        {
            var fired = new List<string>();
            source.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);
            return fired;
        }

        // ────────────────────────────────────────────────────────────
        // UpdateContinueWatchingRailAsync
        // ────────────────────────────────────────────────────────────

        [Fact]
        public async Task UpdateContinueWatchingRailAsync_FiresOnPropertyChanged_WhenNoProfile()
        {
            // Arrange — no profile/playlist set
            var vm = CreateViewModel();
            var fired = TrackPropertyChanges(vm);

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateContinueWatchingRailAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, null)!;
            await task;
            // Wait for the 100ms debounce to complete
            await Task.Delay(200);

            // Assert
            Assert.Contains(nameof(MainViewModel.ContinueWatching), fired);
            Assert.Empty(vm.ContinueWatching);
        }

        [Fact]
        public async Task UpdateContinueWatchingRailAsync_FiresOnPropertyChanged_WithProfile()
        {
            // Arrange — set profile/playlist so we exercise the DB query path
            var vm = CreateViewModel();
            int profileId = 1;
            int playlistId = 10;

            var propertyInfo = typeof(MainViewModel).GetProperty("CurrentProfileId");
            propertyInfo?.SetValue(vm, profileId);

            var selectedPlaylistField = typeof(MainViewModel).GetProperty("SelectedPlaylist");
            selectedPlaylistField?.SetValue(vm, new Playlist
            {
                Id = playlistId,
                Name = "Test",
                ProfileId = profileId,
                Url = "http://test.com"
            });

            // Seed required FK entities
            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "A", Url = "..." };
                context.ProviderAccounts.Add(account);
                context.Profiles.Add(new Profile { Id = profileId, Name = "P", ProviderAccount = account });
                context.Playlists.Add(new Playlist { Id = playlistId, ProfileId = profileId, Name = "PL", Url = "http://test.com" });
                await context.SaveChangesAsync();
            }

            var fired = TrackPropertyChanges(vm);

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateContinueWatchingRailAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, null)!;
            await task;
            await Task.Delay(200); // debounce

            // Assert — OnPropertyChanged fires even when there is no watch history
            Assert.Contains(nameof(MainViewModel.ContinueWatching), fired);
            Assert.Empty(vm.ContinueWatching);
        }

        // ────────────────────────────────────────────────────────────
        // UpdateMyList
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void UpdateMyList_FiresOnPropertyChanged()
        {
            // Arrange
            var vm = CreateViewModel();

            // Populate channels with one MyList item
            vm.Channels = new BatchObservableCollection<Channel>(new[]
            {
                new Channel { Id = 1, Name = "A", Type = ChannelType.VOD, StreamUrl = "s1", IsInMyList = true }
            });

            // Set _allSeriesCache via reflection
            var cacheField = typeof(MainViewModel).GetField("_allSeriesCache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            cacheField!.SetValue(vm, new List<Series>());

            var fired = TrackPropertyChanges(vm);

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateMyList",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(vm, null);

            // Assert
            Assert.Contains(nameof(MainViewModel.MyList), fired);
            Assert.NotEmpty(vm.MyList);
        }

        [Fact]
        public void UpdateMyList_FiresOnPropertyChanged_WhenEmpty()
        {
            // Arrange — no channels with IsInMyList
            var vm = CreateViewModel();
            vm.Channels = new BatchObservableCollection<Channel>(new[]
            {
                new Channel { Id = 1, Name = "A", Type = ChannelType.VOD, StreamUrl = "s1", IsInMyList = false }
            });
            var cacheField = typeof(MainViewModel).GetField("_allSeriesCache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            cacheField!.SetValue(vm, new List<Series>());

            var fired = TrackPropertyChanges(vm);

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateMyList",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(vm, null);

            // Assert — still fires even when empty
            Assert.Contains(nameof(MainViewModel.MyList), fired);
            Assert.Empty(vm.MyList);
        }

        // ────────────────────────────────────────────────────────────
        // UpdateFavoriteChannels
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void UpdateFavoriteChannels_FiresOnPropertyChanged()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.Channels = new BatchObservableCollection<Channel>(new[]
            {
                new Channel { Id = 1, Name = "A", Type = ChannelType.Live, StreamUrl = "s1", IsFavorite = true }
            });
            var cacheField = typeof(MainViewModel).GetField("_allSeriesCache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            cacheField!.SetValue(vm, new List<Series>());

            var fired = TrackPropertyChanges(vm);

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateFavoriteChannels",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(vm, null);

            // Assert
            Assert.Contains(nameof(MainViewModel.FavoriteChannels), fired);
            Assert.NotEmpty(vm.FavoriteChannels);
        }

        // ────────────────────────────────────────────────────────────
        // UpdateHistoryBucketsAsync
        // ────────────────────────────────────────────────────────────

        [Fact]
        public async Task UpdateHistoryBucketsAsync_FiresOnPropertyChanged_ForAllBuckets()
        {
            // Arrange
            var vm = CreateViewModel();
            var fired = TrackPropertyChanges(vm);

            var sourceChannels = new List<Channel>
            {
                new() { Id = 1, Name = "Live1", Type = ChannelType.Live },
                new() { Id = 2, Name = "VOD1", Type = ChannelType.VOD }
            };

            // Also set _allSeriesCache to something with LastWatchedEpisodeAt so
            // the cached path fires HistorySeriesItems.
            var cacheField = typeof(MainViewModel).GetField("_allSeriesCache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            cacheField!.SetValue(vm, new List<Series>
            {
                new() { Id = 10, Name = "Series A", LastWatchedEpisodeAt = DateTime.UtcNow }
            });

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateHistoryBucketsAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, new object[] { sourceChannels })!;
            await task;

            // Assert
            Assert.Contains(nameof(MainViewModel.HistoryLiveChannels), fired);
            Assert.Contains(nameof(MainViewModel.HistoryVodChannels), fired);
            Assert.Contains(nameof(MainViewModel.HistorySeriesItems), fired);
            Assert.Single(vm.HistoryLiveChannels);
            Assert.Single(vm.HistoryVodChannels);
        }

        [Fact]
        public async Task UpdateHistoryBucketsAsync_FiresOnPropertyChanged_WhenAllEmpty()
        {
            // Arrange
            var vm = CreateViewModel();
            var fired = TrackPropertyChanges(vm);

            // Empty source channels, empty cache
            var cacheField = typeof(MainViewModel).GetField("_allSeriesCache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            cacheField!.SetValue(vm, new List<Series>());

            // Act
            var method = typeof(MainViewModel).GetMethod("UpdateHistoryBucketsAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, new object[] { new List<Channel>() })!;
            await task;

            // Assert — still fires even when empty
            Assert.Contains(nameof(MainViewModel.HistoryLiveChannels), fired);
            Assert.Contains(nameof(MainViewModel.HistoryVodChannels), fired);
            Assert.Contains(nameof(MainViewModel.HistorySeriesItems), fired);
            Assert.Empty(vm.HistoryLiveChannels);
            Assert.Empty(vm.HistoryVodChannels);
            Assert.Empty(vm.HistorySeriesItems);
        }

        // ────────────────────────────────────────────────────────────
        // RefreshPersonalListsFromDatabaseAsync
        // ────────────────────────────────────────────────────────────

        [Fact]
        public async Task RefreshPersonalListsFromDatabaseAsync_FiresOnPropertyChanged_ForAllLists()
        {
            // Arrange
            var vm = CreateViewModel();
            int profileId = 1;
            int playlistId = 10;

            // Set profile
            var profileProp = typeof(MainViewModel).GetProperty("CurrentProfileId");
            profileProp?.SetValue(vm, profileId);

            var playlistProp = typeof(MainViewModel).GetProperty("SelectedPlaylist");
            playlistProp?.SetValue(vm, new Playlist { Id = playlistId, Name = "P", ProfileId = profileId });

            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "A", Url = "..." };
                context.ProviderAccounts.Add(account);
                context.Profiles.Add(new Profile { Id = profileId, Name = "P", ProviderAccount = account });
                context.Playlists.Add(new Playlist { Id = playlistId, ProfileId = profileId, Name = "PL" });

                context.Channels.AddRange(
                    new Channel { Id = 1, PlaylistId = playlistId, Name = "My1", StreamUrl = "u1", Type = ChannelType.VOD, IsInMyList = true },
                    new Channel { Id = 2, PlaylistId = playlistId, Name = "Fav1", StreamUrl = "u2", Type = ChannelType.Live, IsFavorite = true },
                    new Channel { Id = 3, PlaylistId = playlistId, Name = "Both", StreamUrl = "u3", Type = ChannelType.VOD, IsInMyList = true, IsFavorite = true }
                );

                // Watch history for history channels
                context.WatchHistories.Add(new WatchHistory
                {
                    ProfileId = profileId,
                    ChannelId = 1,
                    WatchedAt = DateTime.UtcNow,
                    StoppedAt = TimeSpan.FromMinutes(5),
                    Completed = false
                });
                await context.SaveChangesAsync();
            }

            var fired = TrackPropertyChanges(vm);

            // Act
            var method = typeof(MainViewModel).GetMethod("RefreshPersonalListsFromDatabaseAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, null)!;
            await task;

            // Assert
            Assert.Contains(nameof(MainViewModel.MyList), fired);
            Assert.Contains(nameof(MainViewModel.FavoriteChannels), fired);
            Assert.Contains(nameof(MainViewModel.HistoryChannels), fired);
            Assert.NotEmpty(vm.MyList);
            Assert.NotEmpty(vm.FavoriteChannels);
            Assert.NotEmpty(vm.HistoryChannels);
        }
    }
}
