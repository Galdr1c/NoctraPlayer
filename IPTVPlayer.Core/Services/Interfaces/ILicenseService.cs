using IPTVPlayer.Models;

namespace IPTVPlayer.Services.Interfaces;

/// <summary>
/// License/subscription yönetimi servisi
/// </summary>
public interface ILicenseService
{
    // Legacy properties (backward compat)
    bool IsPremium { get; }
    void ActivatePremium();
    string GetPriceText();
    
    // New tier system
    SubscriptionTier CurrentTier { get; }
    SubscriptionInfo GetCurrentSubscription();
    bool IsFeatureAvailable(string featureName);
    bool IsWithinLimit(string limitName, int currentCount);
    int GetLimit(string limitName);
    Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier);
    Task RefreshSubscriptionStatusAsync();
    void SetTierForTesting(SubscriptionTier tier);
    
    event Action SubscriptionChanged;
}
