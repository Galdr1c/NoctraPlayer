using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Noctra.Models;

/// <summary>
/// Kanal türünü belirler
/// </summary>
public enum ChannelType
{
    Live,    // Canlı TV
    VOD,     // Video on Demand
    Series   // Dizi
}

/// <summary>
/// Noctra kanalını temsil eder
/// </summary>
public enum ChannelSortOrder
{
    NewestFirst,
    OldestFirst,
    NameAsc,
    NameDesc
}

/// <summary>
/// Noctra channel entity.
/// </summary>
public partial class Channel : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? GroupTitle { get; set; }
    public string? TvgId { get; set; }
    public string? TvgName { get; set; }
    public string? Language { get; set; }
    public string? Country { get; set; }
    public ChannelType Type { get; set; } = ChannelType.Live;
    
    public int? TmdbId { get; set; }

    [ObservableProperty]
    private DateTime? _lastTmdbSync;

    [ObservableProperty]    private bool _isFavorite;
    
    [ObservableProperty]
    private bool _isInMyList;
    public DateTime? LastWatched { get; set; }
    public int PlaylistId { get; set; }
    
    // Metadata for VOD/Movies
    [ObservableProperty] private string? _plot;
    [ObservableProperty] private int? _releaseYear;
    [ObservableProperty] private double? _rating;
    [ObservableProperty] private string? _backdropUrl;
    [ObservableProperty] private string? _director;
    [ObservableProperty] private string? _cast;
    [ObservableProperty] private string? _contentRating;
    
    public TimeSpan? Duration { get; set; }
    public TimeSpan? WatchedPosition { get; set; }
    public bool IsCompleted { get; set; }
    
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
    
    [property: NotMapped]
    [ObservableProperty]
    private string? _currentProgramTitle;

    [property: NotMapped]
    [ObservableProperty]
    private double _epgProgress;

    public Playlist? Playlist { get; set; }
    
    [NotMapped]
    public string? LocalSizeText { get; set; }

    [NotMapped]
    public string? CoverUrl => !string.IsNullOrEmpty(LogoUrl) ? LogoUrl : BackdropUrl;
    
    [NotMapped]
    public string? Description => Plot;

    public void NotifyVisualsChanged()
    {
        OnPropertyChanged(nameof(LogoUrl));
        OnPropertyChanged(nameof(BackdropUrl));
        OnPropertyChanged(nameof(CoverUrl));
    }

    /// <summary>
    /// Fires PropertyChanged for all metadata fields so the player overlay and detail view
    /// refresh immediately when TMDB data is applied after the initial render.
    /// </summary>
    public void NotifyMetadataChanged()
    {
        NotifyVisualsChanged();
        OnPropertyChanged(nameof(Plot));
        OnPropertyChanged(nameof(Director));
        OnPropertyChanged(nameof(Cast));
        OnPropertyChanged(nameof(Rating));
        OnPropertyChanged(nameof(ContentRating));
        OnPropertyChanged(nameof(Description));
    }
}


