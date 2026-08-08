using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// HttpMessageHandler that simulates a media server with configurable range
/// behavior, manifest bodies and poster endpoints. Everything is served from
/// memory, so the tests exercise the real ContentDownloadService pipeline
/// (queue → worker → HTTP download → resume → completion → delete) without
/// any network access.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public byte[] Poster { get; set; } = Array.Empty<byte>();

    /// <summary>When true, Range requests are answered with 206 + Content-Range.</summary>
    public bool SupportRange { get; set; } = true;

    /// <summary>When true, Range requests are answered with a 200 full body.</summary>
    public bool IgnoreRange { get; set; }

    /// <summary>Optional body override (e.g. an HLS manifest served as octet-stream).</summary>
    public byte[]? OverrideBody { get; set; }

    /// <summary>Optional content type override for the media response.</summary>
    public string? OverrideContentType { get; set; }

    /// <summary>Simulated network latency before the media response is returned.</summary>
    public int InitialDelayMs { get; set; }

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public sealed record RecordedRequest(string Url, string? Range);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var range = request.Headers.Range?.ToString();
        Requests.Enqueue(new RecordedRequest(url, range));

        if (url.Contains("/posters/", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResponse(HttpStatusCode.OK, Poster, "image/jpeg", null);
        }

        if (InitialDelayMs > 0)
        {
            await Task.Delay(InitialDelayMs, cancellationToken);
        }

        var body = OverrideBody ?? Content;
        var mediaType = OverrideContentType ?? "video/mp4";

        long start;
        if (range is not null && !IgnoreRange && SupportRange &&
            TryParseRangeStart(range, out start) && start >= body.Length)
        {
            var invalid = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            invalid.Content.Headers.ContentRange = new ContentRangeHeaderValue(body.Length);
            return invalid;
        }

        if (range is not null && !IgnoreRange && SupportRange &&
            TryParseRangeStart(range, out start))
        {
            var slice = body.AsMemory((int)start).ToArray();
            var partial = BuildResponse(HttpStatusCode.PartialContent, slice, mediaType, null);
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, body.Length - 1, body.Length);
            return partial;
        }

        return BuildResponse(HttpStatusCode.OK, body, mediaType, null);
    }

    private static HttpResponseMessage BuildResponse(
        HttpStatusCode status,
        byte[] body,
        string mediaType,
        ContentRangeHeaderValue? contentRange)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        if (contentRange is not null)
        {
            response.Content.Headers.ContentRange = contentRange;
        }

        return response;
    }

    private static bool TryParseRangeStart(string? rangeHeader, out long start)
    {
        start = 0;
        if (string.IsNullOrWhiteSpace(rangeHeader))
        {
            return false;
        }

        var trimmed = rangeHeader.Trim();
        if (!trimmed.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = trimmed["bytes=".Length..];
        var dash = value.IndexOf('-');
        if (dash <= 0)
        {
            return false;
        }

        return long.TryParse(value[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out start);
    }
}

internal sealed class IntegrationSettingsService : ISettingsService
{
    public AppSettings Settings { get; }

    public event Action? SettingsChanged;

    public IntegrationSettingsService(AppSettings settings) => Settings = settings;

    public Task SaveAsync() => Task.CompletedTask;
    public Task LoadAsync() => Task.CompletedTask;
    public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
    public Task LoadProfileSettingsAsync(int profileId) => Task.CompletedTask;
    public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) => Task.FromResult<AppSettings?>(Settings);
    public void NotifySettingsChanged() { }
    public void ResetToDefaults() { }
}

internal sealed class WifiNetworkService : INetworkService
{
    public string CurrentNetworkStatus => "Wi-Fi";
    public event EventHandler<string>? NetworkStatusChanged;
}

/// <summary>
/// Isolated end-to-end harness for the real ContentDownloadService: a temp
/// SQLite database, a temp download root and an in-memory HTTP server. The
/// queue worker runs for real, so these tests verify actual production
/// behavior (resume, restore, posters, restart) instead of fake contracts.
/// </summary>
internal sealed class DownloadIntegrationContext : IDisposable
{
    public string TempRoot { get; }
    public string DownloadsRoot { get; }
    public DbContextOptions<AppDbContext> Options { get; }
    public SimpleDbContextFactory ContextFactory { get; }
    public StubHttpMessageHandler Handler { get; }
    public HttpClient HttpClient { get; }
    public AppSettings Settings { get; }
    public ContentDownloadService? Service { get; private set; }

    private int _completedCount;

    public int CompletedCount => Volatile.Read(ref _completedCount);

    public DownloadIntegrationContext(byte[]? content = null, bool createService = true)
    {
        TempRoot = Path.Combine(Path.GetTempPath(), $"Noctra-DownloadInt-{Guid.NewGuid():N}");
        DownloadsRoot = Path.Combine(TempRoot, "Downloads");
        Directory.CreateDirectory(DownloadsRoot);

        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(TempRoot, "test.db")}")
            .Options;
        using (var db = new AppDbContext(Options))
        {
            db.Database.EnsureCreated();
        }

