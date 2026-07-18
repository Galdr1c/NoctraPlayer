namespace Noctra.Models;

/// <summary>
/// M3U playlist bilgilerini tutar
/// </summary>
public class Playlist
{
    public const int CurrentChannelTypeRepairVersion = 1;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }
    public bool IsActive { get; set; } = true;
    public int ChannelCount { get; set; }

    // EPG fields
    public string? EpgUrl { get; set; }
    public string? DetectedCountry { get; set; }
    public DateTime? EpgLastUpdated { get; set; }
    public string? EpgLastError { get; set; }

    // Source metadata cache for cheap "unchanged" checks before full M3U download.
    public string? SourceEtag { get; set; }
    public DateTime? SourceLastModified { get; set; }
    public long? SourceContentLength { get; set; }

    // Persisted compatibility-maintenance version. New imports are already classified
    // by the current parser; legacy database rows receive zero from schema fixup.
    public int ChannelTypeRepairVersion { get; set; } = CurrentChannelTypeRepairVersion;

    public int? ProfileId { get; set; }
    public Profile? Profile { get; set; }
    
    // Navigation property
    public ICollection<Channel> Channels { get; set; } = new List<Channel>();
}
