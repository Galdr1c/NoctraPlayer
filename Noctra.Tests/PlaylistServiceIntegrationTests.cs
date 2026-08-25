using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Core.Services;
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

    [CollectionDefinition(nameof(PlaylistServiceIntegrationTests), DisableParallelization = true)]
    public sealed class PlaylistServiceIntegrationTestsCollection
    {
    }

    [Collection(nameof(PlaylistServiceIntegrationTests))]
    public class PlaylistServiceIntegrationTests : IDisposable
    {
        private readonly string _databasePath;
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
            // Setup isolated SQLite database per test instance.
            _databasePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"NoctraPlaylistServiceTests-{Guid.NewGuid():N}.db");

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_databasePath}")
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
            _httpClient.Dispose();
            try { System.IO.File.Delete(_databasePath); } catch { /* test cleanup best effort */ }
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

        [Theory]
        [InlineData(ChannelSortOrder.NewestFirst)]
        [InlineData(ChannelSortOrder.OldestFirst)]
        [InlineData(ChannelSortOrder.NameAsc)]
        [InlineData(ChannelSortOrder.NameDesc)]
        public async Task GetChannelsFilteredPageAsync_SearchPrioritizesLiveBeforeVod(
            ChannelSortOrder sortOrder)
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Search priority",
                "https://provider.test/search-priority",
                new[]
                {
                    new Channel
                    {
                        Name = "Sports News Live",
                        StreamUrl = "https://provider.test/live",
                        Type = ChannelType.Live
                    },
                    new Channel
                    {
                        Name = "Sports News Movie",
                        StreamUrl = "https://provider.test/movie",
                        Type = ChannelType.VOD
                    }
                });

            var page = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 0,
                take: 1,
                searchText: "Sports News",
                sortOrder: sortOrder);

            Assert.Single(page);
            Assert.Equal(ChannelType.Live, page[0].Type);

            var nextPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 1,
                take: 1,
                searchText: "Sports News",
                sortOrder: sortOrder,
                cursor: new ContentPageCursor(page[0].Id));

            Assert.Single(nextPage);
            Assert.Equal(ChannelType.VOD, nextPage[0].Type);
        }

        [Theory]
        [InlineData(ChannelSortOrder.NewestFirst)]
        [InlineData(ChannelSortOrder.OldestFirst)]
        [InlineData(ChannelSortOrder.NameAsc)]
        [InlineData(ChannelSortOrder.NameDesc)]
        public async Task GetChannelsFilteredPageAsync_AllGroupsPlacesAdultCategoriesAfterNormalAcrossPages(
            ChannelSortOrder sortOrder)
        {
            var service = CreateService();
            var channels = BuildAdultLastPagingChannels(sortOrder);
            var playlist = await service.AddFromChannelsAsync(
                $"Adult-last {sortOrder}",
                $"https://provider.test/adult-last/{sortOrder}",
                channels);
            var expectedNames = BuildExpectedAdultLastNames(channels, sortOrder);
            // Metadata-cache casing (Trim()med originals of the raw DB values).
            string[] adultGroups = ["FOR ADULTS", "para adultos"];

            var firstPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 0,
                take: 2,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                adultGroupsLast: adultGroups);
            var secondPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 2,
                take: 2,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                cursor: new ContentPageCursor(firstPage[^1].Id),
                adultGroupsLast: adultGroups);

            Assert.Equal(2, firstPage.Count);
            Assert.All(firstPage, channel => Assert.StartsWith("Normal", channel.GroupTitle));
            Assert.Equal(2, secondPage.Count);
            Assert.All(secondPage, channel =>
                Assert.True(AdultCategoryClassifier.IsAdultCategory(channel.GroupTitle)));
            Assert.Equal(
                expectedNames,
                firstPage.Concat(secondPage).Select(channel => channel.Name));
        }

        [Fact]
        public async Task GetChannelsFilteredPageAsync_SelectedAdultGroupKeepsRequestedNameSort()
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Selected adult group",
                "https://provider.test/selected-adult",
                new[]
                {
                    new Channel { Name = "Zulu", StreamUrl = "https://provider.test/z", GroupTitle = "For Adults", Type = ChannelType.VOD },
                    new Channel { Name = "Alpha", StreamUrl = "https://provider.test/a", GroupTitle = "For Adults", Type = ChannelType.VOD }
                });

            var page = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 0,
                take: 10,
                group: "For Adults",
                type: ChannelType.VOD,
                sortOrder: ChannelSortOrder.NameAsc);

            Assert.Equal(new[] { "Alpha", "Zulu" }, page.Select(channel => channel.Name));
        }

        [Theory]
        [InlineData(ChannelSortOrder.NewestFirst)]
        [InlineData(ChannelSortOrder.OldestFirst)]
        public async Task GetChannelsFilteredPageAsync_TwoPhaseKeysetCrossesAdultBoundaryWithoutGapsOrDupes(
            ChannelSortOrder sortOrder)
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                $"Two-phase {sortOrder}",
                $"https://provider.test/two-phase/{sortOrder}",
                new[]
                {
                    new Channel { Name = "N1", StreamUrl = "https://provider.test/n1", GroupTitle = "News", Type = ChannelType.VOD },
                    new Channel { Name = "N2", StreamUrl = "https://provider.test/n2", GroupTitle = "Sports", Type = ChannelType.VOD },
                    new Channel { Name = "N3", StreamUrl = "https://provider.test/n3", GroupTitle = "News", Type = ChannelType.VOD },
                    // Spaced raw value pins the TRIM match against metadata-trimmed caller names.
                    new Channel { Name = "A1", StreamUrl = "https://provider.test/a1", GroupTitle = " FOR ADULTS ", Type = ChannelType.VOD },
                    new Channel { Name = "A2", StreamUrl = "https://provider.test/a2", GroupTitle = "para adultos", Type = ChannelType.VOD },
                    new Channel { Name = "A3", StreamUrl = "https://provider.test/a3", GroupTitle = " FOR ADULTS ", Type = ChannelType.VOD }
                });
            // Names mirror what GetChannelGroupMetadataAsync feeds the ViewModel:
            // Trim()med originals, so the SQL IN match stays byte-exact like production.
            string[] adultGroups = ["FOR ADULTS", "para adultos"];

            var firstPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 0,
                take: 2,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                adultGroupsLast: adultGroups);
            var secondPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 2,
                take: 2,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                cursor: new ContentPageCursor(firstPage[^1].Id),
                adultGroupsLast: adultGroups);
            Assert.True(secondPage.Count < 2 || AdultCategoryClassifier.IsAdultCategory(secondPage[^1].GroupTitle),
                "boundary page must end inside the adult segment");
            var thirdPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 4,
                take: 2,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                cursor: new ContentPageCursor(secondPage[^1].Id, AdultPhase: true),
                adultGroupsLast: adultGroups);

            var expected = sortOrder == ChannelSortOrder.OldestFirst
                ? new[] { "N1", "N2", "N3", "A1", "A2", "A3" }
                : new[] { "N3", "N2", "N1", "A3", "A2", "A1" };
            var combined = firstPage.Concat(secondPage).Concat(thirdPage).Select(channel => channel.Name).ToArray();
            Assert.Equal(expected, combined);
        }

        [Fact]
        public async Task GetChannelsFilteredPageAsync_CallerSuppliedAdultGroupsOverrideDbDiscovery()
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Caller authority",
                "https://provider.test/caller-authority",
                new[]
                {
                    // Classifier would flag "XXX Zone"; the caller deliberately does not include it.
                    new Channel { Name = "X1", StreamUrl = "https://provider.test/x1", GroupTitle = "XXX Zone", Type = ChannelType.VOD },
                    new Channel { Name = "N1", StreamUrl = "https://provider.test/n1", GroupTitle = "News", Type = ChannelType.VOD },
                    new Channel { Name = "N2", StreamUrl = "https://provider.test/n2", GroupTitle = "News", Type = ChannelType.VOD }
                });

            var page = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 0,
                take: 10,
                type: ChannelType.VOD,
                sortOrder: ChannelSortOrder.NewestFirst,
                adultGroupsLast: ["News"]);

            Assert.Equal(new[] { "X1", "N2", "N1" }, page.Select(channel => channel.Name));
        }

        [Theory]
        [InlineData(ChannelSortOrder.NewestFirst)]
        [InlineData(ChannelSortOrder.OldestFirst)]
        public async Task GetChannelsFilteredPageAsync_EmptyAuthoritativeAdultSetWithCursor_PagesWithoutDuplication(
            ChannelSortOrder sortOrder)
        {
            var service = CreateService();
            var channels = Enumerable.Range(1, 65)
                .Select(index => new Channel
                {
                    Name = $"VOD {index:000}",
                    StreamUrl = $"https://provider.test/no-adult/{index}",
                    Type = ChannelType.VOD
                })
                .ToArray();
            var playlist = await service.AddFromChannelsAsync(
                $"No adult paging {sortOrder}",
                $"https://provider.test/no-adult/{sortOrder}",
                channels);

            // Explicitly empty authoritative set: must behave as plain keyset.
            var firstPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 0,
                take: 30,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                adultGroupsLast: []);
            var secondPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 30,
                take: 30,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                cursor: new ContentPageCursor(firstPage[^1].Id),
                adultGroupsLast: []);
            var thirdPage = await service.GetChannelsFilteredPageAsync(
                playlist.Id,
                skip: 60,
                take: 30,
                type: ChannelType.VOD,
                sortOrder: sortOrder,
                cursor: new ContentPageCursor(secondPage[^1].Id),
                adultGroupsLast: []);

            var expectedIds = sortOrder == ChannelSortOrder.OldestFirst
                ? Enumerable.Range(1, 65).ToArray()
                : Enumerable.Range(1, 65).Reverse().ToArray();
            var combinedIds = firstPage.Concat(secondPage).Concat(thirdPage)
                .Select(channel => channel.Id)
                .ToArray();
            Assert.Equal(expectedIds, combinedIds);
            Assert.Equal(65, combinedIds.Distinct().Count());
        }

        [Fact]
        public async Task GetChannelGroupMetadataAsync_WhenCancelled_StopsAtSqliteBoundary()
        {
            var service = CreateService();
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.GetChannelGroupMetadataAsync(1, cancellationSource.Token));
        }

        [Fact]
        public async Task AddFromChannelsAsync_WhenChannelsEmpty_DoesNotCreatePlaylist()
        {
            var service = CreateService();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddFromChannelsAsync("Empty Playlist", "http://source.com/empty.m3u", Array.Empty<Channel>()));

            using var context = new AppDbContext(_options);
            Assert.Equal(0, await context.Playlists.CountAsync());
            Assert.Equal(0, await context.Channels.CountAsync());
        }

        [Fact]
        public async Task AddFromUrlAsync_WithStreamingChannels_UsesBatchPipeline()
        {
            const string url = "https://example.com/large.m3u";
            var service = CreateService();

            _parserMock
                .Setup(p => p.ParseFromUrlStreamAsync(url, It.IsAny<CancellationToken>()))
                .Returns(StreamChannels(501));

            var playlist = await service.AddFromUrlAsync("Remote Import", url);

            using var context = new AppDbContext(_options);
            var persisted = await context.Playlists.SingleAsync();
            Assert.Equal(playlist.Id, persisted.Id);
            Assert.Equal(url, persisted.Url);
            Assert.True(persisted.IsActive);
            Assert.Equal(501, persisted.ChannelCount);
            Assert.Equal(501, await context.Channels.CountAsync());
            _parserMock.Verify(p => p.ParseFromUrlAsync(It.IsAny<string>()), Times.Never);
            _organizerMock.Verify(
                o => o.Organize(It.IsAny<List<Channel>>(), true),
                Times.Exactly(2));
        }

        [Fact]
        public async Task AddFromUrlAsync_AfterSuccessfulParse_CompletesImportJob()
        {
            var service = CreateService();
            int profileId;
            using (var setupContext = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Import Account", Url = "http://source.com" };
                setupContext.ProviderAccounts.Add(account);
                await setupContext.SaveChangesAsync();

                var profile = new Profile { Name = "Import Profile", ProviderAccountId = account.Id };
                setupContext.Profiles.Add(profile);
                await setupContext.SaveChangesAsync();
                profileId = profile.Id;
            }

            var parsedChannels = new List<Channel>
            {
                new Channel { Name = "Live", StreamUrl = "live-url", Type = ChannelType.Live },
                new Channel { Name = "Movie", StreamUrl = "movie-url", Type = ChannelType.VOD }
            };

            _parserMock
                .Setup(p => p.ParseFromUrlAsync("http://source.com/import.m3u"))
                .ReturnsAsync(parsedChannels);

            var playlist = await service.AddFromUrlAsync("Initial Import", "http://source.com/import.m3u", profileId);

            using var context = new AppDbContext(_options);
            var job = await context.ImportJobs.SingleAsync(j => j.PlaylistId == playlist.Id);

            Assert.Equal(ImportJobKind.M3U, job.Kind);
            Assert.Equal(ImportJobStatus.Completed, job.Status);
            Assert.Equal(profileId, job.ProfileId);
            Assert.Equal("Completed", job.Stage);
            Assert.Equal(1, job.LiveCount);
            Assert.Equal(1, job.VodCount);
            Assert.Equal(0, job.SeriesCount);
        }

        [Fact]
        public async Task AddFromUrlAsync_ReportsImportProgressBeforeCompletion()
        {
            var importJobs = new Mock<IImportJobService>();
            importJobs
                .Setup(service => service.StartAsync(
                    It.IsAny<ImportJobKind>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImportJob { Id = 42, Status = ImportJobStatus.Running });

            var service = CreateService(importJobs.Object);
            var parsedChannels = new List<Channel>
            {
                new Channel { Name = "Live", StreamUrl = "live-url", Type = ChannelType.Live },
                new Channel { Name = "Movie", StreamUrl = "movie-url", Type = ChannelType.VOD },
                new Channel { Name = "Series", StreamUrl = "series-url", Type = ChannelType.Series }
            };

            _parserMock
                .Setup(p => p.ParseFromUrlAsync("http://progress.test/list.m3u"))
                .ReturnsAsync(parsedChannels);

            await service.AddFromUrlAsync("Progress", "http://progress.test/list.m3u");

            var stages = importJobs.Invocations
                .Where(invocation => invocation.Method.Name == nameof(IImportJobService.ReportProgressAsync))
                .Select(invocation => (string)invocation.Arguments[1])
                .ToList();

            Assert.Contains("Parsed", stages);
            Assert.Contains("Organized", stages);
            Assert.Contains("Writing", stages);
            Assert.Contains("Completed", stages);
            importJobs.Verify(service => service.CompleteAsync(42, "Completed", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddFromUrlAsync_WhenParseFails_FailsImportJob()
        {
            var service = CreateService();
            int profileId;
            using (var setupContext = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Import Account", Url = "http://source.com" };
                setupContext.ProviderAccounts.Add(account);
                await setupContext.SaveChangesAsync();

                var profile = new Profile { Name = "Import Profile", ProviderAccountId = account.Id };
                setupContext.Profiles.Add(profile);
                await setupContext.SaveChangesAsync();
                profileId = profile.Id;
            }

            _parserMock
                .Setup(p => p.ParseFromUrlAsync("http://source.com/fail.m3u"))
                .ThrowsAsync(new TimeoutException("provider timed out"));

            await Assert.ThrowsAsync<TimeoutException>(() =>
                service.AddFromUrlAsync("Initial Import", "http://source.com/fail.m3u", profileId));

            using var context = new AppDbContext(_options);
            var job = await context.ImportJobs.SingleAsync();

            Assert.Equal(ImportJobKind.M3U, job.Kind);
            Assert.Equal(ImportJobStatus.Failed, job.Status);
            Assert.Null(job.PlaylistId);
            Assert.Equal(profileId, job.ProfileId);
            Assert.Equal("Failed", job.Stage);
            Assert.Equal("provider timed out", job.ErrorMessage);
        }

        [Fact]
        public async Task AddFromFileAsync_AfterSuccessfulParse_CompletesImportJob()
        {
            var service = CreateService();
            int profileId;
            using (var setupContext = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "File Account", Url = "file://local" };
                setupContext.ProviderAccounts.Add(account);
                await setupContext.SaveChangesAsync();

                var profile = new Profile { Name = "File Profile", ProviderAccountId = account.Id };
                setupContext.Profiles.Add(profile);
                await setupContext.SaveChangesAsync();
                profileId = profile.Id;
            }

            var parsedChannels = new List<Channel>
            {
                new Channel { Name = "Local Live", StreamUrl = "live-url", Type = ChannelType.Live },
                new Channel { Name = "Local Series", StreamUrl = "series-url", Type = ChannelType.Series }
            };

            _parserMock
                .Setup(p => p.ParseFromFileAsync("D:\\imports\\local.m3u"))
                .ReturnsAsync(parsedChannels);

            var playlist = await service.AddFromFileAsync("Local Import", "D:\\imports\\local.m3u", profileId);

            using var context = new AppDbContext(_options);
            var job = await context.ImportJobs.SingleAsync(j => j.PlaylistId == playlist.Id);

            Assert.Equal(ImportJobKind.M3U, job.Kind);
            Assert.Equal(ImportJobStatus.Completed, job.Status);
            Assert.Equal(profileId, job.ProfileId);
            Assert.Equal("Completed", job.Stage);
            Assert.Equal(1, job.LiveCount);
            Assert.Equal(0, job.VodCount);
            Assert.Equal(1, job.SeriesCount);
        }

        [Fact]
        public async Task AddFromFileAsync_WhenParseFails_FailsImportJob()
        {
            var service = CreateService();
            int profileId;
            using (var setupContext = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "File Account", Url = "file://local" };
                setupContext.ProviderAccounts.Add(account);
                await setupContext.SaveChangesAsync();

                var profile = new Profile { Name = "File Profile", ProviderAccountId = account.Id };
                setupContext.Profiles.Add(profile);
                await setupContext.SaveChangesAsync();
                profileId = profile.Id;
            }

            _parserMock
                .Setup(p => p.ParseFromFileAsync("D:\\imports\\broken.m3u"))
                .ThrowsAsync(new IOException("local file unreadable"));

            await Assert.ThrowsAsync<IOException>(() =>
                service.AddFromFileAsync("Local Import", "D:\\imports\\broken.m3u", profileId));

            using var context = new AppDbContext(_options);
            var job = await context.ImportJobs.SingleAsync();

            Assert.Equal(ImportJobKind.M3U, job.Kind);
            Assert.Equal(ImportJobStatus.Failed, job.Status);
            Assert.Null(job.PlaylistId);
            Assert.Equal(profileId, job.ProfileId);
            Assert.Equal("Failed", job.Stage);
            Assert.Equal("local file unreadable", job.ErrorMessage);
        }

        [Fact]
        public async Task AddFromFileAsync_WhenStreamingFailsAfterFirstBatch_RemovesStagingData()
        {
            const string filePath = "D:\\imports\\partial.m3u";
            var service = CreateService();

            _parserMock
                .Setup(p => p.ParseFromFileStreamAsync(filePath, It.IsAny<CancellationToken>()))
                .Returns(StreamChannelsThenFail());

            var error = await Assert.ThrowsAsync<IOException>(() =>
                service.AddFromFileAsync("Partial Import", filePath));

            Assert.Equal("stream interrupted", error.Message);
            _parserMock.Verify(
                p => p.ParseFromFileStreamAsync(filePath, It.IsAny<CancellationToken>()),
                Times.Once);

            using var context = new AppDbContext(_options);
            Assert.Empty(await context.Playlists.ToListAsync());
            Assert.Empty(await context.Channels.ToListAsync());
        }

        [Fact]
        public async Task AddFromFileAsync_WithStreamingChannels_ActivatesCompletedPlaylist()
        {
            const string filePath = "D:\\imports\\large.m3u";
            var service = CreateService();

            _parserMock
                .Setup(p => p.ParseFromFileStreamAsync(filePath, It.IsAny<CancellationToken>()))
                .Returns(StreamChannels(501));

            var playlist = await service.AddFromFileAsync("Large Import", filePath);

            using var context = new AppDbContext(_options);
            var persisted = await context.Playlists.SingleAsync();
            var job = await context.ImportJobs.SingleAsync();

            Assert.Equal(playlist.Id, persisted.Id);
            Assert.True(persisted.IsActive);
            Assert.Equal(501, persisted.ChannelCount);
            Assert.Equal(501, await context.Channels.CountAsync());
            Assert.Equal(ImportJobStatus.Completed, job.Status);
            Assert.Equal(501, job.LiveCount);
            _organizerMock.Verify(
                o => o.Organize(It.IsAny<List<Channel>>(), true),
                Times.Exactly(2));
        }

        [Fact]
        public async Task AddFromUrlAsync_WithChildProfile_ImportsAllChannelsLikeNormalProfile()
        {
            // Çocuk profili özelliği kaldırıldı (Faz 1) — IsChild=true olan eski
            // profiller de tüm içerikleri normal profil gibi alır; filtre yok.
            var service = CreateService();
            int profileId;
            using (var setupContext = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Child Account", Url = "http://child.test" };
                setupContext.ProviderAccounts.Add(account);
                await setupContext.SaveChangesAsync();

                var profile = new Profile { Name = "Child", IsChild = true, ProviderAccountId = account.Id };
                setupContext.Profiles.Add(profile);
                await setupContext.SaveChangesAsync();
                profileId = profile.Id;
            }

            _parserMock
                .Setup(p => p.ParseFromUrlAsync("http://child.test/adult-only.m3u"))
                .ReturnsAsync(new List<Channel>
                {
                    new Channel { Name = "Adult Only", GroupTitle = "Adult", StreamUrl = "adult-url", Type = ChannelType.Live }
                });

            var playlist = await service.AddFromUrlAsync("Adult Only", "http://child.test/adult-only.m3u", profileId);

            using var context = new AppDbContext(_options);
            Assert.Equal(1, await context.Channels.CountAsync());
            Assert.Equal("Adult Only", (await context.Channels.SingleAsync()).Name);
            Assert.Equal(1, (await context.Playlists.SingleAsync()).ChannelCount);

            var job = await context.ImportJobs.SingleAsync();
            Assert.Equal(ImportJobKind.M3U, job.Kind);
            Assert.Equal(ImportJobStatus.Completed, job.Status);
            Assert.Equal(playlist.Id, job.PlaylistId);
            Assert.Equal(profileId, job.ProfileId);
        }

        [Fact]
        public async Task AddFromFileAsync_WhenParseReturnsEmpty_DoesNotCreatePlaylistAndFailsImportJob()
        {
            var service = CreateService();

            _parserMock
                .Setup(p => p.ParseFromFileAsync("D:\\imports\\empty.m3u"))
                .ReturnsAsync(new List<Channel>());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddFromFileAsync("Empty Local Import", "D:\\imports\\empty.m3u"));

            using var context = new AppDbContext(_options);
            Assert.Equal(0, await context.Playlists.CountAsync());
            Assert.Equal(0, await context.Channels.CountAsync());

            var job = await context.ImportJobs.SingleAsync();
            Assert.Equal(ImportJobKind.M3U, job.Kind);
            Assert.Equal(ImportJobStatus.Failed, job.Status);
            Assert.Null(job.PlaylistId);
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
        public async Task RefreshAsync_WhenReplacementInsertFails_PreservesExistingChannels()
        {
            var service = CreateService();
            var initialChannels = new List<Channel>
            {
                new Channel { Name = "Existing 1", StreamUrl = "url1", Type = ChannelType.Live },
                new Channel { Name = "Existing 2", StreamUrl = "url2", Type = ChannelType.Live }
            };

            var playlist = await service.AddFromChannelsAsync("Atomic Refresh", "http://source.com/atomic.m3u", initialChannels);

            var refreshedChannels = new List<Channel>
            {
                new Channel { Name = "Replacement 1", StreamUrl = "new-url1", Type = ChannelType.Live }
            };
            _parserMock.Setup(p => p.ParseFromUrlAsync(playlist.Url)).ReturnsAsync(refreshedChannels);

            using (var setupContext = new AppDbContext(_options))
            {
                await setupContext.Database.ExecuteSqlRawAsync("""
                    CREATE TRIGGER FailChannelInsertDuringRefresh
                    BEFORE INSERT ON Channels
                    WHEN NEW.Name = 'Replacement 1'
                    BEGIN
                        SELECT RAISE(ABORT, 'simulated replacement insert failure');
                    END;
                    """);
            }

            await Assert.ThrowsAsync<SqliteException>(() => service.RefreshAsync(playlist.Id));

            using var context = new AppDbContext(_options);
            var remainingChannels = await context.Channels
                .Where(c => c.PlaylistId == playlist.Id)
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync();

            Assert.Equal(new[] { "Existing 1", "Existing 2" }, remainingChannels);
            var persistedPlaylist = await context.Playlists.SingleAsync(p => p.Id == playlist.Id);
            Assert.Equal(2, persistedPlaylist.ChannelCount);
        }

        [Fact]
        public async Task RefreshAsync_WithChildProfile_AddsAllParsedChannelsLikeNormalProfile()
        {
            // Çocuk profili özelliği kaldırıldı (Faz 1) — eski çocuk profillerde
            // de filtre uygulanmaz; yeni içerikler normal profil gibi eklenir.
            var service = CreateService();

            int profileId;
            using (var context = new AppDbContext(_options))
            {
                var account = new ProviderAccount { Name = "Child Account", Url = "http://child.test" };
                context.ProviderAccounts.Add(account);
                await context.SaveChangesAsync();

                var profile = new Profile { Name = "Child", IsChild = true, ProviderAccountId = account.Id };
                context.Profiles.Add(profile);
                await context.SaveChangesAsync();
                profileId = profile.Id;
            }

            var playlist = await service.AddFromChannelsAsync(
                "Child Safe Playlist",
                "http://source.com/child-safe.m3u",
                new List<Channel>
                {
                    new Channel { Name = "Kid Show", GroupTitle = "Kids", StreamUrl = "kid-url", Type = ChannelType.Live }
                },
                profileId);

            _parserMock
                .Setup(p => p.ParseFromUrlAsync(playlist.Url))
                .ReturnsAsync(new List<Channel>
                {
                    new Channel { Name = "Adult Only", GroupTitle = "Adult", StreamUrl = "adult-url", Type = ChannelType.Live }
                });

            await service.RefreshAsync(playlist.Id);

            using var verifyContext = new AppDbContext(_options);
            var remainingChannels = await verifyContext.Channels
                .Where(c => c.PlaylistId == playlist.Id)
                .Select(c => c.Name)
                .ToListAsync();

            var persistedPlaylist = await verifyContext.Playlists.SingleAsync(p => p.Id == playlist.Id);
            // Refresh, kanalları yeniden çekilen setle değiştirir (filtre yok) —
            // eski çocuk filtresi "Adult Only"yi atıp eski seti korurdu.
            Assert.Equal(new[] { "Adult Only" }, remainingChannels);
            Assert.Equal(1, persistedPlaylist.ChannelCount);
        }

        [Fact]
        public async Task RefreshAsync_WhenOrganizerRemovesAllParsedChannels_PreservesExistingChannels()
        {
            var service = CreateService();
            var initialChannels = new List<Channel>
            {
                new Channel { Name = "Existing Safe", StreamUrl = "existing-url", Type = ChannelType.Live }
            };

            var playlist = await service.AddFromChannelsAsync(
                "Organizer Empty Playlist",
                "http://source.com/organizer-empty.m3u",
                initialChannels);

            var parsedChannels = new List<Channel>
            {
                new Channel { Name = "Parsed But Removed", StreamUrl = "parsed-url", Type = ChannelType.Live }
            };

            _parserMock.Setup(p => p.ParseFromUrlAsync(playlist.Url)).ReturnsAsync(parsedChannels);
            _organizerMock
                .Setup(o => o.Organize(It.Is<List<Channel>>(channels => channels == parsedChannels), It.IsAny<bool>()))
                .Returns(new List<Channel>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync(playlist.Id));

            using var verifyContext = new AppDbContext(_options);
            var remainingChannels = await verifyContext.Channels
                .Where(c => c.PlaylistId == playlist.Id)
                .Select(c => c.Name)
                .ToListAsync();

            var persistedPlaylist = await verifyContext.Playlists.SingleAsync(p => p.Id == playlist.Id);
            Assert.Equal(new[] { "Existing Safe" }, remainingChannels);
            Assert.Equal(1, persistedPlaylist.ChannelCount);
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
        public async Task RefreshAsync_AfterSuccessfulParse_CompletesImportJob()
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Job Tracked Refresh",
                "http://source.com/job-refresh.m3u",
                new List<Channel>
                {
                    new Channel { Name = "Old Channel", StreamUrl = "old-url", Type = ChannelType.Live }
                });

            _parserMock
                .Setup(p => p.ParseFromUrlAsync(playlist.Url))
                .ReturnsAsync(new List<Channel>
                {
                    new Channel { Name = "New Live", StreamUrl = "live-url", Type = ChannelType.Live },
                    new Channel { Name = "New Movie", StreamUrl = "vod-url", Type = ChannelType.VOD }
                });

            await service.RefreshAsync(playlist.Id);

            using var context = new AppDbContext(_options);
            var job = await context.ImportJobs.SingleAsync(j => j.PlaylistId == playlist.Id);

            Assert.Equal(ImportJobKind.PlaylistRefresh, job.Kind);
            Assert.Equal(ImportJobStatus.Completed, job.Status);
            Assert.Equal("Completed", job.Stage);
            Assert.Equal(1, job.LiveCount);
            Assert.Equal(1, job.VodCount);
            Assert.Equal(0, job.SeriesCount);
            Assert.NotNull(job.CompletedAt);
            Assert.Null(job.ErrorMessage);
        }

        [Fact]
        public async Task RefreshAsync_WhenReplacementInsertFails_FailsImportJob()
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Job Failed Refresh",
                "http://source.com/job-fail.m3u",
                new List<Channel>
                {
                    new Channel { Name = "Old Channel", StreamUrl = "old-url", Type = ChannelType.Live }
                });

            _parserMock
                .Setup(p => p.ParseFromUrlAsync(playlist.Url))
                .ReturnsAsync(new List<Channel>
                {
                    new Channel { Name = "Replacement 1", StreamUrl = "replacement-url", Type = ChannelType.Live }
                });

            using (var setupContext = new AppDbContext(_options))
            {
                await setupContext.Database.ExecuteSqlRawAsync("""
                    CREATE TRIGGER FailTrackedRefreshInsert
                    BEFORE INSERT ON Channels
                    WHEN NEW.Name = 'Replacement 1'
                    BEGIN
                        SELECT RAISE(ABORT, 'simulated tracked refresh failure');
                    END;
                    """);
            }

            await Assert.ThrowsAsync<SqliteException>(() => service.RefreshAsync(playlist.Id));

            using var context = new AppDbContext(_options);
            var job = await context.ImportJobs.SingleAsync(j => j.PlaylistId == playlist.Id);

            Assert.Equal(ImportJobKind.PlaylistRefresh, job.Kind);
            Assert.Equal(ImportJobStatus.Failed, job.Status);
            Assert.Equal("Failed", job.Stage);
            Assert.Contains("simulated tracked refresh failure", job.ErrorMessage);
            Assert.NotNull(job.CompletedAt);
        }

        [Fact]
        public async Task RefreshAsync_AfterSuccessfulParse_RemovesStaleRefreshStagingArtifacts()
        {
            var service = CreateService();
            int playlistId;
            int staleStagingPlaylistId;

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
                    Type = ChannelType.Live
                });

                var staleStagingPlaylist = new Playlist
                {
                    Name = "Refresh Source refresh staging",
                    Url = "http://source.com/list.m3u",
                    IsActive = false,
                    ChannelCount = 1,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                    LastUpdated = DateTime.UtcNow.AddMinutes(-10)
                };
                context.Playlists.Add(staleStagingPlaylist);
                await context.SaveChangesAsync();
                staleStagingPlaylistId = staleStagingPlaylist.Id;

                context.Channels.Add(new Channel
                {
                    PlaylistId = staleStagingPlaylistId,
                    Name = "Stale Staged Channel",
                    StreamUrl = "stale-url",
                    Type = ChannelType.Live
                });

                await context.SaveChangesAsync();
            }

            _parserMock
                .Setup(p => p.ParseFromUrlAsync("http://source.com/list.m3u"))
                .ReturnsAsync(new List<Channel>
                {
                    new Channel { Name = "New Channel", StreamUrl = "new-url", Type = ChannelType.Live }
                });

            await service.RefreshAsync(playlistId);

            using (var context = new AppDbContext(_options))
            {
                Assert.False(await context.Playlists.AnyAsync(p => p.Id == staleStagingPlaylistId));
                Assert.False(await context.Channels.AnyAsync(c => c.PlaylistId == staleStagingPlaylistId));

                var channel = Assert.Single(await context.Channels.Where(c => c.PlaylistId == playlistId).ToListAsync());
                Assert.Equal("New Channel", channel.Name);
            }
        }

        [Fact]
        public async Task RefreshStagingCommit_ReplacesActivePlaylistOnlyOnCommitAndPreservesUserData()
        {
            var service = CreateService();
            var lastWatched = DateTime.UtcNow.AddHours(-2);

            var playlist = await service.AddFromChannelsAsync(
                "Provider Refresh",
                "provider://refresh",
                new List<Channel>
                {
                    new Channel
                    {
                        Name = "Channel A",
                        StreamUrl = "stream-a",
                        GroupTitle = "Live",
                        TvgId = "channel-a",
                        Type = ChannelType.Live,
                        IsFavorite = true,
                        IsInMyList = true,
                        WatchedPosition = TimeSpan.FromMinutes(10),
                        Duration = TimeSpan.FromMinutes(60),
                        LastWatched = lastWatched
                    },
                    new Channel
                    {
                        Name = "Removed B",
                        StreamUrl = "stream-b",
                        GroupTitle = "Live",
                        Type = ChannelType.Live
                    }
                });

            var staging = await service.CreateRefreshStagingPlaylistAsync(playlist.Id);

            await service.AppendChannelsAsync(
                staging.Id,
                new List<Channel>
                {
                    new Channel
                    {
                        Name = "Channel A",
                        StreamUrl = "stream-a",
                        GroupTitle = "Live",
                        TvgId = "channel-a",
                        Type = ChannelType.Live
                    },
                    new Channel
                    {
                        Name = "Channel C",
                        StreamUrl = "stream-c",
                        GroupTitle = "Live",
                        Type = ChannelType.Live
                    }
                });

            using (var beforeCommit = new AppDbContext(_options))
            {
                var activeNames = await beforeCommit.Channels
                    .Where(c => c.PlaylistId == playlist.Id)
                    .OrderBy(c => c.Name)
                    .Select(c => c.Name)
                    .ToListAsync();

                var stagedNames = await beforeCommit.Channels
                    .Where(c => c.PlaylistId == staging.Id)
                    .OrderBy(c => c.Name)
                    .Select(c => c.Name)
                    .ToListAsync();

                Assert.Equal(new[] { "Channel A", "Removed B" }, activeNames);
                Assert.Equal(new[] { "Channel A", "Channel C" }, stagedNames);
            }

            await service.CommitRefreshStagingPlaylistAsync(playlist.Id, staging.Id);

            using (var afterCommit = new AppDbContext(_options))
            {
                Assert.False(await afterCommit.Playlists.AnyAsync(p => p.Id == staging.Id));
                Assert.False(await afterCommit.Channels.AnyAsync(c => c.PlaylistId == staging.Id));

                var activeChannels = await afterCommit.Channels
                    .Where(c => c.PlaylistId == playlist.Id)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                Assert.Equal(new[] { "Channel A", "Channel C" }, activeChannels.Select(c => c.Name).ToArray());

                var preserved = activeChannels.Single(c => c.Name == "Channel A");
                Assert.True(preserved.IsFavorite);
                Assert.True(preserved.IsInMyList);
                Assert.Equal(TimeSpan.FromMinutes(10), preserved.WatchedPosition);
                Assert.Equal(TimeSpan.FromMinutes(60), preserved.Duration);
                Assert.Equal(lastWatched.ToUniversalTime(), preserved.LastWatched?.ToUniversalTime());

                var persistedPlaylist = await afterCommit.Playlists.SingleAsync(p => p.Id == playlist.Id);
                Assert.Equal(2, persistedPlaylist.ChannelCount);
            }
        }

        [Fact]
        public async Task RefreshStagingCommit_EmptyStaging_DoesNotReplaceActivePlaylist()
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Provider Refresh",
                "provider://empty-refresh",
                new List<Channel>
                {
                    new Channel
                    {
                        Name = "Existing Channel",
                        StreamUrl = "stream-existing",
                        GroupTitle = "Live",
                        Type = ChannelType.Live
                    }
                });

            var staging = await service.CreateRefreshStagingPlaylistAsync(playlist.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CommitRefreshStagingPlaylistAsync(playlist.Id, staging.Id));

            using var context = new AppDbContext(_options);
            var activeChannels = await context.Channels
                .Where(c => c.PlaylistId == playlist.Id)
                .ToListAsync();

            var existing = Assert.Single(activeChannels);
            Assert.Equal("Existing Channel", existing.Name);

            var persistedPlaylist = await context.Playlists.SingleAsync(p => p.Id == playlist.Id);
            Assert.True(persistedPlaylist.IsActive);
            Assert.Equal(1, persistedPlaylist.ChannelCount);
            Assert.True(await context.Playlists.AnyAsync(p => p.Id == staging.Id && !p.IsActive));
        }

        [Fact]
        public async Task RefreshStagingCommit_DummyOnlyStaging_DoesNotReplaceActivePlaylist()
        {
            var service = CreateService();
            var playlist = await service.AddFromChannelsAsync(
                "Provider Refresh",
                "provider://dummy-refresh",
                new List<Channel>
                {
                    new Channel
                    {
                        Name = "Existing Channel",
                        StreamUrl = "stream-existing",
                        GroupTitle = "Live",
                        Type = ChannelType.Live
                    }
                });

            var staging = await service.CreateRefreshStagingPlaylistAsync(playlist.Id);
            await service.AppendChannelsAsync(
                staging.Id,
                new List<Channel>
                {
                    new Channel
                    {
                        Name = "Loading",
                        StreamUrl = "xtream-dummy://news",
                        GroupTitle = "News",
                        Type = ChannelType.Live
                    }
                });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CommitRefreshStagingPlaylistAsync(playlist.Id, staging.Id));

            using var context = new AppDbContext(_options);
            var existing = Assert.Single(await context.Channels
                .Where(c => c.PlaylistId == playlist.Id)
                .ToListAsync());

            Assert.Equal("Existing Channel", existing.Name);
            Assert.Equal(1, await context.Channels.CountAsync(c => c.PlaylistId == staging.Id));
        }

        [Fact]
        public async Task CreateRefreshStagingPlaylistAsync_PreCanceledScopeCreatesNoStagingPlaylist()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://cancel-staging");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.CreateRefreshStagingPlaylistAsync(playlist.Id, cancellation.Token));

            using var context = new AppDbContext(_options);
            Assert.False(await context.Playlists.AnyAsync(item =>
                item.Id != playlist.Id && item.Name == $"{playlist.Name} refresh staging"));
        }

        [Fact]
        public async Task AppendChannelsAsync_ReplacesExistingStreamInsteadOfDuplicatingIt()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync(
                "Progressive",
                "stalker://progressive");
            var firstBatch = new List<Channel>
            {
                new()
                {
                    Name = "Original",
                    StreamUrl = "http://stream.test/1",
                    GroupTitle = "News",
                    Type = ChannelType.Live
                }
            };
            var retriedBatch = new List<Channel>
            {
                new()
                {
                    Name = "Updated",
                    StreamUrl = "http://stream.test/1",
                    GroupTitle = "News",
                    Type = ChannelType.Live
                }
            };

            await service.AppendChannelsAsync(playlist.Id, firstBatch);
            await service.AppendChannelsAsync(playlist.Id, retriedBatch);

            using var context = new AppDbContext(_options);
            var channels = await context.Channels
                .Where(channel => channel.PlaylistId == playlist.Id)
                .ToListAsync();

            var channel = Assert.Single(channels);
            Assert.Equal("Updated", channel.Name);
        }

        [Fact]
        public async Task ReplaceDummyWithRealChannelsAsync_MaintainsExactIncrementalChannelCount()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://incremental-count");
            await service.AppendChannelsAsync(
                playlist.Id,
                new[]
                {
                    new Channel
                    {
                        Name = "Loading",
                        StreamUrl = "xtream-dummy://news",
                        GroupTitle = "News",
                        Type = ChannelType.Live
                    }
                });

            var batch = new[]
            {
                new Channel { Name = "One", StreamUrl = "http://stream/1", GroupTitle = "News", Type = ChannelType.Live },
                new Channel { Name = "Two", StreamUrl = "http://stream/2", GroupTitle = "News", Type = ChannelType.Live }
            };
            await service.ReplaceDummyWithRealChannelsAsync(playlist.Id, "News", batch);
            await service.ReplaceDummyWithRealChannelsAsync(playlist.Id, "News", batch);

            using var context = new AppDbContext(_options);
            var persisted = await context.Playlists.SingleAsync(item => item.Id == playlist.Id);
            Assert.Equal(2, persisted.ChannelCount);
            Assert.Equal(2, await context.Channels.CountAsync(channel => channel.PlaylistId == playlist.Id));
        }

        [Fact]
        public async Task ReplaceDummyWithRealChannelsAsync_KeepsRecoveryMarkerUntilCategoryCompletes()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://recovery-marker");
            await service.AppendChannelsAsync(
                playlist.Id,
                new[]
                {
                    new Channel
                    {
                        Name = "Loading",
                        StreamUrl = "xtream-dummy://news",
                        GroupTitle = "News",
                        Type = ChannelType.Live
                    }
                });

            await service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                "News",
                new[] { new Channel { Name = "One", StreamUrl = "http://stream/1", GroupTitle = "News", Type = ChannelType.Live } },
                categoryCompleted: false);

            Assert.Contains("News", await service.GetPendingDummyGroupsAsync(playlist.Id));

            await service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                "News",
                new[] { new Channel { Name = "Two", StreamUrl = "http://stream/2", GroupTitle = "News", Type = ChannelType.Live } },
                categoryCompleted: true);

            Assert.DoesNotContain("News", await service.GetPendingDummyGroupsAsync(playlist.Id));
        }

        [Fact]
        public async Task ReplaceDummyWithRealChannelsAsync_FailedInsertRollsBackDummyDeletion()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://atomic-recovery");
            await service.AppendChannelsAsync(playlist.Id, new[]
            {
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://news", GroupTitle = "News", Type = ChannelType.Live }
            });

            using (var setup = new AppDbContext(_options))
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "CREATE TRIGGER fail_channel_insert BEFORE INSERT ON Channels WHEN NEW.StreamUrl = 'http://fail' BEGIN SELECT RAISE(ABORT, 'disk full'); END;");
            }

            await Assert.ThrowsAnyAsync<Exception>(() => service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                "News",
                new[] { new Channel { Name = "Fail", StreamUrl = "http://fail", GroupTitle = "News", Type = ChannelType.Live } },
                categoryCompleted: true));

            using var context = new AppDbContext(_options);
            Assert.Equal(1, await context.Channels.CountAsync(channel =>
                channel.PlaylistId == playlist.Id && channel.StreamUrl.StartsWith("xtream-dummy://")));
            Assert.Equal(1, (await context.Playlists.SingleAsync(item => item.Id == playlist.Id)).ChannelCount);
        }

        [Fact]
        public async Task ReplaceDummyWithRealChannelsAsync_DoesNotDeleteSameNamedDifferentTypeMarker()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://same-name-types");
            await service.AppendChannelsAsync(playlist.Id, new[]
            {
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://live-action", GroupTitle = "Action", Type = ChannelType.Live },
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://vod-action", GroupTitle = "Action", Type = ChannelType.VOD }
            });

            await service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                "Action",
                new[] { new Channel { Name = "Live", StreamUrl = "http://live", GroupTitle = "Action", Type = ChannelType.Live } },
                categoryCompleted: true);

            using var context = new AppDbContext(_options);
            var remainingDummy = await context.Channels.SingleAsync(channel => channel.StreamUrl.StartsWith("xtream-dummy://"));
            Assert.Equal(ChannelType.VOD, remainingDummy.Type);
        }

        [Fact]
        public async Task ReplaceDummyWithRealChannelsAsync_DeletesOnlyExactSameNamedSameTypeMarker()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://same-name-same-type");
            await service.AppendChannelsAsync(playlist.Id, new[]
            {
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://live/10", GroupTitle = "News", Type = ChannelType.Live },
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://live/20", GroupTitle = "News", Type = ChannelType.Live }
            });

            await service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                "News",
                new[] { new Channel { Name = "One", StreamUrl = "http://live/one", GroupTitle = "News", Type = ChannelType.Live } },
                categoryCompleted: true,
                categoryMarkerStreamUrl: "xtream-dummy://live/10",
                categoryType: ChannelType.Live);

            using var context = new AppDbContext(_options);
            var remainingDummy = await context.Channels.SingleAsync(channel => channel.StreamUrl.StartsWith("xtream-dummy://"));
            Assert.Equal("xtream-dummy://live/20", remainingDummy.StreamUrl);
        }

        [Fact]
        public async Task ReplaceDummyWithRealChannelsAsync_EmptyCompletedCategoryDeletesExactMarker()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://empty-category");
            await service.AppendChannelsAsync(playlist.Id, new[]
            {
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://vod/30", GroupTitle = "Empty", Type = ChannelType.VOD }
            });

            await service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                "Empty",
                Array.Empty<Channel>(),
                categoryCompleted: true,
                categoryMarkerStreamUrl: "xtream-dummy://vod/30",
                categoryType: ChannelType.VOD);

            using var context = new AppDbContext(_options);
            Assert.False(await context.Channels.AnyAsync(channel => channel.PlaylistId == playlist.Id));
        }

        [Fact]
        public async Task DeleteAllDummiesAsync_PreCanceledScopePreservesRecoveryMarkersAndCount()
        {
            var service = CreateService();
            var playlist = await service.CreateEmptyPlaylistAsync("Xtream", "xtream://cancel-dummy-cleanup");
            await service.AppendChannelsAsync(playlist.Id, new[]
            {
                new Channel { Name = "Loading", StreamUrl = "xtream-dummy://live/40", GroupTitle = "News", Type = ChannelType.Live }
            });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.DeleteAllDummiesAsync(playlist.Id, cancellation.Token));

            using var context = new AppDbContext(_options);
            Assert.True(await context.Channels.AnyAsync(channel => channel.StreamUrl == "xtream-dummy://live/40"));
            Assert.Equal(1, (await context.Playlists.SingleAsync(item => item.Id == playlist.Id)).ChannelCount);
        }

        [Fact]
        public async Task RefreshAsync_WithChildProfile_DoesNotFilterContent()
        {
            // Çocuk profili özelliği kaldırıldı (Faz 1) — IsChild=true olsa bile
            // hiçbir içerik filtrelenmez; profil normal profil gibi çalışır.
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

            // Assert — filtre yok: her iki kanal da alınır
            using (var context = new AppDbContext(_options))
            {
                Assert.Equal(2, await context.Channels.CountAsync(c => c.PlaylistId == playlist.Id));
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
                    ChannelTypeRepairVersion = 0,
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
                    ChannelTypeRepairVersion = 0,
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
                    ChannelTypeRepairVersion = 0,
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
                    ChannelTypeRepairVersion = 0,
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
        public async Task GetChannelsFilteredAsync_CurrentRepairVersionSkipsLegacyChannelTypeRepair()
        {
            var service = CreateService();
            int playlistId;

            await using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Current M3U",
                    Url = "https://provider.test/current.m3u",
                    IsActive = true,
                    ChannelCount = 1
                };
                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;
                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "Linear Movie Group Channel",
                    GroupTitle = "Movies",
                    StreamUrl = "https://provider.test/live/master.m3u8",
                    Type = ChannelType.VOD
                });
                await context.SaveChangesAsync();
                await SetRepairVersionAsync(context, playlistId, 1);
            }

            var movies = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.VOD);

            Assert.Single(movies);
            Assert.Equal(ChannelType.VOD, movies[0].Type);
            _mediaServiceMock.Verify(
                media => media.AggregateContentAsync(playlistId, It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GetChannelsFilteredAsync_LegacyRepairPersistsCurrentVersion()
        {
            var service = CreateService();
            int playlistId;

            await using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Legacy M3U",
                    Url = "https://provider.test/legacy.m3u",
                    IsActive = true,
                    ChannelCount = 1
                };
                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;
                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "Legacy Linear Channel",
                    GroupTitle = "Movies",
                    StreamUrl = "https://provider.test/live/master.m3u8",
                    Type = ChannelType.VOD
                });
                await context.SaveChangesAsync();
                await SetRepairVersionAsync(context, playlistId, 0);
            }

            var live = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live);

            Assert.Single(live);
            await using var verificationContext = new AppDbContext(_options);
            Assert.Equal(1, await GetRepairVersionAsync(verificationContext, playlistId));
        }

        [Fact]
        public async Task GetChannelsFilteredAsync_AggregationFailureRetriesBeforeRepairVersionCompletes()
        {
            var aggregationAttempts = 0;
            _mediaServiceMock
                .Setup(media => media.AggregateContentAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    aggregationAttempts++;
                    return aggregationAttempts == 1
                        ? Task.FromException(new InvalidOperationException("aggregation interrupted"))
                        : Task.CompletedTask;
                });
            var service = CreateService();
            int playlistId;

            await using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Interrupted Legacy M3U",
                    Url = "https://provider.test/interrupted.m3u",
                    IsActive = true,
                    ChannelCount = 1
                };
                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;
                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "Legacy Linear Channel",
                    GroupTitle = "Movies",
                    StreamUrl = "https://provider.test/live/master.m3u8",
                    Type = ChannelType.VOD
                });
                await context.SaveChangesAsync();
                await SetRepairVersionAsync(context, playlistId, 0);
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live));

            await using (var failedContext = new AppDbContext(_options))
            {
                Assert.NotEqual(1, await GetRepairVersionAsync(failedContext, playlistId));
            }

            var live = await service.GetChannelsFilteredAsync(playlistId, type: ChannelType.Live);

            Assert.Single(live);
            Assert.Equal(2, aggregationAttempts);
            await using var completedContext = new AppDbContext(_options);
            Assert.Equal(1, await GetRepairVersionAsync(completedContext, playlistId));
        }

        [Fact]
        public async Task GetPendingDummyGroupsAsync_ReturnsGroupNamesAndCategoryKeys()
        {
            var service = CreateService();
            int playlistId;

            using (var context = new AppDbContext(_options))
            {
                var playlist = new Playlist
                {
                    Name = "Stalker",
                    Url = "http://provider.test/c/",
                    IsActive = true,
                    ChannelCount = 1,
                    CreatedAt = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
                playlistId = playlist.Id;

                context.Channels.Add(new Channel
                {
                    PlaylistId = playlistId,
                    Name = "Content loading",
                    GroupTitle = "2026 Ramadan",
                    StreamUrl = "stalker-dummy://1490",
                    Type = ChannelType.Series
                });
                await context.SaveChangesAsync();
            }

            var pending = await service.GetPendingDummyGroupsAsync(playlistId);

            Assert.Contains("2026 Ramadan", pending);
            Assert.Contains("1490", pending);
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
                    ChannelTypeRepairVersion = 0,
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
        private static IReadOnlyList<Channel> BuildAdultLastPagingChannels(
            ChannelSortOrder sortOrder)
        {
            var normalNames = sortOrder == ChannelSortOrder.NameDesc
                ? new[] { "Alpha Normal", "Beta Normal" }
                : new[] { "Yankee Normal", "Zulu Normal" };
            var adultNames = sortOrder == ChannelSortOrder.NameDesc
                ? new[] { "Yankee Adult", "Zulu Adult" }
                : new[] { "Alpha Adult", "Beta Adult" };

            var normal = new[]
            {
                new Channel { Name = normalNames[0], StreamUrl = $"https://provider.test/{sortOrder}/normal-1", GroupTitle = "Normal News", Type = ChannelType.VOD },
                new Channel { Name = normalNames[1], StreamUrl = $"https://provider.test/{sortOrder}/normal-2", GroupTitle = "Normal Sports", Type = ChannelType.VOD }
            };
            var adult = new[]
            {
                new Channel { Name = adultNames[0], StreamUrl = $"https://provider.test/{sortOrder}/adult-1", GroupTitle = " FOR ADULTS ", Type = ChannelType.VOD },
                new Channel { Name = adultNames[1], StreamUrl = $"https://provider.test/{sortOrder}/adult-2", GroupTitle = "para adultos", Type = ChannelType.VOD }
            };

            return sortOrder == ChannelSortOrder.OldestFirst
                ? [.. adult, .. normal]
                : [.. normal, .. adult];
        }

        private static IReadOnlyList<string> BuildExpectedAdultLastNames(
            IReadOnlyList<Channel> channels,
            ChannelSortOrder sortOrder)
        {
            var indexed = channels
                .Select((channel, index) => (Channel: channel, Index: index))
                .ToArray();

            static IEnumerable<(Channel Channel, int Index)> SortSegment(
                IEnumerable<(Channel Channel, int Index)> segment,
                ChannelSortOrder order)
                => order switch
                {
                    ChannelSortOrder.OldestFirst => segment.OrderBy(item => item.Index),
                    ChannelSortOrder.NameAsc => segment.OrderBy(item => item.Channel.Name).ThenBy(item => item.Index),
                    ChannelSortOrder.NameDesc => segment.OrderByDescending(item => item.Channel.Name).ThenByDescending(item => item.Index),
                    _ => segment.OrderByDescending(item => item.Index)
                };

            var normal = SortSegment(
                indexed.Where(item => item.Channel.GroupTitle!.StartsWith("Normal", StringComparison.Ordinal)),
                sortOrder);
            var adult = SortSegment(
                indexed.Where(item => AdultCategoryClassifier.IsAdultCategory(item.Channel.GroupTitle)),
                sortOrder);
            return normal.Concat(adult).Select(item => item.Channel.Name).ToArray();
        }

        private PlaylistService CreateService(IImportJobService? importJobService = null)
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
                _localizationServiceMock.Object,
                importJobService ?? new ImportJobService(_contextFactory)
            );
        }

        private static async Task SetRepairVersionAsync(
            AppDbContext context,
            int playlistId,
            int version)
        {
            if (!await HasRepairVersionColumnAsync(context))
            {
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE Playlists ADD COLUMN ChannelTypeRepairVersion INTEGER NOT NULL DEFAULT 0;");
            }

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Playlists SET ChannelTypeRepairVersion = {version} WHERE Id = {playlistId};");
        }

        private static async Task<int> GetRepairVersionAsync(AppDbContext context, int playlistId)
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ChannelTypeRepairVersion FROM Playlists WHERE Id = $playlistId;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$playlistId";
            parameter.Value = playlistId;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static async Task<bool> HasRepairVersionColumnAsync(AppDbContext context)
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT 1 FROM pragma_table_info('Playlists') WHERE name = 'ChannelTypeRepairVersion' LIMIT 1;";
            return await command.ExecuteScalarAsync() is not null;
        }

        private static async IAsyncEnumerable<Channel> StreamChannelsThenFail()
        {
            yield return new Channel
            {
                Name = "Partial Channel",
                StreamUrl = "partial-url",
                Type = ChannelType.Live
            };

            await Task.Yield();
            throw new IOException("stream interrupted");
        }

        private static async IAsyncEnumerable<Channel> StreamChannels(int count)
        {
            for (var index = 0; index < count; index++)
            {
                yield return new Channel
                {
                    Name = $"Channel {index}",
                    StreamUrl = $"stream-{index}",
                    Type = ChannelType.Live
                };
            }

            await Task.CompletedTask;
        }
    }
}
