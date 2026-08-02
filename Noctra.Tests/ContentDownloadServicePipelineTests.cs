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
