namespace Noctra.Billing.Api;

/// <summary>
/// Cloud Run / genel proxy arkasında gerçek istemci IP'sini çözer.
/// </summary>
public static class ClientIpResolver
{
    /// <summary>
    /// X-Forwarded-For zincirinin SON geçerli değerini, yoksa bağlantının uzak
    /// adresini döndürür. Cloud Run (GFE) istekteki XFF zincirinin sonuna gerçek
    /// istemci IP'sini ekler; istemci kendi XFF üstbilgisini sahteleyebildiği için
    /// ilk değer değil son değer platform güvenilir bilgisidir. RemoteIpAddress
    /// Cloud Run'da proxy'ye ait olduğundan ona bakılsaydı bütün kullanıcılar tek
    /// bölümü paylaşır ve per-IP rate limit global 30 istek/dakikaya dönüşüp
    /// meşru kullanıcılara haksız 429 verirdi. Not: bu yalnız abuse/cost bariyeri
    /// tanımlar; yetkilendirme değildir (gerçek güvenlik Play token doğrulamasıdır).
    /// </summary>
    public static string GetClientIp(
        string? forwardedFor,
        string? remoteIp)
    {
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            // Zincirin sonundan başa yürü; sondaki geçerli değer GFE'nin eklediği
            // gerçek istemci IP'sidir (boş/boşluklu segmentleri atla).
            var parts = forwardedFor.Split(',');
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var candidate = parts[i].Trim();
                if (candidate.Length > 0)
                {
                    return candidate;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(remoteIp) ? remoteIp : "unknown";
    }
}
