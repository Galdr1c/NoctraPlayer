namespace IPTVPlayer.Models;

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
/// IPTV kanalını temsil eder
/// </summary>
public class Channel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? GroupTitle { get; set; }
    public string? TvgId { get; set; }
    public string? TvgName { get; set; }
    public string? Language { get; set; }
    public ChannelType Type { get; set; } = ChannelType.Live;
    public bool IsFavorite { get; set; }
    public DateTime? LastWatched { get; set; }
    public int PlaylistId { get; set; }
    
    // Navigation property
    public Playlist? Playlist { get; set; }
}