        ContextFactory = new SimpleDbContextFactory(Options);
        Handler = new StubHttpMessageHandler
        {
            Content = content ?? BuildContent(200 * 1024),
            Poster = BuildContent(4096)
        };
        HttpClient = new HttpClient(Handler);

        Settings = new AppSettings
        {
            DownloadWifiOnly = false,
            DownloadPath = DownloadsRoot
        };

        if (createService)
        {
            Service = CreateService();
        }
    }

    public ContentDownloadService CreateService()
    {
        if (Service is not null)
        {
            return Service;
        }

        var service = new ContentDownloadService(
            new IntegrationSettingsService(Settings),
            ContextFactory,
            HttpClient,
            new LocalizationService(),
            null,
            new DesktopAppPathService(TempRoot, TempRoot),
            new WifiNetworkService());

        service.DownloadCompleted += (_, _) => Interlocked.Increment(ref _completedCount);
        Service = service;
        return service;
    }

    public static byte[] BuildContent(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    public async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return await condition();
    }

    public async Task<DownloadItem?> GetItemAsync(int id)
    {
        await using var db = ContextFactory.CreateDbContext();
        return await db.DownloadItems.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<DownloadItem>> GetItemsAsync()
    {
        await using var db = ContextFactory.CreateDbContext();
        return await db.DownloadItems.AsNoTracking().OrderBy(d => d.Id).ToListAsync();
    }

    public async Task<DownloadItem?> WaitForItemAsync(
        int id,
        Func<DownloadItem, bool>? predicate = null,
        int timeoutMs = 30000)
    {
        DownloadItem? last = null;
        var ok = await WaitUntilAsync(async () =>
        {
            last = await GetItemAsync(id);
            return last is not null && (predicate?.Invoke(last) ?? true);
        }, timeoutMs);

        return ok ? last : null;
    }

    public async Task<bool> WaitForRowGoneAsync(int id, int timeoutMs = 30000)
        => await WaitUntilAsync(async () => await GetItemAsync(id) is null, timeoutMs);

    public List<string> GetAllFiles()
        => Directory.Exists(DownloadsRoot)
            ? Directory.GetFiles(DownloadsRoot, "*", SearchOption.AllDirectories).ToList()
            : new List<string>();

    public void Dispose()
    {
        HttpClient.Dispose();
        try
        {
            Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

public sealed class ContentDownloadIntegrationTests
{
    private const string ServerRoot = "http://test/";

    // ─── Queue dedup (real service, real unique index) ───────────────────────────

    [Fact]
    public async Task QueueDownload_ConcurrentSameEpisode_ExactlyOneWins_OtherAlreadyExists()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(50_000));
        var service = ctx.CreateService();

        var request = new DownloadContentRequest(
            1,
            DownloadItemType.SeriesEpisode,
            "Episode 5",
            ServerRoot + "s1e5.mp4",
            null,
            7,
            0,
            42,
            null,
            null,
            7,
            "The 100",
            2,
            5,
            "Hakeldama");

        var results = await Task.WhenAll(
            service.QueueDownloadAsync(request),
            service.QueueDownloadAsync(request));

        Assert.All(results, r => Assert.True(r.Success));
        Assert.Single(results, r => !r.AlreadyExists);
        Assert.Single(results, r => r.AlreadyExists);
        Assert.Equal(results[0].DownloadId, results[1].DownloadId);

        var winnerId = results.First(r => !r.AlreadyExists).DownloadId!.Value;
        var completed = await ctx.WaitForItemAsync(winnerId, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);

        await using (var db = ctx.ContextFactory.CreateDbContext())
        {
            Assert.Equal(1, await db.DownloadItems.CountAsync());
        }
    }

    [Fact]
    public async Task QueueDownload_ChangedTokenSameEpisode_SuccessAlreadyExistsNotError()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(50_000));
        var service = ctx.CreateService();

        var first = new DownloadContentRequest(
            1,
            DownloadItemType.SeriesEpisode,
            "Episode 5",
            ServerRoot + "s1e5.mp4?token=abc",
            null,
            7,
            0,
            42,
            null,
            null,
            7,
            "The 100",
            2,
            5,
            "Hakeldama");
        var second = first with { SourceUrl = ServerRoot + "s1e5.mp4?token=xyz" };

        var result1 = await service.QueueDownloadAsync(first);
        var result2 = await service.QueueDownloadAsync(second);

        // Production contract (ToDuplicateResult): an existing item is reported
        // as Success=true + AlreadyExists=true — a skip, never a hard failure.
        Assert.True(result1.Success);
        Assert.False(result1.AlreadyExists);
        Assert.True(result2.Success);
        Assert.True(result2.AlreadyExists);
        Assert.Equal(result1.DownloadId, result2.DownloadId);
    }

    // ─── HTTP resume semantics ───────────────────────────────────────────────────

    [Fact]
    public async Task Resume_FailedRowWithLiveDuplicateContentKey_RemovesStaleRow()
    {
        // Device reproduction: the same episode was queued twice — one row
        // Failed (excluded from the unique ContentKey index), the other live
        // (Paused). Retrying the Failed row must NOT violate the unique index;
        // the stale row is dropped and the live one is kept.
        var content = DownloadIntegrationContext.BuildContent(50_000);
        using var ctx = new DownloadIntegrationContext(content: content);
        var service = ctx.CreateService();

        await using (var db = ctx.ContextFactory.CreateDbContext())
        {
            var live = new DownloadItem
            {
                ProfileId = 1,
                DisplayName = "episode",
                SourceUrl = ServerRoot + "episode.mp4",
                ContentKey = "series:1:64287",
                Status = DownloadStatus.Paused,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var stale = new DownloadItem
            {
                ProfileId = 1,
                DisplayName = "episode",
                SourceUrl = ServerRoot + "episode.mp4",
                ContentKey = "series:1:64287",
                Status = DownloadStatus.Failed,
                ErrorMessage = "stale failure",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.DownloadItems.AddRange(live, stale);
            await db.SaveChangesAsync();
        }

        // Retrying the Failed row must NOT violate the unique index: the stale
        // row is dropped, the live row is untouched, no exception.
        await service.ResumeDownloadAsync(2);

        var remaining = await ctx.GetItemsAsync();
        var liveRow = Assert.Single(remaining);
        Assert.Equal(DownloadStatus.Paused, liveRow.Status);
        Assert.Equal("series:1:64287", liveRow.ContentKey);
    }


    [Fact]
    public async Task Resume_PartialTempFile_Http206_AppendsAndCompletes()
    {
        var content = DownloadIntegrationContext.BuildContent(100_000);
        using var ctx = new DownloadIntegrationContext(content: content);
        var service = ctx.CreateService();

        var partPath = CreatePartFile(ctx, "movie", content[..50_000]);
        var itemId = await InsertManualItemAsync(ctx, 1, "movie", partPath, 50_000, content.Length, DownloadStatus.Paused);

        await service.ResumeDownloadAsync(itemId);

        var completed = await ctx.WaitForItemAsync(itemId, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);
        Assert.Null(completed.TempFilePath);

        var finalPath = completed.LocalFilePath!;
        Assert.True(File.Exists(finalPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(finalPath));

        // The resumed request must carry the byte offset as a Range header.
        Assert.Contains(ctx.Handler.Requests, r =>
            r.Url == ServerRoot + "movie.mp4" &&
            r.Range is not null &&
            r.Range.Contains("50000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resume_ServerResponds200ToRange_DeletesPartAndReDownloadsFully()
    {
        var content = DownloadIntegrationContext.BuildContent(100_000);
        using var ctx = new DownloadIntegrationContext(content: content);
        ctx.Handler.SupportRange = false;
        var service = ctx.CreateService();

        // Garbage .part: bytes that are NOT a prefix of the real content.
        var garbage = new byte[50_000];
        Array.Fill(garbage, (byte)0xAA);
        var partPath = CreatePartFile(ctx, "movie", garbage);
        var itemId = await InsertManualItemAsync(ctx, 1, "movie", partPath, 50_000, content.Length, DownloadStatus.Paused);

        await service.ResumeDownloadAsync(itemId);

        var completed = await ctx.WaitForItemAsync(itemId, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);
        Assert.Null(completed.TempFilePath);
        Assert.False(File.Exists(partPath));

        // A 200 response means the server ignored the Range header; the service
        // must discard the partial file and start from zero.
        var finalPath = completed.LocalFilePath!;
        Assert.True(File.Exists(finalPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(finalPath));
        Assert.Contains(ctx.Handler.Requests, r => r.Range is not null && r.Range.Contains("50000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resume_CorruptedPartLargerThanSource_Range416_PausesGracefully()
    {
        var content = DownloadIntegrationContext.BuildContent(1_000);
        using var ctx = new DownloadIntegrationContext(content: content);
        var service = ctx.CreateService();

        // A .part file longer than the actual server content: the Range request
        // is out of bounds and the server answers 416.
        var garbage = new byte[5_000];
        Array.Fill(garbage, (byte)0xBB);
        var partPath = CreatePartFile(ctx, "movie", garbage);
        var itemId = await InsertManualItemAsync(ctx, 1, "movie", partPath, 0, null, DownloadStatus.Paused);

        await service.ResumeDownloadAsync(itemId);

        var paused = await ctx.WaitForItemAsync(itemId, i => i.Status == DownloadStatus.Paused, timeoutMs: 40000);
        Assert.NotNull(paused);
        Assert.False(string.IsNullOrWhiteSpace(paused.ErrorMessage));

        // No corrupted file may ever be presented as completed.
        Assert.False(File.Exists(Path.Combine(ctx.DownloadsRoot, "Movies", "movie.mp4")));
        Assert.True(File.Exists(partPath));
        Assert.Contains(ctx.Handler.Requests, r => r.Range is not null && r.Range.Contains("5000", StringComparison.Ordinal));

        // The worker must still be functional afterwards.
        await AssertDownloadCompletesAsync(ctx, "after416", "after416.mp4");
    }

    // ─── HLS/DASH rejection end-to-end ───────────────────────────────────────────

    [Fact]
    public async Task Download_HlsManifestBody_IsRejected_NeverMarkedCompleted()
    {
        using var ctx = new DownloadIntegrationContext();
        ctx.Handler.OverrideBody = Encoding.UTF8.GetBytes(
            "#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:5.0,\nsegment0.ts\n#EXT-X-ENDLIST\n");
        ctx.Handler.OverrideContentType = "application/octet-stream";
        var service = ctx.CreateService();

        var result = await service.QueueDownloadAsync(new DownloadContentRequest(
            1, DownloadItemType.Vod, "stream", ServerRoot + "stream.mp4"));

        Assert.True(result.Success);

        // The manifest must not survive as a "completed" file: the row is
        // removed and every artifact is cleaned up.
        var gone = await ctx.WaitUntilAsync(async () =>
        {
            await using var db = ctx.ContextFactory.CreateDbContext();
            return await db.DownloadItems.CountAsync() == 0;
        });

        Assert.True(gone, "The HLS item should have been rejected and removed.");
        Assert.Equal(0, ctx.CompletedCount);
        Assert.Empty(ctx.GetAllFiles());
    }

    // ─── Delete / restore ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_CompletedMovie_RestoresChannelStreamAndPoster()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(80_000));
        var service = ctx.CreateService();

        var remoteUrl = ServerRoot + "movie.mp4";
        var remotePoster = ServerRoot + "posters/movie.jpg";
        var (profileId, playlistId, channelId) = await SeedMovieAsync(ctx, "Movie", remoteUrl, remotePoster);

        var result = await service.QueueDownloadAsync(new DownloadContentRequest(
            profileId, DownloadItemType.Vod, "Movie", remoteUrl, remotePoster, playlistId, channelId));

        var completed = await ctx.WaitForItemAsync(result.DownloadId!.Value, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);

        // MarkCompletedAsync flips Status=Completed before it maps the
        // Channel/Episode entities to the local files; wait for the mapping.
        var finalPath = Path.GetFullPath(completed.LocalFilePath!);
        var posterPath = Path.Combine(ctx.DownloadsRoot, "Movies", "Movie.poster.jpg");
        Assert.True(await ctx.WaitUntilAsync(async () =>
        {
            var ch = await GetChannelAsync(ctx, channelId);
            return ch.StreamUrl == finalPath;
        }));
        Assert.True(File.Exists(finalPath));
        Assert.True(File.Exists(posterPath));

        var channel = await GetChannelAsync(ctx, channelId);
        Assert.Equal(finalPath, channel.StreamUrl);
        Assert.StartsWith("file://", channel.LogoUrl, StringComparison.OrdinalIgnoreCase);

        await service.DeleteDownloadAsync(result.DownloadId!.Value);

        Assert.True(await ctx.WaitForRowGoneAsync(result.DownloadId!.Value));
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(!File.Exists(finalPath))));
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(!File.Exists(posterPath))));

        var restored = await GetChannelAsync(ctx, channelId);
        Assert.Equal(remoteUrl, restored.StreamUrl);
        Assert.Equal(remotePoster, restored.LogoUrl);
    }

    [Fact]
    public async Task Delete_CompletedSeriesEpisode_RestoresEpisodeSeasonSeriesUrls()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(80_000));
        var service = ctx.CreateService();

        var remoteUrl = ServerRoot + "s1e1.mp4";
        var remotePoster = ServerRoot + "posters/ep1.jpg";
        var (profileId, playlistId, episodeId, seasonId, seriesId) = await SeedEpisodeAsync(
            ctx, "The 100", remoteUrl, 1, 1, "Episode 1");

        var result = await service.QueueDownloadAsync(new DownloadContentRequest(
            profileId, DownloadItemType.SeriesEpisode, "Episode 1", remoteUrl, remotePoster,
            playlistId, 0, episodeId, null, null, seriesId, "The 100", 1, 1, "Episode 1"));

        var completed = await ctx.WaitForItemAsync(result.DownloadId!.Value, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);

        var finalPath = Path.GetFullPath(completed.LocalFilePath!);
        var posterPath = Path.Combine(ctx.DownloadsRoot, "Series", "The 100", "poster.jpg");
        Assert.True(await ctx.WaitUntilAsync(async () =>
        {
            var (ep, _, _) = await GetEpisodeChainAsync(ctx, episodeId);
            return ep.StreamUrl == finalPath;
        }));
        Assert.True(File.Exists(finalPath));
        Assert.True(File.Exists(posterPath));

        var (episode, season, series) = await GetEpisodeChainAsync(ctx, episodeId);
        Assert.Equal(finalPath, episode.StreamUrl);
        Assert.StartsWith("file://", episode.CoverUrl, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("file://", season.CoverUrl, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("file://", series.CoverUrl, StringComparison.OrdinalIgnoreCase);

        await service.DeleteDownloadAsync(result.DownloadId!.Value);

        Assert.True(await ctx.WaitForRowGoneAsync(result.DownloadId!.Value));
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(!File.Exists(finalPath))));
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(!File.Exists(posterPath))));

        var (restoredEpisode, restoredSeason, restoredSeries) = await GetEpisodeChainAsync(ctx, episodeId);
        Assert.Equal(remoteUrl, restoredEpisode.StreamUrl);
        Assert.Equal(remotePoster, restoredEpisode.CoverUrl);
        Assert.Equal(remotePoster, restoredSeason.CoverUrl);
        Assert.Equal(remotePoster, restoredSeries.CoverUrl);
    }

    [Fact]
    public async Task DeleteAll_ProfileScoped_RemovesOnlyThatProfile_RestoresItsEntities()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(60_000));
        var service = ctx.CreateService();

        var (profileA, playlistA, channelA) = await SeedMovieAsync(ctx, "Movie A", ServerRoot + "a.mp4", ServerRoot + "posters/a.jpg");
        var (profileB, playlistB, channelB) = await SeedMovieAsync(ctx, "Movie B", ServerRoot + "b.mp4", ServerRoot + "posters/b.jpg");

        var resultA = await service.QueueDownloadAsync(new DownloadContentRequest(
            profileA, DownloadItemType.Vod, "Movie A", ServerRoot + "a.mp4", ServerRoot + "posters/a.jpg", playlistA, channelA));
        var resultB = await service.QueueDownloadAsync(new DownloadContentRequest(
            profileB, DownloadItemType.Vod, "Movie B", ServerRoot + "b.mp4", ServerRoot + "posters/b.jpg", playlistB, channelB));

        Assert.NotNull(await ctx.WaitForItemAsync(resultA.DownloadId!.Value, i => i.Status == DownloadStatus.Completed));
        Assert.NotNull(await ctx.WaitForItemAsync(resultB.DownloadId!.Value, i => i.Status == DownloadStatus.Completed));

        var fileA = (await ctx.GetItemAsync(resultA.DownloadId!.Value))!.LocalFilePath!;
        var fileB = (await ctx.GetItemAsync(resultB.DownloadId!.Value))!.LocalFilePath!;
        Assert.True(File.Exists(fileA));
        Assert.True(File.Exists(fileB));

        // Wait until both channels are mapped to their local files (the mapping
        // happens right after Status=Completed is committed).
        Assert.True(await ctx.WaitUntilAsync(async () =>
            (await GetChannelAsync(ctx, channelA)).StreamUrl == fileA &&
            (await GetChannelAsync(ctx, channelB)).StreamUrl == fileB));

        await service.DeleteAllDownloadsAsync(profileA);

        Assert.True(await ctx.WaitForRowGoneAsync(resultA.DownloadId!.Value));
        var profile2Item = await ctx.GetItemAsync(resultB.DownloadId!.Value);
        Assert.NotNull(profile2Item);
        Assert.Equal(DownloadStatus.Completed, profile2Item.Status);
        Assert.True(File.Exists(fileB));

        Assert.Equal(ServerRoot + "a.mp4", (await GetChannelAsync(ctx, channelA)).StreamUrl);
        Assert.Equal(fileB, (await GetChannelAsync(ctx, channelB)).StreamUrl);
    }

    [Fact]
    public async Task DeleteAll_ActiveDownload_CancelsWorkerAndRemovesWithoutLeak()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(2 * 1024 * 1024));
        ctx.Handler.InitialDelayMs = 400;
        var service = ctx.CreateService();

        var result = await service.QueueDownloadAsync(new DownloadContentRequest(
            1, DownloadItemType.Vod, "activeMovie", ServerRoot + "activeMovie.mp4"));

        // The worker must be blocked inside the (delayed) HTTP request when
        // DeleteAllDownloadsAsync arrives, i.e. the CTS is registered.
        var downloading = await ctx.WaitForItemAsync(
            result.DownloadId!.Value, i => i.Status == DownloadStatus.Downloading);
        Assert.NotNull(downloading);

        await service.DeleteAllDownloadsAsync(1);

        Assert.True(await ctx.WaitForRowGoneAsync(result.DownloadId!.Value));
        Assert.Equal(0, ctx.CompletedCount);
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(!ctx.GetAllFiles().Any())));

        // The queue worker must remain healthy for subsequent downloads.
        await AssertDownloadCompletesAsync(ctx, "afterDeleteAll", "afterDeleteAll.mp4");
    }

    // ─── Posters ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Series_SharedPoster_SurvivesDeletingOneEpisode()
    {
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(60_000));
        var service = ctx.CreateService();

        var (profileId, playlistId, e1Id, e2Id, seasonId, seriesId) = await SeedTwoEpisodesAsync(
            ctx, "The 100", ServerRoot + "s1e1.mp4", ServerRoot + "s1e2.mp4");

        var req1 = new DownloadContentRequest(
            profileId, DownloadItemType.SeriesEpisode, "Episode 1", ServerRoot + "s1e1.mp4",
            ServerRoot + "posters/ep1.jpg", playlistId, 0, e1Id, null, null, seriesId, "The 100", 1, 1, "Episode 1");
        var req2 = new DownloadContentRequest(
            profileId, DownloadItemType.SeriesEpisode, "Episode 2", ServerRoot + "s1e2.mp4",
            ServerRoot + "posters/ep2.jpg", playlistId, 0, e2Id, null, null, seriesId, "The 100", 1, 2, "Episode 2");

        var r1 = await service.QueueDownloadAsync(req1);
        var r2 = await service.QueueDownloadAsync(req2);
        Assert.NotNull(await ctx.WaitForItemAsync(r1.DownloadId!.Value, i => i.Status == DownloadStatus.Completed));
        Assert.NotNull(await ctx.WaitForItemAsync(r2.DownloadId!.Value, i => i.Status == DownloadStatus.Completed));

        // Both episodes share ONE folder-level poster file.
        var sharedPoster = Path.Combine(ctx.DownloadsRoot, "Series", "The 100", "poster.jpg");
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(File.Exists(sharedPoster))));
        Assert.Single(ctx.GetAllFiles().Where(f => f.EndsWith("poster.jpg", StringComparison.OrdinalIgnoreCase)));

        var sharedUri = new Uri(Path.GetFullPath(sharedPoster), UriKind.Absolute).AbsoluteUri;
        Assert.True(await ctx.WaitUntilAsync(async () =>
        {
            var (_, season2Now, _) = await GetEpisodeChainAsync(ctx, e2Id);
            return season2Now.CoverUrl == sharedUri;
        }));

        // Deleting one episode must not break the other episode's offline poster.
        await service.DeleteDownloadAsync(r1.DownloadId!.Value);
        Assert.True(await ctx.WaitForRowGoneAsync(r1.DownloadId!.Value));

        Assert.True(File.Exists(sharedPoster), "The shared series poster must survive deleting one episode.");
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(File.Exists(sharedPoster))));

        var (e2, season2After, series2After) = await GetEpisodeChainAsync(ctx, e2Id);
        var r2Item = await ctx.GetItemAsync(r2.DownloadId!.Value);
        Assert.Equal(r2Item!.LocalFilePath, e2.StreamUrl);
        Assert.Equal(sharedUri, e2.CoverUrl);
        Assert.Equal(sharedUri, season2After.CoverUrl);
        Assert.Equal(sharedUri, series2After.CoverUrl);
        Assert.Equal(DownloadStatus.Completed, r2Item.Status);
        // The Downloads screen shows the poster from the DownloadItem itself;
        // it must keep pointing at the surviving local poster file.
        Assert.Equal(sharedUri, r2Item.PosterUrl);

        // And the surviving item can still be deleted cleanly afterwards.
        await service.DeleteDownloadAsync(r2.DownloadId!.Value);
        Assert.True(await ctx.WaitForRowGoneAsync(r2.DownloadId!.Value));
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(!File.Exists(sharedPoster))));
    }

    [Fact]
    public async Task Series_SharedPoster_SurvivesDeletingOneEpisode_WithoutSeriesId()
    {
        // Legacy rows predate SeriesId; the sharing check must fall back to
        // poster-path comparison instead of assuming the poster is not shared.
        using var ctx = new DownloadIntegrationContext(content: DownloadIntegrationContext.BuildContent(60_000));
        var service = ctx.CreateService();

        var (profileId, playlistId, e1Id, e2Id, _, _) = await SeedTwoEpisodesAsync(
            ctx, "The 100", ServerRoot + "s1e1.mp4", ServerRoot + "s1e2.mp4");

        // SeriesId = 0 → DownloadItem.SeriesId stays null, like legacy records.
        var req1 = new DownloadContentRequest(
            profileId, DownloadItemType.SeriesEpisode, "Episode 1", ServerRoot + "s1e1.mp4",
            ServerRoot + "posters/ep1.jpg", playlistId, 0, e1Id, null, null, 0, "The 100", 1, 1, "Episode 1");
        var req2 = new DownloadContentRequest(
            profileId, DownloadItemType.SeriesEpisode, "Episode 2", ServerRoot + "s1e2.mp4",
            ServerRoot + "posters/ep2.jpg", playlistId, 0, e2Id, null, null, 0, "The 100", 1, 2, "Episode 2");

        var r1 = await service.QueueDownloadAsync(req1);
        var r2 = await service.QueueDownloadAsync(req2);
        Assert.NotNull(await ctx.WaitForItemAsync(r1.DownloadId!.Value, i => i.Status == DownloadStatus.Completed));
        Assert.NotNull(await ctx.WaitForItemAsync(r2.DownloadId!.Value, i => i.Status == DownloadStatus.Completed));

        var sharedPoster = Path.Combine(ctx.DownloadsRoot, "Series", "The 100", "poster.jpg");
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(File.Exists(sharedPoster))));

        await service.DeleteDownloadAsync(r1.DownloadId!.Value);
        Assert.True(await ctx.WaitForRowGoneAsync(r1.DownloadId!.Value));

        Assert.True(File.Exists(sharedPoster), "The shared series poster must survive deleting one episode even without SeriesId.");
        Assert.True(await ctx.WaitUntilAsync(() => Task.FromResult(File.Exists(sharedPoster))));
    }

    // ─── Restart / visibility / resilience ───────────────────────────────────────

    [Fact]
    public async Task Restart_InterruptedDownload_AutoResumesViaBootstrapAndCompletes()
    {
        var content = DownloadIntegrationContext.BuildContent(120_000);
        using var ctx = new DownloadIntegrationContext(content: content, createService: false);

        var partPath = CreatePartFile(ctx, "movie", content[..70_000]);
        var itemId = await InsertManualItemAsync(ctx, 1, "movie", partPath, 70_000, content.Length, DownloadStatus.Downloading);

        // A fresh service instance simulates an app restart: the constructor
        // bootstraps interrupted downloads and must resume the .part file.
        var service = ctx.CreateService();

        var completed = await ctx.WaitForItemAsync(itemId, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);
        Assert.Null(completed.TempFilePath);

        var finalPath = completed.LocalFilePath!;
        Assert.True(File.Exists(finalPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(finalPath));
        Assert.Contains(ctx.Handler.Requests, r => r.Range is not null && r.Range.Contains("70000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PauseDownload_ActiveItemIsPausedBeforeCommandCompletes()
    {
        using var ctx = new DownloadIntegrationContext(
            content: DownloadIntegrationContext.BuildContent(2_000_000));
        ctx.Handler.InitialDelayMs = 5000;
        var service = ctx.CreateService();

        var result = await service.QueueDownloadAsync(
            new DownloadContentRequest(
                1,
                DownloadItemType.Vod,
                "slow movie",
                ServerRoot + "slow.mp4"));

        Assert.NotNull(result.DownloadId);
        var downloading = await ctx.WaitForItemAsync(
            result.DownloadId.Value,
            item => item.Status == DownloadStatus.Downloading);
        Assert.NotNull(downloading);

        await service.PauseDownloadAsync(result.DownloadId.Value);

        var paused = await ctx.GetItemAsync(result.DownloadId.Value);
        Assert.NotNull(paused);
        Assert.Equal(DownloadStatus.Paused, paused.Status);
    }

    [Fact]
    public async Task CancelDownload_ActiveItemIsRemovedBeforeCommandCompletes()
    {
        using var ctx = new DownloadIntegrationContext(
            content: DownloadIntegrationContext.BuildContent(2_000_000));
        ctx.Handler.InitialDelayMs = 5000;
        var service = ctx.CreateService();

        var result = await service.QueueDownloadAsync(
            new DownloadContentRequest(
                1,
                DownloadItemType.Vod,
                "cancel movie",
                ServerRoot + "cancel.mp4"));

        Assert.NotNull(result.DownloadId);
        var downloading = await ctx.WaitForItemAsync(
            result.DownloadId.Value,
            item => item.Status == DownloadStatus.Downloading);
        Assert.NotNull(downloading);

        await service.CancelDownloadAsync(result.DownloadId.Value);

        Assert.Null(await ctx.GetItemAsync(result.DownloadId.Value));
    }

    [Fact]
    public async Task GetDownloads_ReturnsAllProfiles_GlobalVisibility()
    {
        using var ctx = new DownloadIntegrationContext();
        var service = ctx.CreateService();

        await InsertManualItemAsync(ctx, 1, "q1", null, 0, null, DownloadStatus.Paused);
        await InsertManualItemAsync(ctx, 2, "q2", null, 0, null, DownloadStatus.Paused);

        // Phase 25: the download list is global; the profileId parameter is ignored.
        var all = await service.GetDownloadsAsync(1);
        Assert.Equal(2, all.Count);

        await service.DeleteAllDownloadsAsync(1);

        var remaining = await service.GetDownloadsAsync(0);
        var remainingItem = Assert.Single(remaining);
        Assert.Equal(2, remainingItem.ProfileId);
    }

    [Fact]
    public async Task Download_TempWriteFailure_IsPausedNotCompleted_WorkerSurvives()
    {
        using var ctx = new DownloadIntegrationContext();
        var service = ctx.CreateService();

        // A directory occupying the .part path makes the temp-file write fail
        // (UnauthorizedAccessException on Windows, IOException on Unix) — the
        // closest deterministic stand-in for a full disk.
        var partDir = Path.Combine(ctx.DownloadsRoot, "Movies", "video.mp4.part");
        Directory.CreateDirectory(partDir);

        var result = await service.QueueDownloadAsync(new DownloadContentRequest(
            1, DownloadItemType.Vod, "video", ServerRoot + "video.mp4"));

        Assert.True(result.Success);
        Assert.NotNull(result.DownloadId);

        // The item must end paused with an error (after auto-resume retries on
        // platforms where the failure surfaces as a transient IOException).
        var item = await ctx.WaitForItemAsync(
            result.DownloadId!.Value,
            i => i.Status == DownloadStatus.Paused,
            timeoutMs: 45000);

        Assert.NotNull(item);
        Assert.NotEqual(DownloadStatus.Completed, item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.ErrorMessage));
        Assert.False(File.Exists(Path.Combine(ctx.DownloadsRoot, "Movies", "video.mp4")));

        // The queue worker must still be alive and process new downloads.
        await AssertDownloadCompletesAsync(ctx, "video2", "video2.mp4");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task AssertDownloadCompletesAsync(
        DownloadIntegrationContext ctx,
        string displayName,
        string fileName)
    {
        var service = ctx.CreateService();
        var result = await service.QueueDownloadAsync(new DownloadContentRequest(
            1, DownloadItemType.Vod, displayName, ServerRoot + fileName));

        Assert.True(result.Success);
        var completed = await ctx.WaitForItemAsync(result.DownloadId!.Value, i => i.Status == DownloadStatus.Completed);
        Assert.NotNull(completed);
        Assert.True(File.Exists(completed.LocalFilePath!));
    }

    private static string CreatePartFile(DownloadIntegrationContext ctx, string stem, byte[] bytes)
    {
        var dir = Path.Combine(ctx.DownloadsRoot, "Movies");
        Directory.CreateDirectory(dir);
        var partPath = Path.Combine(dir, stem + ".mp4.part");
        File.WriteAllBytes(partPath, bytes);
        return partPath;
    }

    private static async Task<int> InsertManualItemAsync(
        DownloadIntegrationContext ctx,
        int profileId,
        string displayName,
        string? tempFilePath,
        long bytesDownloaded,
        int? bytesTotal,
        DownloadStatus status)
    {
        await using var db = ctx.ContextFactory.CreateDbContext();
        var item = new DownloadItem
        {
            ProfileId = profileId,
            DisplayName = displayName,
            SourceUrl = ServerRoot + displayName + ".mp4",
            TempFilePath = tempFilePath,
            BytesDownloaded = bytesDownloaded,
            BytesTotal = bytesTotal,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.DownloadItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static async Task<(int ProfileId, int PlaylistId, int ChannelId)> SeedMovieAsync(
        DownloadIntegrationContext ctx,
        string name,
        string streamUrl,
        string posterUrl)
    {
        await using var db = ctx.ContextFactory.CreateDbContext();
        var account = new ProviderAccount { Name = $"Account-{name}", Url = "http://provider.test" };
        db.ProviderAccounts.Add(account);
        await db.SaveChangesAsync();

        var profile = new Profile { Name = $"Profile-{name}", ProviderAccountId = account.Id };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var playlist = new Playlist { Name = $"Playlist-{name}", ProfileId = profile.Id };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync();

        var channel = new Channel
        {
            Name = name,
            StreamUrl = streamUrl,
            LogoUrl = posterUrl,
            Type = ChannelType.VOD,
            PlaylistId = playlist.Id
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        return (profile.Id, playlist.Id, channel.Id);
    }

    private static async Task<(int ProfileId, int PlaylistId, int EpisodeId, int SeasonId, int SeriesId)> SeedEpisodeAsync(
        DownloadIntegrationContext ctx,
        string seriesName,
        string streamUrl,
        int seasonNumber,
        int episodeNumber,
        string episodeName)
    {
        var (profileId, playlistId, episode1Id, _, seasonId, seriesId) = await SeedTwoEpisodesAsync(
            ctx, seriesName, streamUrl, null);
        return (profileId, playlistId, episode1Id, seasonId, seriesId);
    }

    private static async Task<(int ProfileId, int PlaylistId, int Episode1Id, int Episode2Id, int SeasonId, int SeriesId)> SeedTwoEpisodesAsync(
        DownloadIntegrationContext ctx,
        string seriesName,
        string streamUrl1,
        string? streamUrl2)
    {
        await using var db = ctx.ContextFactory.CreateDbContext();
        var account = new ProviderAccount { Name = $"Account-{seriesName}", Url = "http://provider.test" };
        db.ProviderAccounts.Add(account);
        await db.SaveChangesAsync();

        var profile = new Profile { Name = $"Profile-{seriesName}", ProviderAccountId = account.Id };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var playlist = new Playlist { Name = $"Playlist-{seriesName}", ProfileId = profile.Id };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync();

        var series = new Series { Name = seriesName, PlaylistId = playlist.Id };
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var season = new Season { SeasonNumber = 1, SeriesId = series.Id };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();

        var episode1 = new Episode
        {
            Name = "Episode 1",
            EpisodeNumber = 1,
            StreamUrl = streamUrl1,
            SeasonId = season.Id
        };
        db.Episodes.Add(episode1);
        if (!string.IsNullOrWhiteSpace(streamUrl2))
        {
            db.Episodes.Add(new Episode
            {
                Name = "Episode 2",
                EpisodeNumber = 2,
                StreamUrl = streamUrl2,
                SeasonId = season.Id
            });
        }

        await db.SaveChangesAsync();

        var episode2 = streamUrl2 is null
            ? null
            : await db.Episodes.AsNoTracking().SingleAsync(e => e.StreamUrl == streamUrl2);

        return (profile.Id, playlist.Id, episode1.Id, episode2?.Id ?? 0, season.Id, series.Id);
    }

    private static async Task<Channel> GetChannelAsync(DownloadIntegrationContext ctx, int channelId)
    {
        await using var db = ctx.ContextFactory.CreateDbContext();
        return await db.Channels.AsNoTracking().SingleAsync(c => c.Id == channelId);
    }

    private static async Task<(Episode Episode, Season Season, Series Series)> GetEpisodeChainAsync(
        DownloadIntegrationContext ctx,
        int episodeId)
    {
        await using var db = ctx.ContextFactory.CreateDbContext();
        var episode = await db.Episodes
            .AsNoTracking()
            .Include(e => e.Season)
            .ThenInclude(s => s!.Series)
            .SingleAsync(e => e.Id == episodeId);
        return (episode, episode.Season!, episode.Season!.Series!);
    }
}
