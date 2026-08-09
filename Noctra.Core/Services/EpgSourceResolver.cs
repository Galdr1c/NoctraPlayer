using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// Yapılandırılmış EPG kaynaklarını güven sırasına koyar ve import tercihlerini taşır.
/// Öncelik: Custom URL → Provider EPG → M3U x-tvg-url
/// </summary>
public class EpgSourceResolver
{
    /// <summary>
    /// Verilen parametrelere göre EPG URL listesini öncelik sırasına göre döndürür.
    /// Priority 0: Custom EPG
    /// Priority 1: Provider EPG
    /// Priority 2: M3U x-tvg-url
    /// </summary>
    public List<EpgSource> ResolveEpgSources(
        string? providerEpgUrl = null, 
        string? m3uEpgUrl = null, 
        IEnumerable<string>? customEpgUrls = null, 
        string? preferredLanguageCode = null,
        IDictionary<string, string>? providerHeaders = null)
    {
        var sources = new List<EpgSource>();
        var normalizedPreferredLanguage = LocalizationService.NormalizeLanguageCode(preferredLanguageCode);

        // 0. Kullanıcının özel EPG URL'leri (ayarlardan — EN YÜKSEK ÖNCELİK)
        if (customEpgUrls != null)
        {
            foreach (var url in customEpgUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    sources.Add(new EpgSource
                    {
                        Url = url.Trim(),
                        Priority = 0,
                        Type = EpgSourceType.CustomUrl,
                        IsPrimary = true,
                        PreferredLanguageCode = normalizedPreferredLanguage
                    });
                }
            }
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
                    IsPrimary = true,
                    PreferredLanguageCode = normalizedPreferredLanguage,
                    Headers = providerHeaders
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
                    IsPrimary = string.IsNullOrEmpty(providerEpgUrl),
                    PreferredLanguageCode = normalizedPreferredLanguage
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
    /// M3U URL'inden Xtream Codes EPG (xmltv.php) adresini tahmin etmeye çalışır.
    /// Format: .../get.php?username=...&password=...
    /// </summary>
    public string? TryInferXtreamEpgUrl(string playlistUrl)
    {
        if (string.IsNullOrWhiteSpace(playlistUrl)) return null;

        // Xtream Codes M3U Plus URL: .../get.php?username=...&password=...
        if (playlistUrl.Contains("/get.php") && playlistUrl.Contains("username=") && playlistUrl.Contains("password="))
        {
            try
            {
                // Örnek: http://domain:80/get.php?username=user&password=pass&type=m3u_plus
                var baseUrl = playlistUrl.Split("/get.php")[0];
                
                // Query string ayıklama (manuel - Regex veya basit split ile System.Web bağımlılığı olmadan)
                var userMatch = System.Text.RegularExpressions.Regex.Match(playlistUrl, @"username=([^&]+)");
                var passMatch = System.Text.RegularExpressions.Regex.Match(playlistUrl, @"password=([^&]+)");

                if (userMatch.Success && passMatch.Success)
                {
                    var user = userMatch.Groups[1].Value;
                    var pass = passMatch.Groups[1].Value;
                    return $"{baseUrl}/xmltv.php?username={user}&password={pass}";
                }
            }
            catch
            {
                // Tahmin başarısız olursa null dön
            }
        }

        return null;
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
    public string PreferredLanguageCode { get; set; } = "en-US";
    /// <summary>
    /// Public kaynaklarda, provider yoksa eski EPG verisini temizlemek için
    /// </summary>
    public bool ClearBeforeLoad { get; set; }

    /// <summary>
    /// Bu kaynağa özel HTTP başlıkları (örn: Stalker MAC)
    /// </summary>
    public IDictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// EPG kaynak tipi
/// </summary>
public enum EpgSourceType
{
    CustomUrl,
    Provider,
    M3UHeader
}
