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
    public static class Features
    {
        public const string AudioTrackSelection = "audio_track_selection";
        public const string SubtitleTrackSelection = "subtitle_track_selection";
        public const string MiniPlayer = "mini_player";
        public const string AdvancedSearch = "advanced_search";
        public const string HideChannels = "hide_channels";
        public const string ReorderChannels = "reorder_channels";
        public const string AnimatedBackgrounds = "animated_backgrounds";
        public const string AvatarPacks = "avatar_packs";
        public const string Multiview = "multiview";
        public const string Timeshift = "timeshift";
        public const string Recording = "recording";
        public const string CustomShortcuts = "custom_shortcuts";
        public const string AdFree = "ad_free";
        public const string FullEpg = "full_epg";
    }

    // ==========================================
    // LIMIT NAMES
    // ==========================================
    public static class Limits
    {
        public const string Profiles = "profiles";
        public const string M3UAccounts = "m3u_accounts";
        public const string Favorites = "favorites";
        public const string FavoriteGroups = "favorite_groups";
        public const string Themes = "themes";
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
            // Premium features (Everything unlocks with Premium)
            Features.AudioTrackSelection => tier == SubscriptionTier.Premium,
            Features.SubtitleTrackSelection => tier == SubscriptionTier.Premium,
            Features.MiniPlayer => tier == SubscriptionTier.Premium,
            Features.AdvancedSearch => tier == SubscriptionTier.Premium,
            Features.HideChannels => tier == SubscriptionTier.Premium,
            Features.ReorderChannels => tier == SubscriptionTier.Premium,
            Features.AnimatedBackgrounds => tier == SubscriptionTier.Premium,
            Features.AvatarPacks => tier == SubscriptionTier.Premium,
            Features.AdFree => tier == SubscriptionTier.Premium,
            Features.FullEpg => tier == SubscriptionTier.Premium,
            Features.Multiview => tier == SubscriptionTier.Premium,
            Features.Timeshift => tier == SubscriptionTier.Premium,
            Features.Recording => tier == SubscriptionTier.Premium,
            Features.CustomShortcuts => tier == SubscriptionTier.Premium,
            
            _ => true // Unknown features default to available
        };
    }

    public bool IsWithinLimit(string limitName, int currentCount)
    {
        var tier = _currentSubscription.Tier;
        
        int maxAllowed = limitName switch
        {
            Limits.Profiles => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxProfiles,
                SubscriptionTier.Premium => TierLimits.Premium.MaxProfiles,
                _ => 1
            },
            Limits.M3UAccounts => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxM3UAccounts,
                SubscriptionTier.Premium => TierLimits.Premium.MaxM3UAccounts,
                _ => 1
            },
            Limits.Favorites => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxFavorites,
                SubscriptionTier.Premium => TierLimits.Premium.MaxFavorites,
                _ => 50
            },
            Limits.FavoriteGroups => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxFavoriteGroups,
                SubscriptionTier.Premium => TierLimits.Premium.MaxFavoriteGroups,
                _ => 2
            },
            Limits.Themes => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.ThemeCount,
                SubscriptionTier.Premium => TierLimits.Premium.ThemeCount,
                _ => 1
            },
            _ => int.MaxValue
        };

        return currentCount < maxAllowed;
    }

    public int GetLimit(string limitName)
    {
        var tier = _currentSubscription.Tier;
        
        return limitName switch
        {
            Limits.Profiles => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxProfiles,
                SubscriptionTier.Premium => TierLimits.Premium.MaxProfiles,
                _ => 1
            },
            Limits.M3UAccounts => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxM3UAccounts,
                SubscriptionTier.Premium => TierLimits.Premium.MaxM3UAccounts,
                _ => 1
            },
            Limits.Favorites => tier switch
            {
                SubscriptionTier.Free => TierLimits.Free.MaxFavorites,
                SubscriptionTier.Premium => TierLimits.Premium.MaxFavorites,
                _ => 50
            },
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
    /// Debug/test için tier'ı manuel ayarla
    /// </summary>
    public void SetTierForTesting(SubscriptionTier tier)
    {
        _currentSubscription.Tier = tier;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        SubscriptionChanged?.Invoke();
    }
}


