using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IPTVPlayer.Models;

public class WatchHistory
{
    [Key]
    public int Id { get; set; }

    public int ProfileId { get; set; }
    public Profile? Profile { get; set; }

    public int? ChannelId { get; set; }
    public Channel? Channel { get; set; }

    public int? EpisodeId { get; set; }
    public Episode? Episode { get; set; }

    public DateTime WatchedAt { get; set; }
    public TimeSpan WatchedDuration { get; set; }
    public TimeSpan StoppedAt { get; set; }
    public bool Completed { get; set; }

    [NotMapped]
    public string? Title => Episode?.Name ?? Channel?.Name;
}
