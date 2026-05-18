using Noctra.Models;
using System.Text.Json;

namespace Noctra.Tests;

public class TrailerUrlTests
{
    /// <summary>
    /// Replicates the trailer extraction logic from MainViewModel (~line 6540) and TmdbSyncService (~line 224).
    /// </summary>
    private static TmdbVideo? PickBestTrailer(TmdbVideoResponse? videos)
    {
        return videos?.Results?
            .Where(v => v.Site == "YouTube" && (v.Type == "Trailer" || v.Type == "Teaser"))
            .OrderByDescending(v => v.Type == "Teaser")
            .ThenByDescending(v => v.Type == "Trailer")
            .FirstOrDefault();
    }

    private static string? BuildTrailerUrl(TmdbVideo? video)
        => video != null && !string.IsNullOrEmpty(video.Key)
            ? $"https://www.youtube.com/watch?v={video.Key}"
            : null;

    // ── JSON-based deserialization tests ──────────────────────────────────

    [Fact]
    public void Deserialize_TmdbVideoResponse_WithMultipleVideos()
    {
        var json = """
        {
            "results": [
                { "key": "abc123", "site": "YouTube", "type": "Trailer", "official": true },
                { "key": "def456", "site": "YouTube", "type": "Teaser", "official": true },
                { "key": "ghi789", "site": "Vimeo",   "type": "Trailer", "official": false },
                { "key": "jkl012", "site": "YouTube", "type": "Behind the Scenes", "official": false }
            ]
        }
        """;

        var response = JsonSerializer.Deserialize<TmdbVideoResponse>(json);
        Assert.NotNull(response);
        Assert.NotNull(response.Results);
        Assert.Equal(4, response.Results.Count);
    }

    [Fact]
    public void Deserialize_EmptyResults()
    {
        var json = """{ "results": [] }""";
        var response = JsonSerializer.Deserialize<TmdbVideoResponse>(json);
        Assert.NotNull(response);
        Assert.NotNull(response.Results);
        Assert.Empty(response.Results);
    }

    [Fact]
    public void Deserialize_NullResults_IsNull()
    {
        var json = """{}""";
        var response = JsonSerializer.Deserialize<TmdbVideoResponse>(json);
        Assert.NotNull(response);
        Assert.Null(response.Results);
    }

    // ── Trailer selection logic tests ─────────────────────────────────────

    [Fact]
    public void Picks_YouTubeTrailer_WhenAvailable()
    {
        var videos = new TmdbVideoResponse
        {
            Results = new()
            {
                new() { Key = "abc123", Site = "YouTube", Type = "Trailer" }
            }
        };

        var best = PickBestTrailer(videos);
        Assert.NotNull(best);
        Assert.Equal("abc123", best.Key);
    }

    [Fact]
    public void Prefers_YouTubeTeaser_OverTrailer()
    {
        // OrderByDescending(v => v.Type == "Teaser") → Teaser (true) comes before Trailer (false)
        var videos = new TmdbVideoResponse
        {
            Results = new()
            {
                new() { Key = "trailer1", Site = "YouTube", Type = "Trailer" },
                new() { Key = "teaser1",  Site = "YouTube", Type = "Teaser" }
            }
        };

        var best = PickBestTrailer(videos);
        Assert.NotNull(best);
        Assert.Equal("teaser1", best.Key);
    }

    [Fact]
    public void FallsBackTo_Trailer_WhenNoTeaser()
    {
        var videos = new TmdbVideoResponse
        {
            Results = new()
            {
                new() { Key = "onlyTrailer", Site = "YouTube", Type = "Trailer" }
            }
        };

        var best = PickBestTrailer(videos);
        Assert.NotNull(best);
        Assert.Equal("onlyTrailer", best.Key);
    }

    [Fact]
    public void FiltersOut_NonYouTubeVideos()
    {
        var videos = new TmdbVideoResponse
        {
            Results = new()
            {
                new() { Key = "vimeoKey", Site = "Vimeo", Type = "Trailer" },
                new() { Key = "ytKey",    Site = "YouTube", Type = "Trailer" }
            }
        };

        var best = PickBestTrailer(videos);
        Assert.NotNull(best);
        Assert.Equal("ytKey", best.Key);
    }

