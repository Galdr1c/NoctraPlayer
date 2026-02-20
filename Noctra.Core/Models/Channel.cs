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
    
    [ObservableProperty]
    private string _name = string.Empty;
    
    [ObservableProperty]
    private string _streamUrl = string.Empty;
    
    [ObservableProperty]
    private string? _logoUrl;
    
    [ObservableProperty]
    private string? _groupTitle;
    
    public string? TvgId { get; set; }
    public string? TvgName { get; set; }
    public string? Language { get; set; }
    
    [ObservableProperty]
    private ChannelType _type = ChannelType.Live;
    
    [ObservableProperty]
    private bool _isFavorite;
    
    [ObservableProperty]
    private bool _isInMyList;
    
    public DateTime? LastWatched { get; set; }
    public int PlaylistId { get; set; }
    
    // Metadata for VOD/Movies
    [ObservableProperty]
    private string? _plot;
    
    [ObservableProperty]
    private int? _releaseYear;
    
    [ObservableProperty]
    private double? _rating;
    
    [ObservableProperty]
    private string? _backdropUrl;
    
    [ObservableProperty]
    private string? _director;
    
    [ObservableProperty]
    private string? _cast;
    
    public TimeSpan? Duration { get; set; }
    public TimeSpan? WatchedPosition { get; set; }
    
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
    
    // Navigation property
    public Playlist? Playlist { get; set; }
    
    [NotMapped]
    public string? CoverUrl => !string.IsNullOrEmpty(BackdropUrl) ? BackdropUrl : LogoUrl;
    
    [NotMapped]
    public string? Description => Plot;

    partial void OnLogoUrlChanged(string? value)
        => OnPropertyChanged(nameof(CoverUrl));

    partial void OnBackdropUrlChanged(string? value)
        => OnPropertyChanged(nameof(CoverUrl));

    partial void OnPlotChanged(string? value)
        => OnPropertyChanged(nameof(Description));
}


