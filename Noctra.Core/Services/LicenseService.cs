using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// License service implementation
/// Şimdilik local/mock - Microsoft Store entegrasyonu sonra eklenecek
/// </summary>
public class LicenseService : ObservableObject, ILicenseService
{
    private SubscriptionInfo _currentSubscription = new();

    // ==========================================
    // FEATURE NAMES
    // ==========================================
        public const string AdFree = "ad_free";
        public const string EpgAutoRefresh = "epg_auto_refresh";
        public const string ResumePlayback = "resume_playback";
    }

    // ==========================================
    // LIMIT NAMES
    // ==========================================
    public static class Limits
    {
        public const string Profiles = "profiles";
        public const string M3UAccounts = "m3u_accounts";
        public const string CustomEpgUrls = "custom_epg_urls";
    }

    // ==========================================
    // LEGACY PROPERTIES (backward compat)
    // ==========================================
    public bool IsPremium => _currentSubscription.IsPremiumOrHigher;

    public void ActivatePremium()
    {
        _currentSubscription.Tier = SubscriptionTier.Premium;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        SubscriptionChanged?.Invoke();
    }

    public void DeactivatePremium()
    {
        if (_currentSubscription.Tier == SubscriptionTier.Free) return;
        
        _currentSubscription.Tier = SubscriptionTier.Free;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        SubscriptionChanged?.Invoke();
    }

    public string GetPriceText()
    {
        return "499.95 TL (Tek Sefer)"; 
    }

    // ==========================================
    // NEW TIER SYSTEM
    // ==========================================

    public SubscriptionInfo GetCurrentSubscription()
    {
        return _currentSubscription;
    }

    public SubscriptionTier CurrentTier => _currentSubscription.Tier;

    public bool IsFeatureAvailable(string featureName)
    {
        var tier = _currentSubscription.Tier;
        
        return featureName switch
        {
            // Premium features
            Features.AdFree => tier == SubscriptionTier.Premium,
            Features.EpgAutoRefresh => tier == SubscriptionTier.Premium,
            Features.ResumePlayback => tier == SubscriptionTier.Premium,
            
            _ => false
        };
    }

    public bool IsWithinLimit(string limitName, int currentCount)
    {
        var tier = _currentSubscription.Tier;
        if (tier == SubscriptionTier.Premium) return true;
        
        int maxAllowed = limitName switch
        {
            Limits.Profiles => TierLimits.Free.MaxProfiles,
            Limits.M3UAccounts => TierLimits.Free.MaxM3UAccounts,
            Limits.CustomEpgUrls => TierLimits.Free.MaxCustomEpgUrls,
            _ => int.MaxValue
        };

        return currentCount < maxAllowed;
    }

    public int GetLimit(string limitName)
    {
        var tier = _currentSubscription.Tier;
        if (tier == SubscriptionTier.Premium) return int.MaxValue;
        
        return limitName switch
        {
            Limits.Profiles => TierLimits.Free.MaxProfiles,
            Limits.M3UAccounts => TierLimits.Free.MaxM3UAccounts,
            _ => int.MaxValue
        };
    }

    public event Action? SubscriptionChanged;

    public async Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier)
    {
        // TODO: Microsoft Store API entegrasyonu
        // StoreContext.GetDefault().RequestPurchaseAsync(...)
        
        await Task.Delay(100); // Placeholder
        return false;
    }

    public async Task RefreshSubscriptionStatusAsync()
    {
        // TODO: Microsoft Store'dan güncel abonelik durumunu çek
        await Task.Delay(100); // Placeholder
    }

    /// <summary>
    /// Debug/test için tier'ı manuel ayarla (Event fırlatmaz)
    /// </summary>
    public void SetTierForTesting(SubscriptionTier tier)
    {
        _currentSubscription.Tier = tier;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
    }
}


