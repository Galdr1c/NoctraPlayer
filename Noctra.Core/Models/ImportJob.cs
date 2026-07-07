namespace Noctra.Models;

public enum ImportJobKind
{
    M3U = 0,
    Xtream = 1,
    Stalker = 2,
    PlaylistRefresh = 3
}

public enum ImportJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4
}

public class ImportJob
{
    public int Id { get; set; }
    public int? ProfileId { get; set; }
    public int? PlaylistId { get; set; }
    public ImportJobKind Kind { get; set; }
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;
    public string SourceName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public int LiveCount { get; set; }
    public int VodCount { get; set; }
    public int SeriesCount { get; set; }
    public int FailedCategoryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
