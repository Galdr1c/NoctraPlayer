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
    public DateTime VerifiedAtUtc { get; init; }
}

/// <summary>Entitlement veritabanı satırı (GET /billing/entitlement).</summary>
public sealed class StoredEntitlementRow
{
    public string InstallationId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Satın alma token'ının SHA-256 özeti (ham token saklanmaz).</summary>
    public string PurchaseTokenHash { get; init; } = string.Empty;

    public string EntitlementType { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public bool AutoRenewEnabled { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTime LastVerifiedAtUtc { get; init; }
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
}

/// <summary>Pub/Sub RTDN push mesajı zarfı (veri Base64'lü).</summary>
public sealed class PubSubPushEnvelope
{
    [JsonPropertyName("message")]
    public PubSubMessage? Message { get; init; }
}

public sealed class PubSubMessage
{
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }
}
