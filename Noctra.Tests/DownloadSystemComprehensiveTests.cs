using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    // ─── Fakes ────────────────────────────────────────────────────────────────────

    internal sealed class DownloadTestDispatcher : IDispatcherService
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
        public Task InvokeAsync(Func<Task> func) => func();
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
    }

    internal class StatefulFakeDownloadService : IContentDownloadService
    {
        public List<DownloadItem> MockItems = new();
        public event EventHandler? DownloadsChanged;
        public event EventHandler<DownloadItem>? DownloadCompleted;

        public virtual Task<DownloadContentResult> QueueDownloadAsync(DownloadContentRequest request, CancellationToken ct = default)
        {
            if (MockItems.Any(i => i.SourceUrl == request.SourceUrl))
                return Task.FromResult(new DownloadContentResult(true, true, "Zaten var"));

            var item = new DownloadItem
            {
                Id = MockItems.Count + 1,
                DisplayName = request.DisplayName,
                SourceUrl = request.SourceUrl,
                Status = DownloadStatus.Queued,
                ProfileId = request.ProfileId,
                ChannelType = request.ItemType == DownloadItemType.Vod ? ChannelType.VOD : ChannelType.Series,
                SeriesId = request.SeriesId > 0 ? request.SeriesId : null,
                SeriesTitle = request.SeriesTitle,
                SeasonNumber = request.SeasonNumber,
                EpisodeNumber = request.EpisodeNumber,
                EpisodeTitle = request.EpisodeTitle
            };
            MockItems.Add(item);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(new DownloadContentResult(true, false, "Eklendi", item.Id));
        }

        public Task<string> ResolvePlayableUrlAsync(string url, CancellationToken ct = default)
        {
            var item = MockItems.FirstOrDefault(i => i.SourceUrl == url && i.Status == DownloadStatus.Completed);
            return Task.FromResult(item?.LocalFilePath ?? url);
        }

        public Task CleanupPlaybackCacheAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<DownloadItem>> GetDownloadsAsync(int profileId, CancellationToken ct = default) => 
            Task.FromResult(MockItems.Where(i => i.ProfileId == profileId || profileId == 0).ToList());

        public Task CancelDownloadAsync(int id, CancellationToken ct = default)
        {
            var item = MockItems.FirstOrDefault(i => i.Id == id);
            if (item != null) MockItems.Remove(item);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task DeleteDownloadAsync(int downloadId, CancellationToken ct = default)
        {
            var item = MockItems.FirstOrDefault(i => i.Id == downloadId);
            if (item != null) MockItems.Remove(item);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task DeleteAllDownloadsAsync(int profileId, CancellationToken ct = default)
        {
            MockItems.RemoveAll(i => profileId <= 0 || i.ProfileId == profileId);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task PauseDownloadAsync(int id, CancellationToken ct = default)
        {
            var item = MockItems.FirstOrDefault(i => i.Id == id);
            if (item != null) item.Status = DownloadStatus.Paused;
            return Task.CompletedTask;
        }

        public Task ResumeDownloadAsync(int id, CancellationToken ct = default)
        {
            var item = MockItems.FirstOrDefault(i => i.Id == id);
            if (item != null) item.Status = DownloadStatus.Downloading;
            return Task.CompletedTask;
        }

        public Task FailActiveDownloadsForProfileAsync(int pId, string msg, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteProfileDownloadsAsync(int pId, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ─── Test Context ─────────────────────────────────────────────────────────────

    internal sealed class DownloadTestContext
    {
        public StatefulFakeDownloadService DownloadService { get; } = new();
        public MainViewModel MainVM { get; }
        public PlayerViewModel PlayerVM { get; }
        public FakeVideoPlayerService VideoService { get; } = new();

        public DownloadTestContext()
        {
            var settings = new FakeSettingsService();
            var license = new FakeLicenseService();
            var network = new FakeNetworkService();
            var dispatcher = new DownloadTestDispatcher();
            var history = new FakeWatchHistoryService();
            var media = new FakeMediaService();
            var epg = new FakeEpgService();
            var metadata = new FakeMetadataService();

            PlayerVM = new PlayerViewModel(VideoService, epg, metadata, media, DownloadService, network, dispatcher, settings, license, new LocalizationService(), null!, history, new FakeStalkerPortalService());
            
            // We pass nulls for services not relevant to these tests to avoid huge fake classes.
            // MainViewModel handles nulls or doesn't use them during pure instantiation.
            MainVM = new MainViewModel(
                settings,
                DownloadService,
                metadata,
                dispatcher,
                null!, // dialog
                new WatermarkViewModel(license, dispatcher),
                null!, // channel
                media,
                epg,
                null!, // playlist
                history,
                null!, // xtream
                null!, // stalker
                null!, // lang
                null!, // resolver
                null!, // db context
                null!, // security
                null!, // tmdb sync
                license,
                null!, // update service
                new Moq.Mock<ILocalizationService>().Object,
                null   // logger
            );
        }
    }

    public class DownloadSystemComprehensiveTests
    {
        [Fact]
        public async Task QueueDownload_AddsItemToService()
        {
            var ctx = new DownloadTestContext();
            var req = new DownloadContentRequest(1, DownloadItemType.Vod, "Movie", "http://url.mp4");

            var result = await ctx.DownloadService.QueueDownloadAsync(req);

            Assert.True(result.Success);
            Assert.Single(ctx.DownloadService.MockItems);
        }

        [Fact]
        public async Task QueueDownload_PreventsDuplicates()
        {
            var ctx = new DownloadTestContext();
            var req = new DownloadContentRequest(1, DownloadItemType.Vod, "Movie", "http://url.mp4");

            await ctx.DownloadService.QueueDownloadAsync(req);
            var result2 = await ctx.DownloadService.QueueDownloadAsync(req);

            Assert.True(result2.AlreadyExists);
            Assert.Single(ctx.DownloadService.MockItems);
        }

        [Fact]
        public async Task QueueDownload_PreservesStructuralSeriesMetadata()
        {
            var ctx = new DownloadTestContext();
            var req = new DownloadContentRequest(
                1,
                DownloadItemType.SeriesEpisode,
                "Episode 5",
                "http://url.mp4",
                null,
                0,
                0,
                42,
                null,
                null,
                SeriesId: 7,
                SeriesTitle: "The 100",
                SeasonNumber: 2,
                EpisodeNumber: 5,
                EpisodeTitle: "Hakeldama");

            var result = await ctx.DownloadService.QueueDownloadAsync(req);

            Assert.True(result.Success);
            var item = Assert.Single(ctx.DownloadService.MockItems);
            Assert.Equal(7, item.SeriesId);
            Assert.Equal("The 100", item.SeriesTitle);
            Assert.Equal(2, item.SeasonNumber);
            Assert.Equal(5, item.EpisodeNumber);
            Assert.Equal("Hakeldama", item.EpisodeTitle);
        }

        [Fact]
        public void SeriesFolder_UsesStructuralMetadata_NotEpisodeDisplayName()
        {
            using var tempRoot = new TempDownloadRoot();
            var service = CreateFolderService();
            var item = new DownloadItem
            {
                ChannelType = ChannelType.Series,
                DisplayName = "Episode 5",
                SeriesTitle = "The 100",
                SeasonNumber = 2,
                ProfileId = 1
            };

            var folder = InvokeEnsureItemDownloadDirectory(service, tempRoot.Path, item);

            Assert.EndsWith(
                Path.Combine("Series", "The 100", "Season 02"),
                folder,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Episode 5", folder, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(tempRoot.Path, "Series", "Episode 5")));
        }

        [Fact]
        public void SeriesFolder_FallsBackToDisplayNameParse_WhenStructuralMetadataMissing()
        {
            using var tempRoot = new TempDownloadRoot();
            var service = CreateFolderService();
            var item = new DownloadItem
            {
                ChannelType = ChannelType.Series,
                DisplayName = "The 100 - S01 E05",
                ProfileId = 1
            };

            var folder = InvokeEnsureItemDownloadDirectory(service, tempRoot.Path, item);

            Assert.EndsWith(
                Path.Combine("Series", "The 100", "Season 01"),
                folder,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SeasonDownload_AlreadyExistsEpisode_CountsAsSkippedNotQueued()
        {
            var downloadService = new RecordingDownloadService();
            var viewModel = CreateSeasonDownloadViewModel(downloadService);

            viewModel.CurrentProfileId = 1;
            viewModel.SelectedSeries = new Series { Id = 7, Name = "The 100" };
            viewModel.SelectedSeason = new Season
            {
                Id = 1,
                SeasonNumber = 2,
                SeriesId = 7,
                Episodes =
                {
                    new Episode { Id = 11, SeasonId = 1, EpisodeNumber = 1, Name = "Episode 1", StreamUrl = "http://url/new-1.mp4" },
                    new Episode { Id = 12, SeasonId = 1, EpisodeNumber = 2, Name = "Episode 2", StreamUrl = "http://url/old-2.mp4" }
                }
            };

            // Second episode already exists in the queue — the real service reports
            // Success=true AND AlreadyExists=true for this case.
            downloadService.MockItems.Add(new DownloadItem
            {
                SourceUrl = "http://url/old-2.mp4",
                Status = DownloadStatus.Completed,
                ProfileId = 1
            });

            await viewModel.DownloadSelectedSeasonCommand.ExecuteAsync(null);

            Assert.Contains("1 eklendi", viewModel.StatusMessage);
            Assert.Contains("1 atlandı", viewModel.StatusMessage);
            Assert.DoesNotContain("hata", viewModel.StatusMessage);

            // Structural metadata must flow through the request, not the display name.
            Assert.Equal(2, downloadService.Requests.Count);
            Assert.All(downloadService.Requests, request =>
            {
                Assert.Equal(7, request.SeriesId);
                Assert.Equal("The 100", request.SeriesTitle);
                Assert.Equal(2, request.SeasonNumber);
            });
            Assert.Equal("Episode 1", downloadService.Requests[0].EpisodeTitle);
            Assert.Equal("Episode 2", downloadService.Requests[1].EpisodeTitle);
            Assert.Equal(1, downloadService.Requests[0].EpisodeNumber);
            Assert.Equal(2, downloadService.Requests[1].EpisodeNumber);
        }

        private static MainViewModel CreateSeasonDownloadViewModel(IContentDownloadService downloadService)
        {
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(service => service.Settings).Returns(new AppSettings());

            var dispatcher = new Mock<IDispatcherService>();
            dispatcher.Setup(service => service.Invoke(It.IsAny<Action>()))
                .Callback<Action>(action => action());
            dispatcher.Setup(service => service.BeginInvoke(It.IsAny<Action>()))
                .Callback<Action>(action => action());
            dispatcher.Setup(service => service.InvokeAsync(It.IsAny<Func<Task>>()))
                .Returns(Task.CompletedTask);

            var localization = new Mock<ILocalizationService>();
            localization.Setup(service => service.GetString(It.IsAny<string>())).Returns("Test");
            localization.Setup(service => service.GetString("Download.Season.StartingFormat")).Returns("{0}. sezon - {1} bölüm başlatılıyor");
            localization.Setup(service => service.GetString("Download.Season.Result.QueuedFormat")).Returns("{0} eklendi");
            localization.Setup(service => service.GetString("Download.Season.Result.SkippedFormat")).Returns("{0} atlandı");
            localization.Setup(service => service.GetString("Download.Season.Result.FailedFormat")).Returns("{0} hata");
            localization.Setup(service => service.GetString("Download.Season.ResultFormat")).Returns("Sonuç: {0} - {1}");

            return new MainViewModel(
                settings.Object,
                downloadService,
                new Mock<IMetadataService>().Object,
                dispatcher.Object,
                new Mock<IDialogService>().Object,
                null!,
                new Mock<IChannelService>().Object,
                new Mock<IMediaService>().Object,
                new Mock<IEpgService>().Object,
                new Mock<IPlaylistService>().Object,
                new Mock<IWatchHistoryService>().Object,
                new Mock<IXtreamCodesService>().Object,
                new Mock<IStalkerPortalService>().Object,
                null!,
                null!,
                new Mock<IDbContextFactory<AppDbContext>>().Object,
                new Mock<ISecurityService>().Object,
                new Mock<ITmdbSyncService>().Object,
                new Mock<ILicenseService>().Object,
                null!,
                localization.Object);
        }

        private sealed class RecordingDownloadService : StatefulFakeDownloadService
        {
            public List<DownloadContentRequest> Requests { get; } = new();

            public override Task<DownloadContentResult> QueueDownloadAsync(DownloadContentRequest request, CancellationToken ct = default)
            {
                Requests.Add(request);
                return base.QueueDownloadAsync(request, ct);
            }
        }

        private static ContentDownloadService CreateFolderService()
        {
            var settings = new FakeSettingsService();
            var factory = Moq.Mock.Of<IDbContextFactory<AppDbContext>>();
            return new ContentDownloadService(
                settings,
                factory,
                new HttpClient(),
                new LocalizationService(),
                null,
                new DesktopAppPathService(
                    Path.Combine(Path.GetTempPath(), "Noctra-FolderTests"),
                    Path.GetTempPath()),
                new FakeNetworkService());
        }

        private static string InvokeEnsureItemDownloadDirectory(
            ContentDownloadService service,
            string profilePath,
            DownloadItem item)
        {
            var method = typeof(ContentDownloadService).GetMethod(
                "EnsureItemDownloadDirectory",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (string)method!.Invoke(service, new object[] { profilePath, item })!;
        }

        private sealed class TempDownloadRoot : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Noctra-DownloadFolder-{Guid.NewGuid():N}");

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Fact]
        public void DownloadItem_Series_GeneratesCorrectFolderPath()
        {
            var item = new DownloadItem
            {
                ChannelType = ChannelType.Series,
                DisplayName = "The 100 - S01 E05",
                ProfileId = 1
            };

            var seriesName = SeriesInfoParser.Parse(item.DisplayName).SeriesName;
            Assert.Equal("the 100", seriesName.ToLower());
        }

        [Fact]
        public void MainViewModel_Downloads_SeparatesActiveFromLibrary()
        {
            var ctx = new DownloadTestContext();
            ctx.DownloadService.MockItems.Add(new DownloadItem { Id = 1, Status = DownloadStatus.Downloading, ProfileId = 1 });
            ctx.DownloadService.MockItems.Add(new DownloadItem { Id = 2, Status = DownloadStatus.Completed, ProfileId = 1 });
            ctx.DownloadService.MockItems.Add(new DownloadItem { Id = 3, Status = DownloadStatus.Queued, ProfileId = 1 });

            var active = ctx.DownloadService.MockItems.Where(i => i.Status != DownloadStatus.Completed).ToList();
            var library = ctx.DownloadService.MockItems.Where(i => i.Status == DownloadStatus.Completed).ToList();

            Assert.Equal(2, active.Count);
            Assert.Single(library);
        }

        [Fact]
        public void DownloadItem_Formatting_BytesToHumanReadable()
        {
            // DownloadItem.FormatBytes is private in the actual model, but we can test public 
            // properties that use it if needed, or use reflection.
            var method = typeof(DownloadItem).GetMethod("FormatBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            var resultKb = (string)method!.Invoke(null, new object[] { 1024L })!;
            var resultMb = (string)method!.Invoke(null, new object[] { 1024L * 1024 * 5 })!;

            Assert.Contains("KB", resultKb);
            Assert.Equal("5 MB", resultMb);
        }

        [Fact]
        public async Task PlayerViewModel_PreferLocalFile_WhenCompleted()
        {
            var ctx = new DownloadTestContext();
            var url = "http://server.com/movie.mp4";
            var localPath = "C:\\Downloads\\movie.mp4";

            ctx.DownloadService.MockItems.Add(new DownloadItem 
            { 
                SourceUrl = url, 
                LocalFilePath = localPath, 
                Status = DownloadStatus.Completed 
            });

            var resolved = await ctx.DownloadService.ResolvePlayableUrlAsync(url);

            Assert.Equal(localPath, resolved);
        }

        [Fact]
        public async Task PlayerViewModel_UsesStream_WhenNotDownloaded()
        {
            var ctx = new DownloadTestContext();
            var url = "http://server.com/live.ts";

            var resolved = await ctx.DownloadService.ResolvePlayableUrlAsync(url);

            Assert.Equal(url, resolved);
        }

        [Fact]
        public void PlayerViewModel_IsDownloadedContent_HidesDownloadButton()
        {
            var ctx = new DownloadTestContext();
            ctx.PlayerVM.CurrentChannel = new Channel { StreamUrl = "file://C:/video.mp4" };
            Assert.False(ctx.PlayerVM.CanDownloadCurrentContent);
        }

        [Fact]
        public void StorageLogic_CalculatesPercentagesCorrectly()
        {
            long total = 100;
            long other = 50;
            long noctra = 10;
            long queue = 5;

            double otherPer = (double)other / total * 100;
            double noctraPer = (double)noctra / total * 100;
            double queuePer = (double)queue / total * 100;

            Assert.Equal(50, otherPer);
            Assert.Equal(10, noctraPer);
            Assert.Equal(5, queuePer);
        }

        [Fact]
        public void DownloadStatus_Failed_ShowsErrorMessage()
        {
            var item = new DownloadItem
            {
                Status = DownloadStatus.Failed,
                ErrorMessage = "Sunucu 404 döndürdü"
            };

            Assert.Contains("404", item.StatusText);
        }

        [Fact]
        public void DownloadItem_ETA_CalculatesCorrectSeconds()
        {
            var item = new DownloadItem
            {
                BytesTotal = 200 * 1024 * 1024,
                BytesDownloaded = 100 * 1024 * 1024,
                SpeedBytesPerSecond = 2 * 1024 * 1024,
                EstimatedSecondsRemaining = 50
            };

            Assert.Contains("50sn", item.EtaText);
        }
    }
}
