using System.Text.Json.Serialization;

namespace Noctra.Billing.Api;

/// <summary>
/// Client'ın (Android) purchase token'ını doğrulamak için backend'e
/// gönderdiği istek. packageName/productId değerlerine istemci gönderdi diye
/// güvenilmez — sunucu kendi sabit yapılandırmasıyla doğrular.
/// </summary>
public sealed class BillingVerifyRequest
{
    public string InstallationId { get; init; } = string.Empty;
    public string PurchaseToken { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
}

/// <summary>
/// Doğrulama sonucu — Play'in gerçek durumundan üretilir; client hiçbir
/// süre hesabı yapmaz. Aylık abonelik için ExpiresAtUtc, Play'in
/// subscriptionsv2.get cevabındaki lineItems.expiryTime değeridir.
/// </summary>
public sealed class VerifiedEntitlementResponse
{
    public bool IsActive { get; init; }
    public string EntitlementType { get; init; } = string.Empty; // "Subscription" | "Lifetime"
    public DateTime? ExpiresAtUtc { get; init; }
    public bool AutoRenewEnabled { get; init; }
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Aktif abonelik bir trial offer'da mı? Play'den (subscriptionsv2.get →
    /// lineItems[].offerId + monetization product details → recurrenceMode)
    /// doğrulanır; client tarafında asla tahmin edilmez. Yalnızca gerçek
    /// trial/tanışma fazı (NON_RECURRING) kullanılırken true — ücretli aylık
    /// kullanıcı "trial" gibi görünmez.
    /// </summary>
    public bool IsTrialPeriod { get; init; }
    public DateTime VerifiedAtUtc { get; init; }
}

/// <summary>
/// Play Developer API'den normalize edilmiş doğrulama sonucu.
/// İmplementasyon ayrıntısı (v1/v2 uç noktaları) çağıranlardan gizlenir.
/// </summary>
public sealed class PlayPurchaseVerification
{
    public string EntitlementType { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public bool AutoRenewEnabled { get; init; }
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Aktif abonelik bir trial offer'da mı? subscriptionsv2.get →
    /// lineItems[].offerId ile monetization product details eşleştirilerek
    /// belirlenir (best-effort: ürün detayı okunamazsa false kalır ve hak
    /// doğrulaması asla engellenmez).
    /// </summary>
    public bool IsTrialPeriod { get; init; }
}
