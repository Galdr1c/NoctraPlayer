using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

public sealed class DownloadContentKeyTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public DownloadContentKeyTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"Noctra-DownloadKeyTests-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        using (var context = new AppDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _contextFactory = new SimpleDbContextFactory(_options);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_databasePath);
        }
        catch
        {
        }
    }

    // ─── BuildContentKey ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildContentKey_SeriesEpisode_PrefersEpisodeId()
    {
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://u",
            null, 7, 0, 42, null, null, 7, "The 100", 2, 5, "Hakeldama");

        Assert.Equal("series:7:42", ContentDownloadService.BuildContentKey(request));
    }

    [Fact]
    public void BuildContentKey_SeriesEpisode_FallsBackToNormalizedTitleKey()
    {
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://u",
            null, 7, 0, 0, null, null, 7, "The 100", 2, 5, "Hakeldama");

        Assert.Equal("series:7:the 100:2:5", ContentDownloadService.BuildContentKey(request));
    }

    [Fact]
    public void BuildContentKey_SeriesEpisode_WithoutIdentity_ReturnsNull()
    {
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://u",
            null, 7, 0, 0, null, null, 7, null, 0, 5, null);

        Assert.Null(ContentDownloadService.BuildContentKey(request));
    }

    [Fact]
    public void BuildContentKey_Movie_UsesChannelId()
    {
        var request = new DownloadContentRequest(
            1, DownloadItemType.Vod, "Movie", "http://u",
            null, 7, 99, 0);

        Assert.Equal("movie:7:99", ContentDownloadService.BuildContentKey(request));
    }

    [Fact]
    public void BuildContentKey_MissingPlaylist_ReturnsNull()
    {
        var request = new DownloadContentRequest(
            1, DownloadItemType.Vod, "Movie", "http://u",
            null, 0, 99, 0);

        Assert.Null(ContentDownloadService.BuildContentKey(request));
    }

    // ─── Queue-time dedup ────────────────────────────────────────────────────────

    [Fact]
    public async Task QueueDownload_SameEpisode_ChangedTokenUrl_Dedupes()
    {
        var service = CreateService();
        var first = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/video.mp4?token=abc",
            null, 7, 0, 42, null, null, 7, "The 100", 2, 5, "Hakeldama");
        var second = first with { SourceUrl = "http://192.0.2.1/video.mp4?token=xyz" };

        var result1 = await service.QueueDownloadAsync(first);
        var result2 = await service.QueueDownloadAsync(second);

        Assert.True(result1.Success);
        Assert.True(result2.AlreadyExists);
        Assert.Equal(result1.DownloadId, result2.DownloadId);
    }

    [Fact]
    public async Task QueueDownload_SameMovie_DifferentCdnUrl_Dedupes()
    {
        var service = CreateService();
        var first = new DownloadContentRequest(
            1, DownloadItemType.Vod, "Movie", "http://192.0.2.1/cdn1/movie.mp4",
            null, 7, 99, 0);
        var second = first with { SourceUrl = "http://198.51.100.7/cdn2/movie.mp4" };

        var result1 = await service.QueueDownloadAsync(first);
        var result2 = await service.QueueDownloadAsync(second);

        Assert.True(result1.Success);
        Assert.True(result2.AlreadyExists);
    }

    [Fact]
    public async Task QueueDownload_SameEpisode_FallbackKey_MatchesNormalizedTitle()
    {
        var service = CreateService();
        var first = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/u/1.mp4",
            null, 7, 0, 0, null, null, 7, "The 100", 2, 5, "Hakeldama");
        var second = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/u/2.mp4",
            null, 7, 0, 0, null, null, 7, "THE 100", 2, 5, "Hakeldama");

        var result1 = await service.QueueDownloadAsync(first);
        var result2 = await service.QueueDownloadAsync(second);

        Assert.True(result1.Success);
        Assert.True(result2.AlreadyExists);
    }

    [Fact]
    public async Task QueueDownload_PersistsContentKeyOnItem()
    {
        var service = CreateService();
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/u/1.mp4",
            null, 7, 0, 42, null, null, 7, "The 100", 2, 5, "Hakeldama");

        await service.QueueDownloadAsync(request);

        using var db = _contextFactory.CreateDbContext();
        var item = await db.DownloadItems.AsNoTracking().SingleAsync();
        Assert.Equal("series:7:42", item.ContentKey);
    }

    [Fact]
    public async Task QueueDownload_SameEpisode_LegacyNullKeyRow_DedupesByEpisodeId()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = null,
                DisplayName = "Episode 5",
                SourceUrl = "http://192.0.2.1/old.mp4",
                Status = DownloadStatus.Completed
            });
            db.SaveChanges();
        }

        var service = CreateService();
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/new.mp4?token=abc",
            null, 7, 0, 42, null, null, 7, "The 100", 2, 5, "Hakeldama");

        var result = await service.QueueDownloadAsync(request);

        Assert.True(result.AlreadyExists);
    }

    [Fact]
    public async Task QueueDownload_SameMovie_LegacyNullKeyRow_DedupesByUrl()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                ChannelId = 99,
                ContentKey = null,
                DisplayName = "Movie",
                SourceUrl = "http://192.0.2.1/movie.mp4",
                Status = DownloadStatus.Completed
            });
            db.SaveChanges();
        }

        var service = CreateService();
        var request = new DownloadContentRequest(
            1, DownloadItemType.Vod, "Movie", "http://192.0.2.1/movie.mp4",
            null, 7, 99, 0);

        var result = await service.QueueDownloadAsync(request);

        Assert.True(result.AlreadyExists);
    }

    [Fact]
    public async Task QueueDownload_SameEpisode_DifferentPlaylist_LegacyRow_DoesNotDedupe()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = null,
                DisplayName = "Episode 5",
                SourceUrl = "http://192.0.2.1/old.mp4",
                Status = DownloadStatus.Completed
            });
            db.SaveChanges();
        }

        var service = CreateService();
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/new.mp4?token=abc",
            null, 8, 0, 42, null, null, 7, "The 100", 2, 5, "Hakeldama");

        var result = await service.QueueDownloadAsync(request);

        Assert.True(result.Success);
        Assert.False(result.AlreadyExists);
    }

    [Fact]
    public async Task QueueDownload_SameUrl_DifferentPlaylist_LegacyRow_DoesNotDedupe()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                ChannelId = 99,
                ContentKey = null,
                DisplayName = "Movie",
                SourceUrl = "http://192.0.2.1/movie.mp4",
                Status = DownloadStatus.Completed
            });
            db.SaveChanges();
        }

        var service = CreateService();
        var request = new DownloadContentRequest(
            1, DownloadItemType.Vod, "Movie", "http://192.0.2.1/movie.mp4",
            null, 8, 99, 0);

        var result = await service.QueueDownloadAsync(request);

        Assert.True(result.Success);
        Assert.False(result.AlreadyExists);
    }

    [Fact]
    public async Task QueueDownload_FailedEpisode_CanBeRedownloaded()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = "series:7:42",
                DisplayName = "Episode 5",
                SourceUrl = "http://192.0.2.1/old.mp4",
                Status = DownloadStatus.Failed
            });
            db.SaveChanges();
        }

        var service = CreateService();
        var request = new DownloadContentRequest(
            1, DownloadItemType.SeriesEpisode, "Episode 5", "http://192.0.2.1/new.mp4",
            null, 7, 0, 42, null, null, 7, "The 100", 2, 5, "Hakeldama");

        var result = await service.QueueDownloadAsync(request);

        Assert.True(result.Success);
    }

    // ─── Unique index (race prevention) ──────────────────────────────────────────

    [Fact]
    public async Task UniqueIndex_RejectsSecondInsert_SameContentKey()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = "series:7:42",
                DisplayName = "Episode 5",
                SourceUrl = "http://a.example/v.mp4",
                Status = DownloadStatus.Queued
            });
            db.SaveChanges();
        }

        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = "series:7:42",
                DisplayName = "Episode 5",
                SourceUrl = "http://b.example/v.mp4",
                Status = DownloadStatus.Queued
            });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(ContentDownloadService.IsUniqueConstraintViolation(ex));
        }
    }

    [Fact]
    public async Task UniqueIndex_AllowsFailedRow_ToBeReplaced()
    {
        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = "series:7:42",
                DisplayName = "Episode 5",
                SourceUrl = "http://old.example/v.mp4",
                Status = DownloadStatus.Failed
            });
            db.SaveChanges();
        }

        using (var db = _contextFactory.CreateDbContext())
        {
            db.DownloadItems.Add(new DownloadItem
            {
                ProfileId = 1,
                PlaylistId = 7,
                EpisodeId = 42,
                ContentKey = "series:7:42",
                DisplayName = "Episode 5",
                SourceUrl = "http://new.example/v.mp4",
                Status = DownloadStatus.Queued
            });

            await db.SaveChangesAsync();

            Assert.Equal(2, await db.DownloadItems.CountAsync());
        }
    }

    private ContentDownloadService CreateService()
    {
        var settings = new FakeSettingsService();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"Noctra-DownloadKeyDownloads-{Guid.NewGuid():N}");
        var paths = new DesktopAppPathService(tempRoot, Path.GetTempPath());

        // These tests assert queue-time dedupe only, so the HTTP request is
        // left pending forever: the queue worker then keeps the first item in
        // "Downloading" instead of racing it to "Failed" mid-test.
        return new ContentDownloadService(
            settings,
            _contextFactory,
            new HttpClient(new NeverRespondingHandler()),
            new LocalizationService(),
            null,
            paths,
            new FakeNetworkService());
    }

    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }
    }
}
