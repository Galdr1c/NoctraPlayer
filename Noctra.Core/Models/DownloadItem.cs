using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

public class DownloadItem : INotifyPropertyChanged
{
    private DownloadStatus _status = DownloadStatus.Queued;
    private long _bytesDownloaded;
    private long? _bytesTotal;
    private double _speedBytesPerSecond;
    private int? _estimatedSecondsRemaining;
    private string? _errorMessage;
    private int _queueOrder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int PlaylistId { get; set; }
    public int? ChannelId { get; set; }
    public int? EpisodeId { get; set; }
    public int? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? EpisodeTitle { get; set; }
    public string? ContentKey { get; set; }
    public ChannelType ChannelType { get; set; } = ChannelType.VOD;
    public string DisplayName { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Original remote poster URL captured when the download was queued.
    /// Unlike <see cref="PosterUrl"/> it is never overwritten with the local
    /// poster path, so it can be restored onto mapped entities when the
    /// download is deleted.
    /// </summary>
    public string? SourcePosterUrl { get; set; }

    public string SourceUrl { get; set; } = string.Empty;
    public string? LocalFilePath { get; set; }
    public string? TempFilePath { get; set; }
    public string? AudioTracksJson { get; set; }
    public string? SubtitleTracksJson { get; set; }
    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (!SetField(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsQueued));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public long BytesDownloaded
    {
        get => _bytesDownloaded;
        set
        {
            if (!SetField(ref _bytesDownloaded, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public long? BytesTotal
    {
        get => _bytesTotal;
        set
        {
            if (!SetField(ref _bytesTotal, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set
        {
            if (!SetField(ref _speedBytesPerSecond, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SpeedText));
        }
    }

    public int? EstimatedSecondsRemaining
    {
        get => _estimatedSecondsRemaining;
        set
        {
            if (!SetField(ref _estimatedSecondsRemaining, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EtaText));
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (!SetField(ref _errorMessage, value))
            {
                return;
            }

            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(StatusText));
        }
    }
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
    public int QueueOrder
    {
        get => _queueOrder;
        set => SetField(ref _queueOrder, value);
    }

    [NotMapped]
    public string StatusText
    {
        get
        {
            // Neutral fallback; UI renders localized text via converters.
            var text = Status switch
            {
                DownloadStatus.Queued => "Queued",
                DownloadStatus.Downloading => "Downloading",
                DownloadStatus.Paused => "Paused",
                DownloadStatus.Completed => "Completed",
                DownloadStatus.Failed => "Failed",
                DownloadStatus.Canceled => "Canceled",
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

            return $"{FormatBytes((long)SpeedBytesPerSecond)}/s";
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

            // Neutral fallback; UI renders localized text via converters.
            var ts = TimeSpan.FromSeconds(EstimatedSecondsRemaining.Value);
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
            }

            if (ts.TotalMinutes >= 1)
            {
                return $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s";
            }

            return $"{ts.Seconds}s";
        }
    }

    [NotMapped]
    public string BaseDisplayName
    {
        get
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? string.Empty : DisplayName;
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

    internal static string FormatBytes(long bytes)
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        if (!string.IsNullOrEmpty(propertyName))
        {
            OnPropertyChanged(propertyName);
        }

        return true;
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
