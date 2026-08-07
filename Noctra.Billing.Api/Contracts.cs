using System.Text.Json.Serialization;

namespace Noctra.Billing.Api;

/// <summary>
/// Client'ın (Android) purchase token'ını doğrulamak için backend'e
/// gönderdiği istek. packageName/productId değerlerine istemci gönderdi diye
/// güvenilmez — sunucu kendi sabit yapılandırmasıyla doğrular. Backend
/// stateless'tir: installation kimliği taşınmaz.
/// </summary>
public sealed class BillingVerifyRequest
{
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
    /// True when Google Play reports the active subscription line item in a
    /// free-trial phase via `lineItems[].offerPhase.freeTrial` — tek istekle
    /// okunur, ek Monetization API çağrısı yoktur. Client bu bayrağı tahmin
    /// etmez; ücretli aylık kullanıcı asla trial gibi görünmez.
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
    /// True when Google Play reports the active subscription line item in a
    /// free-trial phase via `lineItems[].offerPhase.freeTrial` — tek istekle
    /// okunur, ek Monetization API çağrısı yoktur. offerPhase yoksa false
    /// kalır ve hak doğrulaması asla engellenmez (best-effort).
    /// </summary>
    public bool IsTrialPeriod { get; init; }
}