    [Fact]
    public void FiltersOut_NonTrailerNonTeaserTypes()
    {
        var videos = new TmdbVideoResponse
        {
            Results = new()
            {
                new() { Key = "bts1", Site = "YouTube", Type = "Behind the Scenes" },
                new() { Key = "clip1", Site = "YouTube", Type = "Clip" },
                new() { Key = "feat1", Site = "YouTube", Type = "Featurette" },
                new() { Key = "realTrailer", Site = "YouTube", Type = "Trailer" }
            }
        };

        var best = PickBestTrailer(videos);
        Assert.NotNull(best);
        Assert.Equal("realTrailer", best.Key);
    }

    [Fact]
    public void ReturnsNull_WhenNoYouTubeTrailerOrTeaser()
    {
        var videos = new TmdbVideoResponse
        {
            Results = new()
            {
                new() { Key = "bts1",  Site = "YouTube", Type = "Behind the Scenes" },
                new() { Key = "vKey",  Site = "Vimeo",   Type = "Trailer" }
            }
        };

        var best = PickBestTrailer(videos);
        Assert.Null(best);
    }

    [Fact]
    public void ReturnsNull_WhenResultsIsNull()
    {
        var videos = new TmdbVideoResponse { Results = null };
        Assert.Null(PickBestTrailer(videos));
    }

    [Fact]
    public void ReturnsNull_WhenResultsIsEmpty()
    {
        var videos = new TmdbVideoResponse { Results = new() };
        Assert.Null(PickBestTrailer(videos));
    }

    // ── Trailer URL format tests ──────────────────────────────────────────

    [Fact]
    public void Builds_CorrectYouTubeUrl()
    {
        var video = new TmdbVideo { Key = "dQw4w9WgXcQ", Site = "YouTube", Type = "Trailer" };
        var url = BuildTrailerUrl(video);

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", url);
    }

    [Fact]
    public void ReturnsNull_WhenVideoIsNull()
    {
        Assert.Null(BuildTrailerUrl(null));
    }

    [Fact]
    public void ReturnsNull_WhenKeyIsEmpty()
    {
        var video = new TmdbVideo { Key = "", Site = "YouTube", Type = "Trailer" };
        Assert.Null(BuildTrailerUrl(video));
    }

    // ── End-to-end integration-style tests ────────────────────────────────

    [Fact]
    public void EndToEnd_SelectsAndBuildsUrl_FromRealisticResponse()
    {
        // Simulates a realistic TMDB /tv/{id}?append_to_response=videos response
        var json = """
        {
            "id": 1399,
            "name": "Game of Thrones",
            "videos": {
                "results": [
                    { "key": "KPLWWIOCOOQ", "site": "YouTube", "type": "Opening Credits", "official": true },
                    { "key": "giYc6XPVhrA", "site": "YouTube", "type": "Trailer", "official": true },
                    { "key": "bV9Epcrv7SA", "site": "YouTube", "type": "Teaser", "official": true },
                    { "key": "8sR3BV3S6kQ", "site": "YouTube", "type": "Behind the Scenes", "official": false }
                ]
            }
        }
        """;

        var detail = JsonSerializer.Deserialize<TmdbDetail>(json);
        Assert.NotNull(detail);

        var best = PickBestTrailer(detail.Videos);
        Assert.NotNull(best);
        Assert.Equal("bV9Epcrv7SA", best.Key); // Teaser preferred over Trailer

        var url = BuildTrailerUrl(best);
        Assert.Equal("https://www.youtube.com/watch?v=bV9Epcrv7SA", url);
    }

    [Fact]
    public void EndToEnd_NoVideos_ReturnsNoTrailer()
    {
        var json = """
        {
            "id": 1399,
            "name": "Game of Thrones",
            "videos": { "results": [] }
        }
        """;

        var detail = JsonSerializer.Deserialize<TmdbDetail>(json);
        Assert.NotNull(detail);

        Assert.Null(PickBestTrailer(detail.Videos));
    }

    [Fact]
    public void EndToEnd_OnlyNonYouTube_ReturnsNoTrailer()
    {
        var json = """
        {
            "id": 1399,
            "name": "Game of Thrones",
            "videos": {
                "results": [
                    { "key": "someKey", "site": "Vimeo", "type": "Trailer", "official": true }
                ]
            }
        }
        """;

        var detail = JsonSerializer.Deserialize<TmdbDetail>(json);
        Assert.NotNull(detail);

        Assert.Null(PickBestTrailer(detail.Videos));
    }
}
