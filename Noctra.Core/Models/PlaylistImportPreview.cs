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

    public string ToSummaryText()
    {
        if (!IsValid)
        {
            return $"Dogrulama basarisiz: {ErrorMessage}";
        }

        var duplicateSuffix = DuplicateNameCount > 0 || DuplicateStreamUrlCount > 0
            ? $" | Tekrar: isim {DuplicateNameCount}, URL {DuplicateStreamUrlCount}"
            : string.Empty;

        var existingSuffix = HasExistingAccountDuplicate
            ? " | Uyari: Bu baglantiyi kullanan baska hesap mevcut"
            : string.Empty;

        return $"{SourceType} Onizleme | Toplam {TotalChannels} kanal | Canli {LiveCount} | VOD {VodCount} | Dizi {SeriesCount} | Kategori {CategoryCount}{duplicateSuffix}{existingSuffix}";
    }

    public static PlaylistImportPreview FromChannels(
        IReadOnlyCollection<Channel> channels,
        string sourceType,
        bool hasExistingAccountDuplicate)
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
            HasExistingAccountDuplicate = hasExistingAccountDuplicate
        };
    }

    public static PlaylistImportPreview Invalid(string sourceType, string errorMessage)
    {
        return new PlaylistImportPreview
        {
            IsValid = false,
            SourceType = sourceType,
            ErrorMessage = errorMessage
        };
    }
}

