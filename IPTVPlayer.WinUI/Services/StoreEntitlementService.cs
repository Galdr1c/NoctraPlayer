using System;
using System.Threading.Tasks;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;
using Windows.Services.Store;
using System.Collections.Generic;

namespace IPTVPlayer.WinUI.Services;

public class StoreEntitlementService : ILicenseService
{
    private readonly StoreContext _context;
    private SubscriptionInfo _currentSubscription;
    private bool _isInit;

    public bool IsPremium => _currentSubscription.IsPremiumOrHigher;
    public SubscriptionTier CurrentTier => _currentSubscription.Tier;
    public event Action? SubscriptionChanged;

    public StoreEntitlementService() { _context = StoreContext.GetDefault(); _currentSubscription = new SubscriptionInfo { Tier = SubscriptionTier.Free, ExpiresAt = DateTime.MaxValue }; }

    public async Task InitializeAsync() { if (_isInit) return; await RefreshSubscriptionStatusAsync(); _isInit = true; }
    public void ActivatePremium() { }
    public string GetPriceText() => "480 TL";
    public SubscriptionInfo GetCurrentSubscription() => _currentSubscription;

    public bool IsFeatureAvailable(string f) => f switch { "Multiview" => IsPremium, "Timeshift" => IsPremium, "NoAds" => IsPremium, _ => true };
    public bool IsWithinLimit(string l, int c) => IsPremium || l switch { "Profiles" => c < 3, "Favorites" => c < 50, _ => true };
    public int GetLimit(string l) => IsPremium ? 9999 : l switch { "Profiles" => 3, "Favorites" => 50, _ => 999 };

    public async Task<bool> StartPurchaseFlowAsync(SubscriptionTier t) {
        var res = await _context.RequestPurchaseAsync("9NBLGGH42764");
        if (res.Status == StorePurchaseStatus.Succeeded) { await RefreshSubscriptionStatusAsync(); return true; }
        return false;
    }

    public async Task RefreshSubscriptionStatusAsync() {
        try {
            var res = await _context.GetAssociatedStoreProductsAsync(new[] { "Durable", "Subscription" });
            if (res.ExtendedError == null && res.Products.Values.Any(p => p.IsInUserCollection)) {
                _currentSubscription.Tier = SubscriptionTier.Premium;
                SubscriptionChanged?.Invoke();
            }
        } catch { }
    }

    public void SetTierForTesting(SubscriptionTier t) { _currentSubscription.Tier = t; SubscriptionChanged?.Invoke(); }
}
