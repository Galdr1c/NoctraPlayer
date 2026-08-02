using System.IO;
using System.Net.Http;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class ContentDownloadServicePipelineTests
{
    [Theory]
    [InlineData("https://cdn.example/movie/master.m3u8", true)]
    [InlineData("https://cdn.example/movie/manifest.mpd?token=abc", true)]
    [InlineData("https://cdn.example/get?format=m3u8&token=abc", true)]
    [InlineData("https://cdn.example/get?output=ts&token=abc", false)]
    [InlineData("https://cdn.example/movie/file.mp4", false)]
    [InlineData("https://cdn.example/movie/file.mkv?token=abc", false)]
    public void SegmentedManifestUrls_AreRecognizedBeforeQueueing(
        string sourceUrl,
        bool expected)
    {
        Assert.Equal(
            expected,
            ContentDownloadService.IsSegmentedManifestUrl(sourceUrl));
    }

    [Fact]
    public void ContentKind_FromUrl_DistinguishesHlsDashAndDirectFile()
    {
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromUrl("https://cdn.example/live/master.m3u8"));
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromUrl("https://cdn.example/get?format=hls"));
        Assert.Equal(DownloadContentKind.Dash, ContentDownloadService.DetectContentKindFromUrl("https://cdn.example/live/manifest.mpd"));
        Assert.Equal(DownloadContentKind.Dash, ContentDownloadService.DetectContentKindFromUrl("https://cdn.example/get?output=dash"));
        Assert.Equal(DownloadContentKind.DirectFile, ContentDownloadService.DetectContentKindFromUrl("https://cdn.example/movie/file.mp4"));
        Assert.Equal(DownloadContentKind.Unsupported, ContentDownloadService.DetectContentKindFromUrl(null));
    }

    [Fact]
    public void ContentKind_FromContentType_RecognizesManifestMediaTypes()
    {
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromContentType("application/vnd.apple.mpegurl"));
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromContentType("application/x-mpegurl"));
        Assert.Equal(DownloadContentKind.Dash, ContentDownloadService.DetectContentKindFromContentType("application/dash+xml"));
        Assert.Equal(DownloadContentKind.DirectFile, ContentDownloadService.DetectContentKindFromContentType("video/mp4"));
        Assert.Equal(DownloadContentKind.DirectFile, ContentDownloadService.DetectContentKindFromContentType("application/octet-stream"));
        Assert.Equal(DownloadContentKind.DirectFile, ContentDownloadService.DetectContentKindFromContentType(null));
    }

    [Fact]
    public void ContentKind_FromContent_SniffsManifestBodies()
    {
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromContent("#EXTM3U\n#EXT-X-VERSION:3\n"u8));
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromContent("#EXTINF:5.0,\nsegment0.ts\n"u8));
        Assert.Equal(DownloadContentKind.Hls, ContentDownloadService.DetectContentKindFromContent("\uFEFF#EXTM3U\n"u8));
        Assert.Equal(DownloadContentKind.Dash, ContentDownloadService.DetectContentKindFromContent("<?xml version=\"1.0\"?><MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\">"u8));
        Assert.Equal(DownloadContentKind.Dash, ContentDownloadService.DetectContentKindFromContent("<mpd minBufferTime=\"PT1S\">"u8));
        Assert.Equal(DownloadContentKind.DirectFile, ContentDownloadService.DetectContentKindFromContent("RIFF\x00\x00\x00\x00AVI "u8));
        Assert.Equal(DownloadContentKind.DirectFile, ContentDownloadService.DetectContentKindFromContent(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ContentKind_FromContent_HandlesManifestServedAsOctetStream()
    {
        // The scenario from the bug report: a manifest delivered with a generic
        // media type and an extension-less URL must still be detected.
        var kind = ContentDownloadService.DetectContentKindFromContent("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000\nplaylist.m3u8\n"u8);
        Assert.Equal(DownloadContentKind.Hls, kind);
        Assert.True(ContentDownloadService.IsManifestContentKind(kind));
    }

    [Theory]
    [InlineData("application/vnd.apple.mpegurl", true)]
    [InlineData("application/dash+xml", true)]
    [InlineData("video/mp4", false)]
    [InlineData(null, false)]
    public void SegmentedManifestContentTypes_AreRecognized(
        string? mediaType,
        bool expected)
    {
        Assert.Equal(
            expected,
            ContentDownloadService.IsSegmentedManifestContentType(mediaType));
    }

    [Fact]
    public void TransientDownloadExceptions_IncludeTransportFailuresButNotCancellation()
    {
        Assert.True(
            ContentDownloadService.IsTransientDownloadException(
                new HttpRequestException("connection reset")));
        Assert.True(
            ContentDownloadService.IsTransientDownloadException(
                new IOException("unexpected end of stream")));
        Assert.True(
            ContentDownloadService.IsTransientDownloadException(
                new TimeoutException("request timed out")));
        Assert.False(
            ContentDownloadService.IsTransientDownloadException(
                new OperationCanceledException()));
        Assert.False(
            ContentDownloadService.IsTransientDownloadException(
                new InvalidOperationException("invalid media")));
    }

    [Fact]
    public void PosterPath_UsesPerMovieFileAndSharedSeriesFolderLocations()
    {
        var movie = new DownloadItem { ChannelType = ChannelType.VOD };
        var series = new DownloadItem { ChannelType = ChannelType.Series };

        var moviePoster = ContentDownloadService.BuildPosterPath(
            movie,
            Path.Combine("Downloads", "Movies", "Movie.mp4"));
        var seriesPoster = ContentDownloadService.BuildPosterPath(
            series,
            Path.Combine("Downloads", "Series", "Show", "Season 01", "Episode.mp4"));

        Assert.EndsWith(
            Path.Combine("Movies", "Movie.poster.jpg"),
            moviePoster,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Series", "Show", "poster.jpg"),
            seriesPoster,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreMappedPoster_RestoresOriginalRemotePoster_WhenKnown()
    {
        var moviePath = Path.Combine("Downloads", "Movies", "Movie.mp4");
        var localPoster = Path.Combine("Downloads", "Movies", "Movie.poster.jpg");
        var item = new DownloadItem
        {
            SourcePosterUrl = "https://cdn.example/posters/movie.jpg",
            LocalFilePath = moviePath
        };

        Assert.Equal(
            "https://cdn.example/posters/movie.jpg",
            ContentDownloadService.RestoreMappedPoster(localPoster, item));
    }

    [Fact]
    public void RestoreMappedPoster_ClearsDeadLocalPoster_WhenSourceUnknown()
    {
        var moviePath = Path.Combine("Downloads", "Movies", "Movie.mp4");
        var localPoster = Path.Combine("Downloads", "Movies", "Movie.poster.jpg");
        var legacyItem = new DownloadItem
        {
            SourcePosterUrl = null,
            LocalFilePath = moviePath
        };

        Assert.Null(ContentDownloadService.RestoreMappedPoster(localPoster, legacyItem));
        Assert.Equal(
            "https://kept.example/poster.jpg",
            ContentDownloadService.RestoreMappedPoster("https://kept.example/poster.jpg", legacyItem));
    }
}
