using System.Text.Json.Serialization;

namespace Noctra.Models;

/// <summary>
/// TMDB API search response wrapper
/// </summary>
public class TmdbSearchResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("results")]
    public List<TmdbResult> Results { get; set; } = new();
    
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
    
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

/// <summary>
/// Individual TMDB search result (movie or TV show)
/// </summary>
public class TmdbResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }
    
    [JsonPropertyName("title")]
    public string? Title { get; set; }  // For movies
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }  // For TV shows
    
    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }
    
    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }
    
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
    
    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }
    
    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }
    
    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }
    
    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }
    
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }  // For movies (YYYY-MM-DD)
    
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }  // For TV shows (YYYY-MM-DD)
    
    [JsonPropertyName("genre_ids")]
    public List<int> GenreIds { get; set; } = new();
    
    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }
    
    [JsonPropertyName("adult")]
    public bool Adult { get; set; }
    
    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }
    
    /// <summary>
    /// Gets the display title (movie title or TV show name)
    /// </summary>
    public string DisplayTitle => Title ?? Name ?? OriginalTitle ?? OriginalName ?? "Unknown";
    
    /// <summary>
    /// Gets the release year from the date string
    /// </summary>
    public int? ReleaseYear
    {
        get
        {
            var dateStr = ReleaseDate ?? FirstAirDate;
            if (string.IsNullOrEmpty(dateStr) || dateStr.Length < 4)
                return null;
            
            if (int.TryParse(dateStr[..4], out var year))
                return year;
            
            return null;
        }
    }
}

public class TmdbDetail : TmdbResult
{
    [JsonPropertyName("credits")]
    public TmdbCredits? Credits { get; set; }

    [JsonPropertyName("release_dates")]
    public TmdbReleaseDatesResponse? ReleaseDates { get; set; }

    [JsonPropertyName("content_ratings")]
    public TmdbContentRatingsResponse? ContentRatings { get; set; }
}

/// <summary>
/// TMDB genre response
/// </summary>
public class TmdbGenreResponse
{
    [JsonPropertyName("genres")]
    public List<TmdbGenre> Genres { get; set; } = new();
}

/// <summary>
/// Individual TMDB genre
/// </summary>
public class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Domain model for enriched channel metadata
/// </summary>
public class ChannelMetadata
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public double? Rating { get; set; }
    public int? ReleaseYear { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? MediaType { get; set; }
    public int? TmdbId { get; set; }
    public string? Director { get; set; }
    public string? Cast { get; set; }
    public string? ContentRating { get; set; }
}

public class TmdbCredits
{
    [JsonPropertyName("cast")]
    public List<TmdbCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<TmdbCrew> Crew { get; set; } = new();
}

public class TmdbCast
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("character")]
    public string? Character { get; set; }
    
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public class TmdbCrew
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("job")]
    public string? Job { get; set; } // Director, Producer, etc.
}

public class TmdbReleaseDatesResponse
{
    [JsonPropertyName("results")]
    public List<TmdbCountryReleaseDate> Results { get; set; } = new();
}

public class TmdbCountryReleaseDate
{
    [JsonPropertyName("iso_3166_1")]
    public string IsoCode { get; set; } = string.Empty;

    [JsonPropertyName("release_dates")]
    public List<TmdbCertificationResult> ReleaseDates { get; set; } = new();
}

public class TmdbCertificationResult
{
    [JsonPropertyName("certification")]
    public string Certification { get; set; } = string.Empty;
}

public class TmdbContentRatingsResponse
{
    [JsonPropertyName("results")]
    public List<TmdbCountryContentRating> Results { get; set; } = new();
}

public class TmdbCountryContentRating
{
    [JsonPropertyName("iso_3166_1")]
    public string IsoCode { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public string Rating { get; set; } = string.Empty;
}

/// <summary>
/// TMDB Season details including episode list
/// </summary>
public class TmdbSeasonDetail
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("episodes")]
    public List<TmdbEpisodeDetail> Episodes { get; set; } = new();
}

/// <summary>
/// TMDB Episode details inside a season
/// </summary>
public class TmdbEpisodeDetail
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("still_path")]
    public string? StillPath { get; set; } // Episode Thumbnail
    
    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }
}

