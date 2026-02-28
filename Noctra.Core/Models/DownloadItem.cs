using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Noctra.Services;

namespace Noctra.Models;

public enum DownloadStatus
{
    Queued = 0,
    Downloading = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5
}

public class DownloadItem
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int PlaylistId { get; set; }
    public int? ChannelId { get; set; }
    public int? EpisodeId { get; set; }
    public ChannelType ChannelType { get; set; } = ChannelType.VOD;
    public string DisplayName { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string? LocalFilePath { get; set; }
    public string? TempFilePath { get; set; }
    public string? AudioTracksJson { get; set; }
    public string? SubtitleTracksJson { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public long BytesDownloaded { get; set; }
    public long? BytesTotal { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public int? EstimatedSecondsRemaining { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    [NotMapped]
    public bool IsActive =>
        Status == DownloadStatus.Queued ||
        Status == DownloadStatus.Downloading ||
        Status == DownloadStatus.Paused;

    [NotMapped]
    public bool IsCompleted => Status == DownloadStatus.Completed;

    [NotMapped]
    public bool IsPaused => Status == DownloadStatus.Paused;

    [NotMapped]
    public bool IsQueued => Status == DownloadStatus.Queued;

    [NotMapped]
    public int QueueOrder { get; set; }

    [NotMapped]
    public string StatusText
    {
        get
        {
            var text = Status switch
            {
                DownloadStatus.Queued => "Kuyrukta",
                DownloadStatus.Downloading => "Indiriliyor",
                DownloadStatus.Paused => "Duraklatildi",
                DownloadStatus.Completed => "Tamamlandi",
                DownloadStatus.Failed => "Hatali",
                DownloadStatus.Canceled => "Iptal",
                _ => "-"
            };

            if ((Status == DownloadStatus.Failed || Status == DownloadStatus.Paused) && !string.IsNullOrWhiteSpace(ErrorMessage))
            {
                return $"{text} - {ErrorMessage}";
            }

            return text;
        }
    }

    [NotMapped]
    public double ProgressPercent
    {
        get
        {
            if (!BytesTotal.HasValue || BytesTotal.Value <= 0)
            {
                return 0;
            }

            var percent = (BytesDownloaded / (double)BytesTotal.Value) * 100.0;
            return Math.Clamp(percent, 0, 100);
        }
    }

    [NotMapped]
    public string ProgressText => $"{ProgressPercent:0.#}%";

    [NotMapped]
    public string SpeedText
    {
        get
        {
            if (SpeedBytesPerSecond <= 0)
            {
                return "-";
            }

            return $"{FormatBytes((long)SpeedBytesPerSecond)}/sn";
        }
    }

    [NotMapped]
    public string SizeText
    {
        get
        {
            if (!BytesTotal.HasValue || BytesTotal.Value <= 0)
            {
                return FormatBytes(BytesDownloaded);
            }

            return $"{FormatBytes(BytesDownloaded)} / {FormatBytes(BytesTotal.Value)}";
        }
    }

    [NotMapped]
    public string EtaText
    {
        get
        {
            if (!EstimatedSecondsRemaining.HasValue || EstimatedSecondsRemaining.Value <= 0)
            {
                return "-";
            }

            var ts = TimeSpan.FromSeconds(EstimatedSecondsRemaining.Value);
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}sa {ts.Minutes:00}dk";
            }

            if (ts.TotalMinutes >= 1)
            {
                return $"{(int)ts.TotalMinutes}dk {ts.Seconds:00}sn";
            }

            return $"{ts.Seconds}sn";
        }
    }

    [NotMapped]
    public string BaseDisplayName
    {
        get
        {
            if (ChannelType != ChannelType.Series || string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            var info = SeriesInfoParser.Parse(DisplayName);
            return info.SeriesName;
        }
    }

    [NotMapped]
    public string? SeriesInfoText
    {
        get
        {
            if (ChannelType != ChannelType.Series || string.IsNullOrWhiteSpace(DisplayName))
            {
                return null;
            }

            var info = SeriesInfoParser.Parse(DisplayName);
            return SeriesInfoParser.GetSeriesInfoText(info.Season, info.Episode);
        }
    }

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {Units[unitIndex]}";
    }
}
