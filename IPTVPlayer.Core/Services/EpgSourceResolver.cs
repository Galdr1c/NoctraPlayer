namespace IPTVPlayer.Services;

/// <summary>
/// Ülke koduna göre en uygun EPG kaynaklarını belirler
/// Öncelik: Provider EPG → M3U x-tvg-url → iptv-epg.org → epgshare01 → global fallback
/// </summary>
public class EpgSourceResolver
{
    /// <summary>
    /// iptv-epg.org URL kalıbı
    /// </summary>
    private const string IptvEpgOrgTemplate = "https://iptv-epg.org/files/epg-{0}.xml";

    /// <summary>
    /// Global fallback URL (büyük dosya, son çare)
    /// </summary>
    private const string GlobalFallbackUrl = "https://iptv-epg.org/files/epg-all.xml";

    /// <summary>
    /// Desteklenen ülkeler ve özel EPG URL'leri
    /// </summary>
    private static readonly Dictionary<string, string[]> CountryEpgSources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TR"] = new[]
        {
            "https://iptv-epg.org/files/epg-tr.xml"
        },
        ["GB"] = new[]
        {
            "https://iptv-epg.org/files/epg-gb.xml"
        },
        ["US"] = new[]
        {
            "https://iptv-epg.org/files/epg-us.xml"
        },
        ["DE"] = new[]
        {
            "https://iptv-epg.org/files/epg-de.xml"
        },
        ["FR"] = new[]
        {
            "https://iptv-epg.org/files/epg-fr.xml"
        },
        ["IT"] = new[]
        {
            "https://iptv-epg.org/files/epg-it.xml"
        },
        ["ES"] = new[]
        {
            "https://iptv-epg.org/files/epg-es.xml"
        },
        ["NL"] = new[]
        {
            "https://iptv-epg.org/files/epg-nl.xml"
        },
        ["RU"] = new[]
        {
            "https://iptv-epg.org/files/epg-ru.xml"
        },
        ["AR"] = new[]
        {
            "https://iptv-epg.org/files/epg-ar.xml"
        }
    };

    /// <summary>
    /// Verilen parametrelere göre EPG URL listesini öncelik sırasına göre döndürür
    /// </summary>
    /// <param name="countryCode">Tespit edilen ülke kodu (2 harf)</param>
    /// <param name="providerEpgUrl">Provider'ın kendi EPG URL'i (Xtream/Stalker)</param>
    /// <param name="m3uEpgUrl">M3U dosyasındaki x-tvg-url</param>
    /// <returns>Öncelik sırasına göre EPG URL listesi</returns>
    public List<EpgSource> ResolveEpgSources(string countryCode, string? providerEpgUrl = null, string? m3uEpgUrl = null)
    {
        var sources = new List<EpgSource>();

        // 1. Provider EPG (en güvenilir — aynı channel ID'leri kullanır)
        if (!string.IsNullOrWhiteSpace(providerEpgUrl))
        {
            sources.Add(new EpgSource
            {
                Url = providerEpgUrl,
                Priority = 1,
                Type = EpgSourceType.Provider,
                IsPrimary = true // Provider ID'leri M3U TvgId ile eşleşir
            });
        }

        // 2. M3U x-tvg-url
        if (!string.IsNullOrWhiteSpace(m3uEpgUrl))
        {
            sources.Add(new EpgSource
            {
                Url = m3uEpgUrl,
                Priority = 2,
                Type = EpgSourceType.M3UHeader,
                IsPrimary = sources.Count == 0 // Sadece provider yoksa primary
            });
        }

        // 3. iptv-epg.org (ülke bazlı) — HER ZAMAN secondary (isim eşleştirmesi ile)
        var code = countryCode.ToLowerInvariant();
        sources.Add(new EpgSource
        {
            Url = string.Format(IptvEpgOrgTemplate, code),
            Priority = 3,
            Type = EpgSourceType.IptvEpgOrg,
            IsPrimary = false, // ASLA primary değil — farklı channel ID'leri var
            ClearBeforeLoad = sources.Count == 0 // Provider/M3U yoksa önce temizle
        });

        // 4. Global fallback (bilinen ülkelerin özel URL'leri yoksa)
        if (!CountryEpgSources.ContainsKey(countryCode))
        {
            sources.Add(new EpgSource
            {
                Url = GlobalFallbackUrl,
                Priority = 4,
                Type = EpgSourceType.GlobalFallback,
                IsPrimary = false
            });
        }

        return sources.OrderBy(s => s.Priority).ToList();
    }

    /// <summary>
    /// Bilinen ülke EPG URL'lerini doğrudan döndürür
    /// </summary>
    public string[] GetKnownSourcesForCountry(string countryCode)
    {
        return CountryEpgSources.TryGetValue(countryCode, out var sources) 
            ? sources 
            : Array.Empty<string>();
    }
}

/// <summary>
/// EPG kaynak bilgisi
/// </summary>
public class EpgSource
{
    public string Url { get; set; } = string.Empty;
    public int Priority { get; set; }
    public EpgSourceType Type { get; set; }
    public bool IsPrimary { get; set; }
    /// <summary>
    /// Public kaynaklarda, provider yoksa eski EPG verisini temizlemek için
    /// </summary>
    public bool ClearBeforeLoad { get; set; }
}

/// <summary>
/// EPG kaynak tipi
/// </summary>
public enum EpgSourceType
{
    CustomUrl,
    Provider,
    M3UHeader,
    IptvEpgOrg,
    EpgShare01,
    GlobalFallback
}
