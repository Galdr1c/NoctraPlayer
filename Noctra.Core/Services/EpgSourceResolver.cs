namespace Noctra.Services;

/// <summary>
/// Ülke koduna göre en uygun EPG kaynaklarını belirler
/// Öncelik: Provider EPG → M3U x-tvg-url → iptv-epg.org → global fallback
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
    /// Verilen parametrelere göre EPG URL listesini öncelik sırasına ve country listesine göre döndürür.
    /// Priority 0: Custom EPG
    /// Priority 1: Provider EPG
    /// Priority 2: M3U x-tvg-url
    /// Priority 3: iptv-epg.org (ülkelere göre)
    /// Priority 4: Global Fallback
    /// </summary>
    public List<EpgSource> ResolveEpgSources(List<string> countryCodes, string? providerEpgUrl = null, string? m3uEpgUrl = null, string? customEpgUrl = null, bool hasUsableTvgIds = false)
    {
        var sources = new List<EpgSource>();

        // 0. Kullanıcının özel EPG URL'i (ayarlardan — EN YÜKSEK ÖNCELİK)
        if (!string.IsNullOrWhiteSpace(customEpgUrl))
        {
            sources.Add(new EpgSource
            {
                Url = customEpgUrl,
                Priority = 0,
                Type = EpgSourceType.CustomUrl,
                IsPrimary = true // Kullanıcı kendi URL'ini girdi, güvenilir kaynak
            });
        }

        // 1. Provider EPG (en güvenilir — aynı channel ID'leri kullanır)
        if (!string.IsNullOrWhiteSpace(providerEpgUrl))
        {
            if (!sources.Any(s => s.Url == providerEpgUrl))
            {
                sources.Add(new EpgSource
                {
                    Url = providerEpgUrl,
                    Priority = 1,
                    Type = EpgSourceType.Provider,
                    IsPrimary = true // Provider ID'leri kanal TvgId veya Id ile eşleşir
                });
            }
        }

        // 2. M3U x-tvg-url
        if (!string.IsNullOrWhiteSpace(m3uEpgUrl))
        {
            if (!sources.Any(s => s.Url == m3uEpgUrl))
            {
                sources.Add(new EpgSource
                {
                    Url = m3uEpgUrl,
                    Priority = 2,
                    Type = EpgSourceType.M3UHeader,
                    IsPrimary = string.IsNullOrEmpty(providerEpgUrl) // Sadece provider yoksa primary sayılır
                });
            }
        }

        // 3. iptv-epg.org (country loops)
        foreach (var countryCode in countryCodes)
        {
            var code = countryCode.ToLowerInvariant();
            var countryUrl = string.Format(IptvEpgOrgTemplate, code);
            
            if (!sources.Any(s => s.Url == countryUrl))
            {
                sources.Add(new EpgSource
                {
                    Url = countryUrl,
                    Priority = 3,
                    Type = EpgSourceType.IptvEpgOrg,
                    IsPrimary = true
                });
            }
        }

        // 4. Global fallback (sadece eğer bilinen bir ülke listesinde yoksa vs. ama genelde ekleriz)
        // Check if any of the provided countries are in our known dictionary, if not add fallback.
        bool hasKnownCountry = countryCodes.Any(c => CountryEpgSources.ContainsKey(c));
        if (!hasKnownCountry)
        {
            if (!sources.Any(s => s.Url == GlobalFallbackUrl))
            {
                sources.Add(new EpgSource
                {
                    Url = GlobalFallbackUrl,
                    Priority = 4,
                    Type = EpgSourceType.GlobalFallback,
                    IsPrimary = true
                });
            }
        }

        // Sort by priority and set ClearBeforeLoad only for the very first item
        var finalSources = sources.OrderBy(s => s.Priority).ToList();
        for (int i = 0; i < finalSources.Count; i++)
        {
            finalSources[i].ClearBeforeLoad = (i == 0);
        }

        return finalSources;
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


