using Noctra.Models;

namespace Noctra.Models;

public class PlaylistImportPreview
{
    public bool IsValid { get; init; }
    public string SourceType { get; init; } = "Unknown";
    public int TotalChannels { get; init; }
    public int LiveCount { get; init; }
    public int VodCount { get; init; }
    public int SeriesCount { get; init; }
    public int CategoryCount { get; init; }
    public int DuplicateNameCount { get; init; }
    public int DuplicateStreamUrlCount { get; init; }
    public bool HasExistingAccountDuplicate { get; init; }
    public string? ErrorMessage { get; init; }

    public ConnectionHealth Health { get; init; } = ConnectionHealth.Unknown;
    public int? StatusCode { get; init; }
    public long? LatencyMs { get; init; }

    public string ToSummaryText(Func<string, string>? localize = null)
    {
        static string Fallback(string key) => key switch
        {
            "PlaylistPreview.ValidationFailedFormat" => "Validation failed: {0}{1}",
            "PlaylistPreview.StatusCodeSuffixFormat" => " (Code: {0})",
            "PlaylistPreview.Health.Good" => "Excellent",
            "PlaylistPreview.Health.Weak" => "Fair",
            "PlaylistPreview.Health.Bad" => "Poor",
            "PlaylistPreview.Health.Unknown" => "Unknown",
            "PlaylistPreview.LatencyFormat" => " | Latency: {0}ms ({1})",
            "PlaylistPreview.DuplicateChannelFormat" => " | Duplicate channels: names {0}, URLs {1}",
            "PlaylistPreview.ExistingAccountWarning" => " | Warning: another account already uses this connection",
            "PlaylistPreview.SummaryFormat" => "{0} preview | Total {1} channels | Live {2} | VOD {3} | Series {4} | Categories {5}{6}{7}{8}",
            _ => key
        };

        var t = localize ?? Fallback;

        if (!IsValid)
        {
            var statusPart = StatusCode.HasValue
                ? string.Format(t("PlaylistPreview.StatusCodeSuffixFormat"), StatusCode)
                : string.Empty;
            return string.Format(t("PlaylistPreview.ValidationFailedFormat"), ErrorMessage, statusPart);
        }

        var healthText = Health switch
        {
            ConnectionHealth.Good => t("PlaylistPreview.Health.Good"),
            ConnectionHealth.Weak => t("PlaylistPreview.Health.Weak"),
            ConnectionHealth.Bad => t("PlaylistPreview.Health.Bad"),
            _ => t("PlaylistPreview.Health.Unknown")
        };
        
        var latencyPart = LatencyMs.HasValue
            ? string.Format(t("PlaylistPreview.LatencyFormat"), LatencyMs, healthText)
            : string.Empty;

        var duplicateSuffix = DuplicateNameCount > 0 || DuplicateStreamUrlCount > 0
            ? string.Format(t("PlaylistPreview.DuplicateChannelFormat"), DuplicateNameCount, DuplicateStreamUrlCount)
            : string.Empty;

        var existingSuffix = HasExistingAccountDuplicate
            ? t("PlaylistPreview.ExistingAccountWarning")
            : string.Empty;

        return string.Format(
            t("PlaylistPreview.SummaryFormat"),
            SourceType,
            TotalChannels,
            LiveCount,
            VodCount,
            SeriesCount,
            CategoryCount,
            latencyPart,
            duplicateSuffix,
            existingSuffix);
    }

    public static PlaylistImportPreview FromChannels(
        IReadOnlyCollection<Channel> channels,
        string sourceType,
        bool hasExistingAccountDuplicate,
        ConnectionHealth health = ConnectionHealth.Unknown,
        long? latencyMs = null,
        int? statusCode = null)
    {
        var nonEmptyNames = channels
            .Select(c => c.Name?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Cast<string>()
            .ToList();

        var nonEmptyUrls = channels
            .Select(c => c.StreamUrl?.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Cast<string>()
            .ToList();

        return new PlaylistImportPreview
        {
            IsValid = channels.Count > 0,
            SourceType = sourceType,
            TotalChannels = channels.Count,
            LiveCount = channels.Count(c => c.Type == ChannelType.Live),
            VodCount = channels.Count(c => c.Type == ChannelType.VOD),
            SeriesCount = channels.Count(c => c.Type == ChannelType.Series),
            CategoryCount = channels
                .Select(c => c.GroupTitle?.Trim())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            DuplicateNameCount = nonEmptyNames
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1),
            DuplicateStreamUrlCount = nonEmptyUrls
                .GroupBy(u => u, StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1),
            HasExistingAccountDuplicate = hasExistingAccountDuplicate,
            Health = health,
            LatencyMs = latencyMs,
            StatusCode = statusCode
        };
    }

    public static PlaylistImportPreview Invalid(string sourceType, string errorMessage, int? statusCode = null)
    {
        return new PlaylistImportPreview
        {
            IsValid = false,
            SourceType = sourceType,
            ErrorMessage = errorMessage,
            Health = ConnectionHealth.Critical,
            StatusCode = statusCode
        };
    }
}

public enum ConnectionHealth
{
    Unknown,
    Good,     // < 300ms, 200 OK
    Weak,     // 300-1000ms, 200 OK
    Bad,      // > 1000ms, 200 OK
    Critical  // Connection Failed / 4xx / 5xx
}
