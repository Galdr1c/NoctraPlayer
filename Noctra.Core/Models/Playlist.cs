namespace Noctra.Models;

/// <summary>
/// M3U playlist bilgilerini tutar
/// </summary>
public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastUpdated { get; set; }
    public bool IsActive { get; set; } = true;
    public int ChannelCount { get; set; }

    // EPG fields
    public string? EpgUrl { get; set; }
    public string? DetectedCountry { get; set; }
    public DateTime? EpgLastUpdated { get; set; }
    public string? EpgLastError { get; set; }

    public int? ProfileId { get; set; }
    public Profile? Profile { get; set; }
    
    // Navigation property
    public ICollection<Channel> Channels { get; set; } = new List<Channel>();
}

