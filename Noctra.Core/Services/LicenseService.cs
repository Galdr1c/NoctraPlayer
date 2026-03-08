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
        public const string ResumePlayback = "resume_playback";
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
            Features.ResumePlayback => tier == SubscriptionTier.Premium,
            
            _ => false // Unknown features default to unavailable
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
            Limits.Favorites => TierLimits.Free.MaxFavorites,
            Limits.FavoriteGroups => TierLimits.Free.MaxFavoriteGroups,
            Limits.Themes => TierLimits.Free.ThemeCount,
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
            Limits.Favorites => TierLimits.Free.MaxFavorites,
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


