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
        ["Spor"] = ["spor", "sport", "futbol", "basketbol", "bein", "espn", "eurosport", "s sport", "tivibu spor", "nba", "premier league"],
        ["Haber"] = ["news", "haber", "cnn", "bbc news", "nbc", "fox news", "ntv", "haberturk", "tgrt haber", "a haber"],
        ["Çocuk"] = ["kids", "çocuk", "cartoon", "disney", "nickelodeon", "baby", "minika", "trt çocuk"],
        ["Filmler"] = ["movie", "film", "sinema", "cinema", "box office"],
        ["Diziler"] = ["series", "dizi", "tv show", "kanal d", "show tv", "star tv", "atv"],
        ["Belgesel"] = ["documentary", "belgesel", "discovery", "nat geo", "national geographic", "animal planet", "history"],
        ["Müzik"] = ["music", "müzik", "mtv", "vevo", "kral", "power"],
        ["Eğlence"] = ["entertainment", "eğlence", "show", "komedi", "comedy"]
    };

    // Group name normalization map
    private static readonly Dictionary<string, string> GroupMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Turkish variations
        ["Spor"] = "Spor",
        ["Sports"] = "Spor",
        ["Sport"] = "Spor",

        ["Haber"] = "Haber",
        ["News"] = "Haber",

        ["Çocuk"] = "Çocuk",
        ["Kids"] = "Çocuk",
        ["Children"] = "Çocuk",

        ["Movies"] = "Filmler",
        ["Film"] = "Filmler",
        ["Sinema"] = "Filmler",
        ["Films"] = "Filmler",

        ["Series"] = "Diziler",
        ["Tv Shows"] = "Diziler",
        ["Dizi"] = "Diziler",

        ["Documentary"] = "Belgesel",
        ["Belgesel"] = "Belgesel",

        ["Music"] = "Müzik",
        ["Müzik"] = "Müzik",

        ["Entertainment"] = "Eğlence",
        ["Eğlence"] = "Eğlence",

        ["General"] = "Uncategorized",
        ["Genel"] = "Uncategorized",
        ["Uncategorized"] = "Uncategorized",
        ["undefined"] = "Uncategorized",
        [""] = "Uncategorized"
    };

    /// <summary>
    /// Tam organizasyon pipeline'ı
    /// </summary>
    public List<Channel> Organize(List<Channel> channels)
    {
        if (channels == null || channels.Count == 0)
            return channels ?? new List<Channel>();

        var originalCount = channels.Count;

        // Stage 1: Fix misplaced Live channels (Series appearing as Live) before deduplication
        FixChannelTypes(channels);

        // Stage 2: Remove duplicates (keeps highest quality)
        var organized = RemoveDuplicates(channels);

        // Stage 3: Auto-categorize uncategorized channels
        AutoCategorize(organized);

        // Stage 4: Normalize group names
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

            var nameLower = channel.Name?.ToLowerInvariant() ?? string.Empty;

            var matched = false;
            foreach (var (category, keywords) in CategoryRules)
            {
                if (keywords.Any(kw => nameLower.Contains(kw)))
                {
                    // Prevent Live channels from being categorized as Filmler based on name
                    // (Filmler is usually reserved for VOD, while Diziler includes major TV channels)
                    if (channel.Type == Models.ChannelType.Live && category == "Filmler")
                    {
                        continue;
                    }

                    channel.GroupTitle = category;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                channel.GroupTitle = "Uncategorized";
            }
        }
    }

    /// <summary>
    /// Canlı TV kategorilerine yanlışlıkla karışmış dizi (Series) gruplarını tespit edip tipini düzeltir.
    /// </summary>
    public void FixChannelTypes(List<Channel> channels)
    {
        var groups = channels.GroupBy(c => c.GroupTitle ?? "Uncategorized").ToList();
        foreach (var group in groups)
        {
            var isLiveGroup = group.Any(c => c.Type == ChannelType.Live);
            if (!isLiveGroup) continue;

            // Kategori ismi bazlı tespit (Daha güvenilir bir işaret)
            var groupName = group.Key;
            
            // (S| pattern i Xtream series için çok spesifiktir, doğrudan kabul et.
            var hasSeriesMarker = groupName.Contains("(S|", StringComparison.OrdinalIgnoreCase);
            
            // "Series" anahtar kelimesi ise ambiguous olabilir, Live TV belirteçlerini kontrol et.
            var hasSeriesKeywords = groupName.Contains("Series", StringComparison.OrdinalIgnoreCase) || 
                                     groupName.Contains("Série", StringComparison.OrdinalIgnoreCase) ||
                                     groupName.Contains("Dizi", StringComparison.OrdinalIgnoreCase) ||
                                     groupName.Contains("Bölüm", StringComparison.OrdinalIgnoreCase);

            // "Koleksiyon" veya doğrudan dizi arşivi belirteçleri (BEIN DİZİLER vb. durumlar için)
            var isStrongSeriesCategory = groupName.Contains("DİZİLER", StringComparison.OrdinalIgnoreCase) || 
                                         groupName.Contains("KOLEKSİYON", StringComparison.OrdinalIgnoreCase);

            var hasLiveKeywords = groupName.Contains("SPOR", StringComparison.OrdinalIgnoreCase) || 
                                   groupName.Contains("SPORT", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("HABER", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("NEWS", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("RADIO", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("24/7", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("CANLI", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("LIVE", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("|", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("✅", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("FHD", StringComparison.OrdinalIgnoreCase) ||
                                   groupName.Contains("4K", StringComparison.OrdinalIgnoreCase) ||
                                   (groupName.Contains(" - ", StringComparison.OrdinalIgnoreCase) && !hasSeriesMarker); // TR - SERIES gibi durumlar genellikle Live'dır.

            // Eğer çok güçlü bir Dizi kategorisi ismiyse (MAX DİZİLER gibi), Live keyword'leri olsa bile dizi kabul et.
            var isSeriesGroupByName = hasSeriesMarker || isStrongSeriesCategory || (hasSeriesKeywords && !hasLiveKeywords);

            if (isSeriesGroupByName)
            {
                foreach (var channel in group)
                {
                    channel.Type = ChannelType.Series;
                }
                continue;
            }

            // Dizi formatına uygun olan kanal sayısını hesapla
            int seriesVotes = 0;
            int total = 0;
            foreach (var channel in group)
            {
                total++;
                if (SeriesInfoParser.IsSeries(channel.Name))
                {
                    seriesVotes++;
                }
            }

            // Gruptaki içeriklerin %60'ından fazlası S01E01 vb. dizi formatındaysa bu bir Dizi grubudur
            if (total > 0 && (double)seriesVotes / total > 0.6)
            {
                foreach (var channel in group)
                {
                    channel.Type = ChannelType.Series;
                }
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
    /// Tip → Grup → Kanal numarası → Alfabetik sıralama
    /// </summary>
    public List<Channel> SmartSort(List<Channel> channels)
    {
        return channels
            .OrderBy(c => c.Type) // Live -> VOD -> Series (or based on enum order)
            .ThenBy(c => c.GroupTitle ?? "zzz") // Uncategorized last
            .ThenBy(c => GetChannelNumber(c.Name) ?? int.MaxValue) // Numbered channels first
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase) // Alphabetical
            .ToList();
    }

    /// <summary>
    /// Eksik TvgId'leri kanal adından türetir
    /// </summary>
    public void EnrichMetadata(List<Channel> channels)
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
    /// Kanal adından ve grubundan benzerlik anahtarı üretir
    /// </summary>
    private static string GenerateSimilarityKey(Channel channel)
    {
        // Temel anahtar: Temizlenmiş ve normalize edilmiş kanal adı
        var key = SeriesInfoParser.NormalizeKey(channel.Name);
        
        // Eğer StreamUrl varsa, URL bazlı tekilleştirme en güvenilisidir.
        // Aynı isimde farklı URL'ler farklı yayınları temsil edebilir (farklı diller, farklı kaynaklar).
        // Ancak aynı URL'ye sahip içerikler kesinlikle aynıdır.
        if (!string.IsNullOrEmpty(channel.StreamUrl))
        {
            key += $"|url|{channel.StreamUrl.Trim().ToLowerInvariant()}";
        }
        else if (!string.IsNullOrEmpty(channel.GroupTitle))
        {
            // URL yoksa grup bilgisini de ekleyerek farklı dillerdeki aynı isimli yayınları koru
            key += $"|{NormalizeIdentityToken(channel.GroupTitle)}";
        }

        if (channel.Type == ChannelType.Series)
        {
            var parsed = SeriesInfoParser.Parse(channel.Name);
            if (parsed.Season > 0 || parsed.Episode > 0)
            {
                key += $" s{parsed.Season:00}e{parsed.Episode:00}";
            }
        }
        return key;
    }

    private static string NormalizeIdentityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return string.Join(" ", raw.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// a'nın b'den yüksek kaliteli olup olmadığını kontrol eder
    /// </summary>
    private static bool IsHigherQuality(Channel a, Channel b)
    {
        // 1. Tip önceliği (Dizi/VOD, Live'dan daha değerlidir eğer bunlar duplicate ise)
        if (a.Type != b.Type)
        {
            if (a.Type == ChannelType.Series && b.Type != ChannelType.Series) return true;
            if (a.Type == ChannelType.VOD && b.Type == ChannelType.Live) return true;
        }

        // 2. Çözünürlük/Kalite tagi önceliği
        var aIndex = GetQualityIndex(a.Name);
        var bIndex = GetQualityIndex(b.Name);
        
        if (aIndex < bIndex) return true;
        if (aIndex > bIndex) return false;

        // 3. Tarih önceliği (Daha yeni eklenen veya güncellenen daha iyidir)
        return a.Id > b.Id;
    }

    private static int GetQualityIndex(string name)
    {
        var matches = QualityTagRegex().Matches(name);
        if (matches.Count > 0)
        {
            int bestIndex = QualityOrder.Length;
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var tag = match.Value.ToLowerInvariant();
                int idx = Array.IndexOf(QualityOrder, tag);
                if (idx != -1 && idx < bestIndex)
                {
                    bestIndex = idx;
                }
            }
            return bestIndex;
        }
        return QualityOrder.Length; // No quality tag = lowest priority
    }

    /// <summary>
    /// Kanal adından numara çıkarır (TRT 1 → 1, CNN → null)
    /// </summary>
    private static int? GetChannelNumber(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Avoid extracting numbers from URLs or IPs (e.g. http://192.168.1.1)
        if (name.Contains("://"))
        {
            return null;
        }

        var match = ChannelNumberRegex().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value;
        if (int.TryParse(raw, out var parsed))
        {
            // Avoid large numbers that are likely years (e.g. 2024) or garbage, cap at 9999
            if (parsed > 0 && parsed <= 9999)
            {
                // Optional: check if the number is part of a year (e.g. 1990 - 2030)
                // If it is a year and not at the end of the string, it's likely not a channel number.
                if (parsed >= 1900 && parsed <= 2030 && !name.EndsWith(raw))
                {
                    return null;
                }
                
                return parsed;
            }
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
    [GeneratedRegex(@"(?<!\S)(\d{1,4})(?!\S)")]
    private static partial Regex ChannelNumberRegex();

    [GeneratedRegex(@"\b(4k|uhd|2160p|1080p|fhd|hd|720p|sd|480p)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityTagRegex();
}
