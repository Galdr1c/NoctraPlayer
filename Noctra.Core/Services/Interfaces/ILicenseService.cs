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
