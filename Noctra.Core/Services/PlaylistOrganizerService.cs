using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Otomatik playlist organizasyon servisi
/// 5 aşamalı pipeline: Dedup → Kategorize → Normalize → Sırala → Zenginleştir
/// </summary>
public partial class PlaylistOrganizerService : IPlaylistOrganizerService
{
    // Quality tiers (lower index = higher quality)
    private static readonly string[] QualityOrder = { "4k", "uhd", "2160p", "1080p", "fhd", "hd", "720p", "sd", "480p" };

    // Category detection rules
    private static readonly Dictionary<string, string[]> CategoryRules = new()
    {
        ["Spor"] = ["sport", "futbol", "basketbol", "bein", "espn", "eurosport", "s sport", "tivibu spor", "nba", "premier league"],
        ["Haber"] = ["news", "haber", "cnn", "bbc news", "nbc", "fox news", "ntv", "haberturk", "tgrt haber", "a haber"],
        ["Çocuk"] = ["kids", "çocuk", "cartoon", "disney", "nickelodeon", "baby", "minika", "trt çocuk"],
        ["Film"] = ["movie", "film", "sinema", "cinema", "box office"],
        ["Dizi"] = ["series", "dizi", "tv show", "kanal d", "show tv", "star tv", "atv"],
        ["Belgesel"] = ["documentary", "belgesel", "discovery", "nat geo", "national geographic", "animal planet", "history"],
        ["Müzik"] = ["music", "müzik", "mtv", "vevo", "kral", "power"],
        ["Eğlence"] = ["entertainment", "eğlence", "show", "komedi", "comedy"]
    };

    // Group name normalization map
    private static readonly Dictionary<string, string> GroupMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Turkish variations
        ["SPOR"] = "Spor",
        ["Sports"] = "Spor",
        ["SPORTS"] = "Spor",
        ["Sport"] = "Spor",

        ["HABER"] = "Haber",
        ["News"] = "Haber",
        ["NEWS"] = "Haber",

        ["ÇOCUK"] = "Çocuk",
        ["Kids"] = "Çocuk",
        ["KIDS"] = "Çocuk",
        ["Children"] = "Çocuk",

        ["Movies"] = "Filmler",
        ["MOVIES"] = "Filmler",
        ["Film"] = "Filmler",
        ["FILM"] = "Filmler",
        ["FİLM"] = "Filmler",
        ["Sinema"] = "Filmler",
        ["SINEMA"] = "Filmler",

        ["Series"] = "Diziler",
        ["SERIES"] = "Diziler",
        ["TV SHOWS"] = "Diziler",
        ["Tv Shows"] = "Diziler",
        ["Dizi"] = "Diziler",
        ["DİZİ"] = "Diziler",

        ["Documentary"] = "Belgesel",
        ["DOCUMENTARY"] = "Belgesel",
        ["BELGESEL"] = "Belgesel",

        ["Music"] = "Müzik",
        ["MUSIC"] = "Müzik",
        ["MÜZİK"] = "Müzik",

        ["Entertainment"] = "Eğlence",
        ["ENTERTAINMENT"] = "Eğlence",
        ["EĞLENCE"] = "Eğlence",

