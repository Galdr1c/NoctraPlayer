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
    string? ActivePromoCode => null;
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
