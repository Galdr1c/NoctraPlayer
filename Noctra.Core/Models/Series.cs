using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations.Schema;
using Noctra.Services;
using CommunityToolkit.Mvvm.ComponentModel;
namespace Noctra.Models;

/// <summary>
/// Dizi bilgilerini tutar
/// </summary>
public partial class Series : ObservableObject
{
    private ICollection<Season> _seasons = new List<Season>();

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    [ObservableProperty]
    private string? _coverUrl;

    [ObservableProperty]
    private string? _plot;

    [ObservableProperty]
    private string? _genre;

    [ObservableProperty]
    private int? _releaseYear;

    [ObservableProperty]
    private double? _rating;

    [ObservableProperty]
    private string? _contentRating;

    public int PlaylistId { get; set; }

    // TMDB Integration
    public int? TmdbId { get; set; }
    public string? TmdbTitle { get; set; }

    [ObservableProperty]
    private DateTime? _lastTmdbSync;

    [ObservableProperty]
    private string? _cast;

    [ObservableProperty]
    private string? _director;

    [ObservableProperty]
    private string? _backdropUrl;

    [ObservableProperty]
    private string? _trailerUrl;

    public DateTime? MetadataFetchedAt { get; set; }

    [ObservableProperty]
    private string? _networkName;

    [ObservableProperty]
    private string? _networkLogoUrl;

    [ObservableProperty]
    private bool _isInMyList;
    
    [ObservableProperty]
    private bool _isFavorite;
    
    // Navigation properties
    public Playlist? Playlist { get; set; }
    public ICollection<Season> Seasons
    {
        get => _seasons;
        set
        {
            _seasons = value ?? new List<Season>();
            OnPropertyChanged(nameof(SeasonCountSafe));
            OnPropertyChanged(nameof(TotalEpisodesCount));
            OnPropertyChanged(nameof(SeriesInfoText));
        }
    }

    public string? GroupTitle { get; set; }

    [NotMapped]
    public string? DisplayCategory => !string.IsNullOrEmpty(GroupTitle) ? GroupTitle : Genre;

    [NotMapped]
    public string? LocalSizeText { get; set; }

    [NotMapped]
    public int SeasonCountSafe => _seasons?.Count ?? 0;

    [NotMapped]
    public int TotalEpisodesCount => _seasons?.Sum(s => s.Episodes?.Count ?? 0) ?? 0;

    [NotMapped]
    public DateTime? LastWatchedEpisodeAt { get; set; }

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

/// <summary>
/// Sezon bilgilerini tutar
/// </summary>
public partial class Season : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string? _coverUrl;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string? _plot;

    public int SeriesId { get; set; }
    public int? TmdbSeasonId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsExpanded { get; set; }

    // Navigation properties
    public Series? Series { get; set; }
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}

/// <summary>
/// Bölüm bilgilerini tutar
/// </summary>
public partial class Episode : ObservableObject
{
    public int Id { get; set; }
    public int EpisodeNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;

    [ObservableProperty]
    private string? _plot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(DisplaySubtitle))]
    private string? _tmdbEpisodeName;

    [ObservableProperty]
    private string? _coverUrl;
    
    [ObservableProperty]
    private TimeSpan? _duration;
    
    [ObservableProperty]
    private DateTime? _lastWatched;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedPercentage))]
    private TimeSpan? _watchedPosition;
    
    public DateTime? AirDate { get; set; }
    public int SeasonId { get; set; }

    // Intro/Credits timestamps (seconds)
    public double? IntroStartSec { get; set; }
    public double? IntroEndSec { get; set; }
    public double? CreditsStartSec { get; set; }
    
    // Navigation property
    public Season? Season { get; set; }

    [NotMapped]
    public string? DurationText => Duration.HasValue && Duration.Value.TotalMinutes > 0
        ? $"{(int)Duration.Value.TotalMinutes} dk"
        : null;

    [NotMapped]
    public string? AirDateText => AirDate.HasValue
        ? AirDate.Value.ToString("dd MMM yyyy")
        : null;

    [NotMapped]
    public string? EpisodeMetaText
    {
        get
        {
            var parts = new List<string>();
            if (AirDate.HasValue) parts.Add(AirDate.Value.ToString("dd MMM yyyy"));
            if (Duration.HasValue && Duration.Value.TotalMinutes > 0) parts.Add($"{(int)Duration.Value.TotalMinutes} dk");
            return parts.Count > 0 ? string.Join("  •  ", parts) : null;
        }
    }

    /// <summary>
    /// Single effective display title: the provider title first (what the user
    /// recognizes), otherwise the localized TMDB title, otherwise "Episode N".
    /// </summary>
    [NotMapped]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            if (!string.IsNullOrWhiteSpace(TmdbEpisodeName))
            {
                return TmdbEpisodeName;
            }

            return $"Episode {EpisodeNumber}";
        }
    }

    /// <summary>
    /// The TMDB title as a subtitle — only when it differs from the effective
    /// display title (no duplicate, no same-language echo).
    /// </summary>
    [NotMapped]
    public string? DisplaySubtitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TmdbEpisodeName))
            {
                return null;
            }

            return string.Equals(TmdbEpisodeName, DisplayTitle, StringComparison.OrdinalIgnoreCase)
                ? null
                : TmdbEpisodeName;
        }
    }

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

    [ObservableProperty]
    private bool _isCompleted;

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
}
