using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations.Schema;
using Noctra.Services;
namespace Noctra.Models;

/// <summary>
/// Dizi bilgilerini tutar
/// </summary>
public class Series
{
    private ICollection<Season> _seasons = new List<Season>();
    private int? _cachedSeasonCount;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public string? Plot { get; set; }
    public string? Genre { get; set; }
    public int? ReleaseYear { get; set; }
    public double? Rating { get; set; }
    public int PlaylistId { get; set; }
    public bool IsInMyList { get; set; }
    public bool IsFavorite { get; set; }
    
    // Navigation properties
    public Playlist? Playlist { get; set; }
    public ICollection<Season> Seasons
    {
        get => _seasons;
        set
        {
            _seasons = value ?? new List<Season>();
            _cachedSeasonCount = null;
        }
    }

    [NotMapped]
    public string? GroupTitle => Genre;

    [NotMapped]
    public int SeasonCountSafe
    {
        get
        {
            if (_cachedSeasonCount.HasValue)
            {
                return _cachedSeasonCount.Value;
            }

            if (_seasons == null || _seasons.Count == 0)
            {
                _cachedSeasonCount = 1;
                return 1;
            }

            var seasonNumbers = new HashSet<int>();
            foreach (var season in _seasons)
            {
                if (season.SeasonNumber > 0)
                {
                    seasonNumbers.Add(season.SeasonNumber);
                }
            }

            _cachedSeasonCount = seasonNumbers.Count > 0 ? seasonNumbers.Count : 1;
            return _cachedSeasonCount.Value;
        }
    }
}

/// <summary>
/// Sezon bilgilerini tutar
/// </summary>
public class Season
{
    public int Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public string? CoverUrl { get; set; }
    public int SeriesId { get; set; }
    
    // Navigation properties
    public Series? Series { get; set; }
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}

/// <summary>
/// Bölüm bilgilerini tutar
/// </summary>
public class Episode
{
    public int Id { get; set; }
    public int EpisodeNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? Plot { get; set; }
    public string? CoverUrl { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime? LastWatched { get; set; }
    public TimeSpan? WatchedPosition { get; set; }
    public int SeasonId { get; set; }

    // Intro/Credits timestamps (seconds)
    public double? IntroStartSec { get; set; }
    public double? IntroEndSec { get; set; }
    public double? CreditsStartSec { get; set; }
    
    // Navigation property
    public Season? Season { get; set; }

    [NotMapped]
    public double WatchedPercentage
    {
        get
        {
            if (Duration == null || Duration.Value.TotalSeconds <= 0 || WatchedPosition == null)
                return 0;

            var percent = (WatchedPosition.Value.TotalSeconds / Duration.Value.TotalSeconds) * 100;
            return Math.Clamp(percent, 0, 100);
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsCompleted { get; set; }

    [NotMapped]
    public string BaseDisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return string.Empty;
            var info = SeriesInfoParser.Parse(Name);
            return info.SeriesName;
        }
    }

    [NotMapped]
    public string? SeriesInfoText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return null;
            var info = SeriesInfoParser.Parse(Name);
            return SeriesInfoParser.GetSeriesInfoText(info.Season, info.Episode);
        }
    }
}
