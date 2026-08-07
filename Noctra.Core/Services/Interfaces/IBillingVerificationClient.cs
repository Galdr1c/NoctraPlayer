namespace Noctra.Services.Interfaces;

/// <summary>
/// Play satın alma token'ının backend üzerinden doğrulanması isteği.
/// packageName/productId sunucuda kendi yapılandırmasıyla doğrulanır;
/// backend stateless olduğu için installation kimliği taşınmaz.
/// </summary>
public sealed class BillingVerifyRequest
{
    public string PurchaseToken { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
}

/// <summary>
/// Backend'in Google Play'den okuduğu gerçek durumun normalize edilmiş hali.
/// ExpiresAtUtc, subscriptionsv2.get → lineItems.expiryTime değeridir;
/// client tarafında süre asla hesaplanmaz.
/// </summary>
public sealed class BillingVerifiedEntitlement
{
    public bool IsActive { get; init; }
    public string EntitlementType { get; init; } = string.Empty; // "Subscription" | "Lifetime"
    public DateTime? ExpiresAtUtc { get; init; }
    public bool AutoRenewEnabled { get; init; }
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// True when Google Play reports the active subscription line item in a
    /// free-trial phase via `lineItems[].offerPhase.freeTrial` — backend tek
    /// istekle okur (ek Monetization API çağrısı yok); client bu bayrağı
    /// tahmin etmez. Ücretli aylık kullanıcı asla trial gibi görünmez.
    /// </summary>
    public bool IsTrialPeriod { get; init; }
    public DateTime VerifiedAtUtc { get; init; }
}

/// <summary>
/// Purchase token'larını backend doğrulama hizmetine iletir. Başarısızlık
/// (ağ, sunucu hatası, bozuk yanıt) null döndürür — çağıran, son bilinen
/// doğrulanmış hakkı kullanmaya devam eder (fail-safe önbellek).
/// </summary>
public interface IBillingVerificationClient
{
    Task<BillingVerifiedEntitlement?> VerifyAsync(
        BillingVerifyRequest request,
        CancellationToken cancellationToken = default);
}