        ["General"] = "Genel",
        ["GENERAL"] = "Genel",
        ["Uncategorized"] = "Genel",
        ["UNCATEGORIZED"] = "Genel",
        ["undefined"] = "Genel",
        [""] = "Genel"
    };

    /// <summary>
    /// Tam organizasyon pipeline'ı
    /// </summary>
    public List<Channel> Organize(List<Channel> channels)
    {
        if (channels == null || channels.Count == 0)
            return channels ?? new List<Channel>();

        var originalCount = channels.Count;

        // Stage 1: Remove duplicates (keeps highest quality)
        var organized = RemoveDuplicates(channels);

        // Stage 2: Auto-categorize uncategorized channels
        AutoCategorize(organized);

        // Stage 3: Normalize group names
        NormalizeGroupNames(organized);

        // Stage 4: Smart sort
        organized = SmartSort(organized);

        // Stage 5: Enrich metadata (TvgId generation)
        EnrichMetadata(organized);

        System.Diagnostics.Debug.WriteLine(
            $"[PlaylistOrganizer] Pipeline complete: {originalCount} → {organized.Count} channels " +
            $"({originalCount - organized.Count} duplicates removed)");

        return organized;
    }

    /// <summary>
    /// Similarity key ile duplicate tespiti, yüksek kaliteyi tercih eder
    /// </summary>
    public List<Channel> RemoveDuplicates(List<Channel> channels)
    {
        var uniqueChannels = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            var key = GenerateSimilarityKey(channel);

            if (string.IsNullOrEmpty(key))
            {
                // Empty key — keep as-is (edge case)
                uniqueChannels[Guid.NewGuid().ToString()] = channel;
                continue;
            }

            if (!uniqueChannels.TryGetValue(key, out var existing))
            {
                uniqueChannels[key] = channel;
            }
            else
            {
                // Keep the higher quality version
                if (IsHigherQuality(channel, existing))
                {
                    uniqueChannels[key] = channel;
                }
            }
        }

        return uniqueChannels.Values.ToList();
    }

    /// <summary>
    /// Kategorisiz kanalları anahtar kelime bazlı kategorize eder
    /// </summary>
    public void AutoCategorize(List<Channel> channels)
    {
        foreach (var channel in channels)
        {
            if (!string.IsNullOrEmpty(channel.GroupTitle) &&
                channel.GroupTitle != "Uncategorized" &&
                channel.GroupTitle != "undefined")
            {
                continue; // Already categorized
            }

            var nameLower = channel.Name.ToLowerInvariant();

            var matched = false;
            foreach (var (category, keywords) in CategoryRules)
            {
                if (keywords.Any(kw => nameLower.Contains(kw)))
                {
                    channel.GroupTitle = category;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                channel.GroupTitle = "Genel";
            }
        }
    }

    /// <summary>
    /// Tutarsız grup isimlerini standart Türkçe isimlere dönüştürür
    /// </summary>
    public void NormalizeGroupNames(List<Channel> channels)
    {
        foreach (var channel in channels)
        {
            var group = channel.GroupTitle?.Trim() ?? "";

            if (GroupMapping.TryGetValue(group, out var normalized))
            {
                channel.GroupTitle = normalized;
            }
        }
    }

    /// <summary>
    /// Grup → Kanal numarası → Alfabetik sıralama
    /// </summary>
    public List<Channel> SmartSort(List<Channel> channels)
    {
        return channels
            .OrderBy(c => c.GroupTitle ?? "zzz") // Uncategorized last
            .ThenBy(c => GetChannelNumber(c.Name) ?? int.MaxValue) // Numbered channels first
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase) // Alphabetical
            .ToList();
    }

    /// <summary>
    /// Eksik TvgId'leri kanal adından türetir
    /// </summary>
    private static void EnrichMetadata(List<Channel> channels)
    {
        foreach (var channel in channels)
        {
            if (string.IsNullOrEmpty(channel.TvgId))
            {
                channel.TvgId = GenerateEpgId(channel.Name);
            }
        }
    }

    // ────────────────────────────────────────────────
    // HELPER METHODS
    // ────────────────────────────────────────────────

    /// <summary>
    /// Kanal adından benzerlik anahtarı üretir
    /// "TRT 1 HD FHD 1080p" → "trt1"
    /// </summary>
    private static string GenerateSimilarityKey(Channel channel)
    {
        var key = SeriesInfoParser.NormalizeKey(channel.Name);
        if (channel.Type == ChannelType.Series)
        {
            var parsed = SeriesInfoParser.Parse(channel.Name);
            key += $" s{parsed.Season:00}e{parsed.Episode:00}";
        }
        return key;
    }

    /// <summary>
    /// a'nın b'den yüksek kaliteli olup olmadığını kontrol eder
    /// </summary>
    private static bool IsHigherQuality(Channel a, Channel b)
    {
        var aIndex = GetQualityIndex(a.Name);
        var bIndex = GetQualityIndex(b.Name);
        return aIndex < bIndex; // Lower index = higher quality
    }

    private static int GetQualityIndex(string name)
    {
        var lower = name.ToLowerInvariant();
        for (int i = 0; i < QualityOrder.Length; i++)
        {
            if (lower.Contains(QualityOrder[i]))
                return i;
        }
        return QualityOrder.Length; // No quality tag = lowest priority
    }

    /// <summary>
    /// Kanal adından numara çıkarır (TRT 1 → 1, CNN → null)
    /// </summary>
    private static int? GetChannelNumber(string name)
    {
        var match = ChannelNumberRegex().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value;
        if (int.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        if (long.TryParse(raw, out var parsedLong))
        {
            return parsedLong > int.MaxValue ? int.MaxValue : (int)parsedLong;
        }

        return null;
    }

    /// <summary>
    /// Kanal adından EPG ID üretir
    /// "|TR| Show TV HD" → "ShowTV"
    /// </summary>
    private static string GenerateEpgId(string name)
    {
        // Use central cleaner to get a consistent base name
        var clean = SeriesInfoParser.CleanSeriesName(name);

        // PascalCase: "Show TV" → "ShowTV"  
        return string.Concat(clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Source-generated regexes for performance
    [GeneratedRegex(@"\b(\d+)\b")]
    private static partial Regex ChannelNumberRegex();
}
