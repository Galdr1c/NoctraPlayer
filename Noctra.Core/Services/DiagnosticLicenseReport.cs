using System.Text;
using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// Diagnostic raporları için ortak lisans durumu metni üretir (mobil/masaüstü
/// tutarlılığı). Yalnızca "Premium/Free" demez — GÜNCEL LicenseService
/// durumunu yansıtır: kaynak (kalıcı paket / abonelik / promosyon / edisyon),
/// bitiş zamanı (UTC), gerçek trial ve pending satın alma.
/// </summary>
public static class DiagnosticLicenseReport
{
    public static string BuildStatus(ILicenseService licenseService)
    {
        ArgumentNullException.ThrowIfNull(licenseService);

        var sb = new StringBuilder();
        if (licenseService.IsPremium)
        {
            var info = licenseService.GetCurrentSubscription();
            sb.AppendLine("Status: Premium");
            sb.AppendLine($"Premium Source: {DescribeSource(licenseService.CurrentPremiumSource)}");

            if (licenseService.PremiumExpiresAtUtc is { } expiresAt)
            {
                sb.AppendLine($"Premium Expires (UTC): {expiresAt:O}");
            }

            // Trial yalnızca backend'in doğruladığı gerçek trial offer'da true.
            if (info.IsTrialPeriod)
            {
                sb.AppendLine("Trial: Yes");
            }
        }
        else
        {
            sb.AppendLine("Status: Free");
        }

        if (licenseService.HasPendingStorePurchase)
        {
            sb.AppendLine("Pending Purchase: Yes");
        }

        return sb.ToString();
    }

    private static string DescribeSource(PremiumSource source) => source switch
    {
        PremiumSource.GooglePlayLifetime => "Google Play Lifetime",
        PremiumSource.GooglePlaySubscription => "Google Play Subscription",
        PremiumSource.Promo => "Promo Code",
        PremiumSource.PremiumEdition => "Premium Edition",
        // IsPremium true iken Source her zaman dolu olur (manual debug override
        // da PremiumEdition üretir); None yalnızca savunmacı yedektir.
        _ => "None"
    };
}
