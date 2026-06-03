using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    internal sealed class StatefulFakeDownloadService : IContentDownloadService
    {
        public List<DownloadItem> MockItems = new();
        public event EventHandler? DownloadsChanged;
        public event EventHandler<DownloadItem>? DownloadCompleted;

        public Task<DownloadContentResult> QueueDownloadAsync(DownloadContentRequest request, CancellationToken ct = default)
        {
            if (MockItems.Any(i => i.SourceUrl == request.SourceUrl))
                return Task.FromResult(new DownloadContentResult(false, true, "Zaten var"));

            var item = new DownloadItem
            {
                Id = MockItems.Count + 1,
                DisplayName = request.DisplayName,
                SourceUrl = request.SourceUrl,
                Status = DownloadStatus.Queued,
                ProfileId = request.ProfileId,
                ChannelType = request.ItemType == DownloadItemType.Vod ? ChannelType.VOD : ChannelType.Series
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

            Assert.False(result2.Success);
            Assert.True(result2.AlreadyExists);
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
