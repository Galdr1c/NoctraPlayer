using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Playlist organizasyon servisi.
/// Pipeline: Dedup → Kategorize → Sırala → Zenginleştir
/// </summary>
public partial class PlaylistOrganizerService : IPlaylistOrganizerService
{
    private static readonly string[] QualityOrder = { "4k", "uhd", "2160p", "1080p", "fhd", "hd", "720p", "sd", "480p" };

    public List<Channel> Organize(List<Channel> channels, bool trustProviderTypes = false)
    {
        if (channels == null || channels.Count == 0)
            return channels ?? new List<Channel>();

        var originalCount = channels.Count;

        var organized = RemoveDuplicates(channels);
        AutoCategorize(organized);
        organized = SmartSort(organized);
        EnrichMetadata(organized);

        System.Diagnostics.Debug.WriteLine(
            $"[PlaylistOrganizer] Pipeline complete: {originalCount} → {organized.Count} channels " +
            $"({originalCount - organized.Count} duplicates removed)");

        return organized;
    }

    /// <summary>
    /// Aynı URL'ye sahip kanalları tekilleştirir, yüksek kaliteliyi tutar.
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
        // Categorization rules have been disabled by request.
        // If a channel lacks a group title, we just mark it as "Uncategorized"
        foreach (var channel in channels)
        {
            if (string.IsNullOrEmpty(channel.GroupTitle) || channel.GroupTitle == "undefined")
            {
                channel.GroupTitle = "Uncategorized";
            }
        }
    }


    /// <summary>
    /// Tutarsız grup isimlerini standart Türkçe isimlere dönüştürür
    /// </summary>
    public void NormalizeGroupNames(List<Channel> channels)
    {
        // Normalization has been disabled. Categories will be preserved exactly as provided.
    }

    /// <summary>
    /// Tip → Adult → Grup → Kanal numarası → Alfabetik sıralama
    /// </summary>
    public List<Channel> SmartSort(List<Channel> channels)
    {
        return channels
            .OrderBy(c => c.Type) // Live -> VOD -> Series
            .ThenBy(c => IsAdultContent(c.GroupTitle) ? 1 : 0) // Adult kategoriler en sona
            .ThenBy(c => c.GroupTitle ?? "zzz") // Alfabetik grup (Uncategorized sonlarda)
            .ThenBy(c => GetChannelNumber(c.Name) ?? int.MaxValue) // Numaralı kanallar öne
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase) // Alfabetik kanal adı
            .ToList();
    }

    /// <summary>
    /// Boş TvgId'leri kanal adından türetir.
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
    // HELPERS
    // ────────────────────────────────────────────────

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

    private static bool IsHigherQuality(Channel a, Channel b)
    {
        // 1. Tip önceliği (Dizi/VOD, Live'dan daha değerlidir eğer bunlar duplicate ise)
        if (a.Type != b.Type)
        {
            if (a.Type == ChannelType.Live && IsLinearStreamUrl(a.StreamUrl) && b.Type != ChannelType.Live) return true;
            if (b.Type == ChannelType.Live && IsLinearStreamUrl(b.StreamUrl) && a.Type != ChannelType.Live) return false;
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

    private static bool IsLinearStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        var lowerUrl = streamUrl.Trim().ToLowerInvariant();
        if (lowerUrl.Contains("/movie/") ||
            lowerUrl.Contains("/vod/") ||
            lowerUrl.Contains("/series/") ||
            lowerUrl.Contains("/tv_show/") ||
            lowerUrl.Contains("type=vod") ||
            lowerUrl.Contains("type=movie") ||
            lowerUrl.Contains("type=series"))
        {
            return false;
        }

        var path = lowerUrl;
        var q = path.IndexOf('?');
        if (q >= 0)
        {
            path = path[..q];
        }

        return path.EndsWith(".m3u8") ||
               path.EndsWith("/m3u8") ||   // proxy path segment
               path.EndsWith(".ts") ||
               path.EndsWith("/ts") ||     // proxy path segment — en yaygın canlı TV göstergesi
               path.EndsWith(".m3u") ||
               path.EndsWith("/m3u") ||
               lowerUrl.Contains("format=m3u8") ||
               lowerUrl.Contains("extension=m3u8") ||
               lowerUrl.Contains("extension=ts");
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

    private static bool IsAdultContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return AdultContentRegex().IsMatch(text);
    }

    private static string GenerateEpgId(string name)
    {
        // Use central cleaner to get a consistent base name
        var clean = SeriesInfoParser.CleanSeriesName(name);

        // PascalCase: "Show TV" → "ShowTV"  
        return string.Concat(clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"(?<!\S)(\d{1,4})(?!\S)")]
    private static partial Regex ChannelNumberRegex();

    [GeneratedRegex(@"\b(4k|uhd|2160p|1080p|fhd|hd|720p|sd|480p)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityTagRegex();

    [GeneratedRegex(@"(?:\b|_)(adult|xxx|porn|sexy|18\+| \+18|pink|redlight|erotik|erotic|lust|hentai|brazzers|bangbros|babes|realitykings|digitalplayground|naughtyamerica|passion|penthouse|hustler|playboy|blue movie|hardcore|softcore|x-rated|sex|cam|strip|fetish|bondage|bdsm|amateur|milf|gay|lesbian|pornstar|yetişkin|mature)(?:\b|_)", RegexOptions.IgnoreCase)]
    private static partial Regex AdultContentRegex();
}
