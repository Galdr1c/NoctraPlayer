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

    public string ToSummaryText()
    {
        if (!IsValid)
        {
            var statusPart = StatusCode.HasValue ? $" (Kod: {StatusCode})" : "";
            return $"Dogrulama basarisiz: {ErrorMessage}{statusPart}";
        }

        var healthText = Health switch
        {
            ConnectionHealth.Good => "Mukemmel",
            ConnectionHealth.Weak => "Orta",
            ConnectionHealth.Bad => "Kotu",
            _ => "Bilinmiyor"
        };
        
        var latencyPart = LatencyMs.HasValue ? $" | Gecikme: {LatencyMs}ms ({healthText})" : "";

        var duplicateSuffix = DuplicateNameCount > 0 || DuplicateStreamUrlCount > 0
            ? $" | Yinelenen Kanal: isim {DuplicateNameCount}, URL {DuplicateStreamUrlCount}"
            : string.Empty;

        var existingSuffix = HasExistingAccountDuplicate
            ? " | Uyari: Bu baglantiyi kullanan baska hesap mevcut"
            : string.Empty;

        return $"{SourceType} Onizleme | Toplam {TotalChannels} kanal | Canli {LiveCount} | VOD {VodCount} | Dizi {SeriesCount} | Kategori {CategoryCount}{latencyPart}{duplicateSuffix}{existingSuffix}";
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

