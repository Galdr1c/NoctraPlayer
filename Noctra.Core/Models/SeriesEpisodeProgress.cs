using System.ComponentModel.DataAnnotations;

namespace Noctra.Models;

/// <summary>
/// Provider'dan bagimsiz dizi bolum ilerleme kaydi.
/// Anahtar: Profile + SeriesKey + SeasonNumber + EpisodeNumber.
/// </summary>
public class SeriesEpisodeProgress
{
    [Key]
    public int Id { get; set; }

    public int ProfileId { get; set; }
    public Profile? Profile { get; set; }

    [MaxLength(512)]
    public string SeriesKey { get; set; } = string.Empty;

    [MaxLength(255)]
    public string SeriesTitle { get; set; } = string.Empty;

    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }

    public DateTime LastWatchedAt { get; set; }
    public TimeSpan StoppedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool Completed { get; set; }
}
