namespace Noctra.Models;

/// <summary>
/// Mağazadan (Google Play) satın alınabilir Premium ürün türü.
/// </summary>
public enum StoreProductKind
{
    /// <summary>Tekrarlayan abonelik (örn. aylık).</summary>
    Subscription = 0,

    /// <summary>Tek seferlik kalıcı paket (non-consumable).</summary>
    Lifetime = 1
}

/// <summary>
/// Mağazada listelenen satın alınabilir Premium ürünü.
/// </summary>
public sealed class StoreProduct
{
    public string ProductId { get; init; } = string.Empty;
    public StoreProductKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>Mağazanın yerelleştirdiği fiyat metni (örn. "₺59,99").</summary>
    public string Price { get; init; } = string.Empty;

    /// <summary>Abonelik dönemi (ISO-8601, örn. "P1M"). Yalnızca abonelikler için.</summary>
    public string? BillingPeriod { get; init; }

    /// <summary>Abonelik satın alma akışında zorunlu teklif jetonu.</summary>
    public string? OfferToken { get; init; }
}

/// <summary>
/// Kullanıcının mağazadan edindiği Premium hakları.
/// </summary>
public sealed class StoreEntitlement
{
    public static readonly StoreEntitlement None = new();

    /// <summary>Tek seferlik kalıcı paket satın alındıysa true.</summary>
    public bool HasLifetimePremium { get; init; }

    /// <summary>
    /// Abonelik bitiş zamanı (UTC). Yalnızca backend doğrulamasından gelir
    /// (subscriptionsv2.get → lineItems.expiryTime) — client tarafında süre
    /// asla hesaplanmaz/uzatılmaz.
    /// </summary>
    public DateTime? SubscriptionExpiresAtUtc { get; init; }

    /// <summary>
    /// Bu turda haklar backend doğrulamasından geçti mi? False, doğrulama
    /// hizmetine ulaşılamadığı anlamına gelir (ağ/sunucu hatası) — çağıran
    /// son bilinen doğrulanmış değeri (önbellek) kullanmalıdır.
    /// </summary>
    public bool IsVerified { get; init; } = true;

    public bool HasActivePremium =>
        HasLifetimePremium ||
        (SubscriptionExpiresAtUtc.HasValue && SubscriptionExpiresAtUtc.Value > DateTime.UtcNow);
}

/// <summary>
/// Satın alma akışı sonucu.
/// </summary>
public sealed class StorePurchaseResult
{
    public bool Success { get; init; }
    public bool CancelledByUser { get; init; }
    public bool AlreadyOwned { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static StorePurchaseResult Ok() => new() { Success = true };

    public static StorePurchaseResult Cancelled() => new() { CancelledByUser = true };

    public static StorePurchaseResult Owned() => new() { AlreadyOwned = true };

    public static StorePurchaseResult Fail(string message) => new() { ErrorMessage = message };
}
