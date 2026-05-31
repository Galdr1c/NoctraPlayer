using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Noctra.Tests
{
    public class SimpleDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public SimpleDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new AppDbContext(_options);
    }

    public class PlaylistServiceIntegrationTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly Mock<IM3UParser> _parserMock;
        private readonly Mock<IMediaService> _mediaServiceMock;
        private readonly Mock<IPlaylistOrganizerService> _organizerMock;
        private readonly Mock<IEpgService> _epgServiceMock;
        private readonly Mock<ISettingsService> _settingsServiceMock;
        private readonly Mock<ILocalizationService> _localizationServiceMock;
        private readonly LanguageDetectionService _languageDetection;
        private readonly EpgSourceResolver _epgSourceResolver;
        private readonly HttpClient _httpClient;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public PlaylistServiceIntegrationTests()
        {
            // Setup In-Memory SQLite
            _connection = new SqliteConnection("Data Source=PlaylistServiceTests;Mode=Memory;Cache=Shared");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using (var context = new AppDbContext(_options))
            {
                context.Database.EnsureCreated();
            }

            _contextFactory = new SimpleDbContextFactory(_options);

            _parserMock = new Mock<IM3UParser>();
            _mediaServiceMock = new Mock<IMediaService>();
            _organizerMock = new Mock<IPlaylistOrganizerService>();
            _epgServiceMock = new Mock<IEpgService>();
            _settingsServiceMock = new Mock<ISettingsService>();
            _localizationServiceMock = new Mock<ILocalizationService>();
            _languageDetection = new LanguageDetectionService();
            _epgSourceResolver = new EpgSourceResolver();
            _httpClient = new HttpClient();

            // Setup default settings
            _settingsServiceMock.Setup(s => s.Settings).Returns(new AppSettings());

            // Setup default localization behavior: return key as string
            _localizationServiceMock.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);

            // Default organizer behavior: just return what's given
            _organizerMock.Setup(o => o.Organize(It.IsAny<List<Channel>>(), It.IsAny<bool>()))
                .Returns<List<Channel>, bool>((c, _) => c);

            _mediaServiceMock
                .Setup(m => m.AggregateContentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            _connection.Close();
            _httpClient.Dispose();
        }

        [Fact]
        public async Task AddFromChannelsAsync_ShouldCreatePlaylistAndChannels()
        {
            // Arrange
            var service = CreateService();
            var channels = new List<Channel>
            {
                new Channel { Name = "Test Channel 1", StreamUrl = "http://test.com/1", Type = ChannelType.Live },
                new Channel { Name = "Test Channel 2", StreamUrl = "http://test.com/2", Type = ChannelType.Live }
            };

            // Act
            var playlist = await service.AddFromChannelsAsync("Test Playlist", "http://source.com", channels);

            // Give background tasks from AddFromChannelsAsync a moment to run/settle
            await Task.Delay(200);

            // Assert
            using var context = new AppDbContext(_options);
            Assert.NotNull(playlist);
            Assert.Equal(1, await context.Playlists.CountAsync());
            Assert.Equal(2, await context.Channels.CountAsync());
            
            var dbPlaylist = await context.Playlists.FirstAsync();
            Assert.Equal(2, dbPlaylist.ChannelCount);
        }

        [Fact]
        public async Task RefreshAsync_ShouldAddOnlyNewChannels_DiffUpsertLogic()
        {
            // Arrange
            var service = CreateService();
            var initialChannels = new List<Channel>
            {
                new Channel { Name = "Existing 1", StreamUrl = "url1", Type = ChannelType.Live },
                new Channel { Name = "Existing 2", StreamUrl = "url2", Type = ChannelType.Live }
            };

            var playlist = await service.AddFromChannelsAsync("Test Playlist", "http://source.com", initialChannels);
            
            // Setup Parser to return more channels for refresh
            var updatedChannels = new List<Channel>
            {
                new Channel { Name = "Existing 1", StreamUrl = "url1", Type = ChannelType.Live }, // Duplicate
                new Channel { Name = "Existing 2", StreamUrl = "url2", Type = ChannelType.Live }, // Duplicate
                new Channel { Name = "New 3", StreamUrl = "url3", Type = ChannelType.Live }       // New
            };
            
            _parserMock.Setup(p => p.ParseFromUrlAsync(playlist.Url)).ReturnsAsync(updatedChannels);
            _parserMock.Setup(p => p.ParseFromFileAsync(It.IsAny<string>())).ReturnsAsync(updatedChannels);

            // Give background tasks from AddFromChannelsAsync a moment to run/settle
            await Task.Delay(200);

            // Act
            var updatedPlaylist = await service.RefreshAsync(playlist.Id);

            // Give background tasks from RefreshAsync a moment to run
            await Task.Delay(200);

            // Assert
            using var context = new AppDbContext(_options);
            Assert.Equal(3, await context.Channels.CountAsync(c => c.PlaylistId == playlist.Id));
            Assert.Equal(3, updatedPlaylist.ChannelCount);
            
            var newChannel = await context.Channels.FirstOrDefaultAsync(c => c.Name == "New 3");
            Assert.NotNull(newChannel);
        }

        [Fact]
        public async Task RefreshAsync_WithNoChanges_ShouldNotAddAnyChannels()
        {
            // Arrange
            var service = CreateService();
            var channels = new List<Channel>
            {
                new Channel { Name = "Channel 1", StreamUrl = "url1", Type = ChannelType.Live }
            };

            var playlist = await service.AddFromChannelsAsync("Test Playlist", "http://source.com", channels);
            
            _parserMock.Setup(p => p.ParseFromUrlAsync(playlist.Url)).ReturnsAsync(channels);

            // Act
            await service.RefreshAsync(playlist.Id);

            // Assert
            using var context = new AppDbContext(_options);
            Assert.Equal(1, await context.Channels.CountAsync());
        }

        [Fact]
        public async Task RefreshAsync_AfterSuccessfulParse_ReplacesDerivedPlaylistData()
        {
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Refresh Source",
                    Url = "http://source.com/list.m3u",
                    IsActive = true,
                    ChannelCount = 1,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };
                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "Old Channel",
                    StreamUrl = "old-url",
                    TvgId = "old.epg",
                    Type = ChannelType.Live
                });
                context.Series.Add(new Series
                {
                    PlaylistId = playlistId,
                    Name = "Old Series"
                });
                context.EpgPrograms.Add(new EpgProgram
                {
                    ChannelId = "old.epg",
                    Title = "Old Program",
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow.AddHours(1)
                });
                await context.SaveChangesAsync();
            }

            var refreshedChannels = new List<Channel>
            {
                new Channel { Name = "New Channel", StreamUrl = "new-url", Type = ChannelType.Live }
            };
            _parserMock.Setup(p => p.ParseFromUrlAsync("http://source.com/list.m3u")).ReturnsAsync(refreshedChannels);

            await service.RefreshAsync(playlistId);

            using (var context = new AppDbContext(_options))
            {
                var channel = Assert.Single(await context.Channels.Where(c => c.PlaylistId == playlistId).ToListAsync());
                Assert.Equal("New Channel", channel.Name);
                Assert.False(await context.Series.AnyAsync(s => s.PlaylistId == playlistId));
                Assert.False(await context.EpgPrograms.AnyAsync(e => e.ChannelId == "old.epg"));
            }
        }

        [Fact]
        public async Task RefreshAsync_WithChildProfile_ShouldApplyFilter()
        {
            // Arrange
            var service = CreateService();
            
            int profileId;
            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Test Account", Url = "http://test.com" };
                context.ProviderAccounts.Add(account);
                await context.SaveChangesAsync();

                var profile = new Profile { Name = "Child", IsChild = true, ProviderAccountId = account.Id };
                context.Profiles.Add(profile);
                await context.SaveChangesAsync();
                profileId = profile.Id;
            }

            var channels = new List<Channel>
            {
                new Channel { Name = "Kid Show", GroupTitle = "Kids", Type = ChannelType.Live },
                new Channel { Name = "Adult Content", GroupTitle = "Adult", Type = ChannelType.Live }
            };

            _parserMock.Setup(p => p.ParseFromUrlAsync("http://test.com/m3u")).ReturnsAsync(channels);

            var playlist = await service.AddFromUrlAsync("Test", "http://test.com/m3u", profileId: profileId);

            // Give background tasks a moment
            await Task.Delay(200);

            // Assert
            using (var context = new AppDbContext(_options))
            {
                Assert.Equal(1, await context.Channels.CountAsync(c => c.PlaylistId == playlist.Id));
                var dbChannel = await context.Channels.FirstAsync();
                Assert.Equal("Kid Show", dbChannel.Name);
            }
        }

        [Fact]
        public async Task AddFromChannelsAsync_ExistingPlaylist_RepairsLinearMovieGroupChannelToLive()
        {
            // Arrange
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "IPTV-org TR",
                    Url = "https://iptv-org.github.io/iptv/countries/tr.m3u",
                    IsActive = true,
                    ChannelCount = 1,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "MovieSmart Turk (576p)",
                    GroupTitle = "Movies",
                    StreamUrl = "https://example.com/moviesmart/master.m3u8?token=1",
                    Type = ChannelType.VOD
                });
                await context.SaveChangesAsync();
            }

            // Act
            await service.AddFromChannelsAsync(
                "IPTV-org TR",
                "https://iptv-org.github.io/iptv/countries/tr.m3u",
                Array.Empty<Channel>());

            // Assert
            using (var context = new AppDbContext(_options))
            {
                var channel = await context.Channels.SingleAsync(c => c.PlaylistId == playlistId);
                Assert.Equal(ChannelType.Live, channel.Type);
                Assert.Equal("MovieSmart Turk (576p)", channel.Name);
            }
        }

        [Fact]
        public async Task GetChannelsFilteredAsync_RepairsExistingLinearMovieGroupChannelBeforeFiltering()
        {
            // Arrange
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "IPTV-org",
                    Url = "https://iptv-org.github.io/iptv/index.m3u",
                    IsActive = true,
                    ChannelCount = 1,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "MovieSmart Turk (576p)",
                    GroupTitle = "Movies",
                    StreamUrl = "https://example.com/moviesmart/master.m3u8",
                    Type = ChannelType.VOD
                });
                await context.SaveChangesAsync();
            }

            // Act
            var movies = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.VOD);
            var live = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live);

            // Assert
            Assert.Empty(movies);
            var channel = Assert.Single(live);
            Assert.Equal("MovieSmart Turk (576p)", channel.Name);
            Assert.Equal(ChannelType.Live, channel.Type);
        }

        [Fact]
        public async Task GetChannelsFilteredAsync_RepairsExistingDiziGroupLinearChannelsButKeepsEpisodes()
        {
            // Arrange
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Provider",
                    Url = "http://provider.test/list.m3u",
                    IsActive = true,
                    ChannelCount = 3,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.AddRange(
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "TR • BEIN SERIES 1",
                        GroupTitle = "TR • DIZI",
                        StreamUrl = "http://provider/live/bein-series-1",
                        Type = ChannelType.Series
                    },
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "TR • FX",
                        GroupTitle = "TR • DIZI",
                        StreamUrl = "http://provider/live/fx",
                        Type = ChannelType.Series
                    },
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "Breaking Bad S01E01",
                        GroupTitle = "TR • DIZI",
                        StreamUrl = "http://provider/content/breaking-bad-s01e01.mp4",
                        Type = ChannelType.Series
                    });
                await context.SaveChangesAsync();
            }

            // Act
            var live = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live);
            var series = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Series);

            // Assert
            Assert.Equal(2, live.Count);
            Assert.Contains(live, c => c.Name == "TR • BEIN SERIES 1");
            Assert.Contains(live, c => c.Name == "TR • FX");

            var episode = Assert.Single(series);
            Assert.Equal("Breaking Bad S01E01", episode.Name);
            _mediaServiceMock.Verify(m => m.AggregateContentAsync(playlistId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetChannelsFilteredAsync_DoesNotRepairProviderSeriesVirtualLinksToLive()
        {
            // Arrange
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Xtream",
                    Url = "http://provider.test/xtream",
                    IsActive = true,
                    ChannelCount = 2,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.AddRange(
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "The Last of Us",
                        GroupTitle = "Series",
                        StreamUrl = "xtream-series://123",
                        Type = ChannelType.Series
                    },
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "Dark",
                        GroupTitle = "Series",
                        StreamUrl = "stalker-series://456",
                        Type = ChannelType.Series
                    });
                await context.SaveChangesAsync();
            }

            // Act
            var series = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Series);
            var live = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live);

            // Assert
            Assert.Equal(2, series.Count);
            Assert.Empty(live);
            Assert.Contains(series, c => c.StreamUrl == "xtream-series://123");
            Assert.Contains(series, c => c.StreamUrl == "stalker-series://456");
        }

        [Fact]
        public async Task GetChannelsFilteredAsync_RepairsYearTitledProxyMoviesFromLiveToVod()
        {
            // Arrange
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "M3U Movies",
                    Url = "http://provider.test/get.php",
                    IsActive = true,
                    ChannelCount = 2,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.AddRange(
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "É Quase Verdade (2026)",
                        GroupTitle = "Filmes | Ficcao",
                        StreamUrl = "http://provider.test/stream/12345",
                        Type = ChannelType.Live
                    },
                    new Channel
                    {
                        PlaylistId = playlistId,
                        Name = "Canal 2026",
                        GroupTitle = "ABERTOS",
                        StreamUrl = "http://provider.test/channel/2026",
                        Type = ChannelType.Live
                    });
                await context.SaveChangesAsync();
            }

            // Act
            var movies = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.VOD);
            var live = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live);

            // Assert
            var movie = Assert.Single(movies);
            Assert.Equal("É Quase Verdade (2026)", movie.Name);

            var channel = Assert.Single(live);
            Assert.Equal("Canal 2026", channel.Name);
        }
        private PlaylistService CreateService()
        {
            return new PlaylistService(
                _contextFactory,
                _parserMock.Object,
                _mediaServiceMock.Object,
                _organizerMock.Object,
                _languageDetection,
                _epgSourceResolver,
                _epgServiceMock.Object,
                _httpClient,
                _settingsServiceMock.Object,
                _localizationServiceMock.Object
            );
        }
    }
}
