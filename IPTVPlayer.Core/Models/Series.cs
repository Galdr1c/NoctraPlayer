namespace IPTVPlayer.Models;

/// <summary>
/// Dizi bilgilerini tutar
/// </summary>
public class Series
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public string? Plot { get; set; }
    public string? Genre { get; set; }
    public int? ReleaseYear { get; set; }
    public double? Rating { get; set; }
    public int PlaylistId { get; set; }
    public bool IsInMyList { get; set; }
    
    // Navigation properties
    public Playlist? Playlist { get; set; }
    public ICollection<Season> Seasons { get; set; } = new List<Season>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? GroupTitle => Genre;
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
    
    // Navigation property
    public Season? Season { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
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
}
