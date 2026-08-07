using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// License/subscription yönetimi servisi
/// </summary>
public interface ILicenseService
{
    // Legacy properties (backward compat)
    bool IsPremium { get; }
    bool CanUpgradeToPremium { get; }
    bool IsEditionLockedPremium { get; }
    DateTime? PromoPremiumExpiresAtUtc => null;

    /// <summary>
    /// Etkin Premium'un biteceği an (mağaza aboneliği veya promosyon;
    /// kalıcı edisyon/kalıcı paket için null).
    /// </summary>
    DateTime? PremiumExpiresAtUtc => null;

    string? ActivePromoCode => null;

    /// <summary>
    /// Kayıtlı PromoGrant çözülemiyorsa true (fail-closed Free'e düşüş sırasında
    /// kullanıcıya sessiz kalınmamalıdır).
    /// </summary>
    bool IsPromoGrantCorrupted => false;

    /// <summary>
    /// Mağazada onay bekleyen (PENDING) bir satın alma var mı? Pending satın
    /// alma Premium vermez; UI bu bayrağı görüp "ödeme bekleniyor" bildirimi
    /// gösterir. Mağaza desteği olmayan platformlarda (masaüstü) her zaman false.
    /// </summary>
    bool HasPendingStorePurchase => false;

    /// <summary>
    /// Kalıcı (tek seferlik) Premium paketi sahibi mi? Kalıcı paket sahibine
    /// aylık satın alma sunulmaz. Mağaza desteği olmayan platformlarda false.
    /// </summary>
    bool HasLifetimePremium => false;

    /// <summary>
    /// Google Play'de AKTİF bir süreli abonelik var mı (kalıcı paketten
    /// bağımsız)? Kalıcı paket alındıktan sonra aylık abonelik yenilenmeye
    /// devam edebilir; UI kullanıcıyı aboneliği iptal etmeye yönlendirir.
    /// </summary>
    bool HasActiveStoreSubscription => false;

    /// <summary>
    /// Etkin Premium'un kaynağı (kalıcı paket / abonelik / promosyon / edisyon).
    /// </summary>
    PremiumSource CurrentPremiumSource => PremiumSource.None;

    Task<PromoCodeRedemptionResult> ApplyPromoCodeAsync(string promoCode) =>
        Task.FromResult(PromoCodeRedemptionResult.Fail("Promosyon kodu bu lisans servisinde desteklenmiyor."));
    void ActivatePremium();
    void DeactivatePremium();
    
    // New tier system
    SubscriptionTier CurrentTier { get; }
    SubscriptionInfo GetCurrentSubscription();
    bool IsFeatureAvailable(string featureName);
    bool IsWithinLimit(string limitName, int currentCount);
    int GetLimit(string limitName);
    Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier);
    Task RefreshSubscriptionStatusAsync();
    void SetTierForTesting(SubscriptionTier tier);
    
    event Action? SubscriptionChanged;
}
