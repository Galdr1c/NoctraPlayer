namespace IPTVPlayer.Models;

/// <summary>
/// Subscription tier enum
/// </summary>
public enum SubscriptionTier
{
    Free = 0,
    Premium = 1
}

/// <summary>
/// Contains limits and features for each subscription tier
/// </summary>
public static class TierLimits
{
    // ==========================================
    // FREE TIER
    // ==========================================
    public static class Free
    {
        public const int MaxProfiles = 1;
        public const int MaxM3UAccounts = 1;
        public const int MaxFavorites = 50; // Updated to 50
        public const int MaxFavoriteGroups = 2;
        public const int EpgHours = 24; // 24 saat EPG
        public const int ThemeCount = 1; // Sadece default mor tema
        public const bool HasAds = true; // Watermark dahil
        public const bool HasWatermark = true;
        public const bool CanSelectAudioTrack = false;
        public const bool CanSelectSubtitleTrack = false;
        public const bool HasMiniPlayer = false;
        public const bool HasAdvancedSearch = false;
        public const bool CanHideChannels = false;
        public const bool CanReorderChannels = false;
    }

    // ==========================================
    // PREMIUM TIER (Lifetime / 480 TL)
    // ==========================================
    public static class Premium
    {
        public const int MaxProfiles = int.MaxValue; // Sınırsız
        public const int MaxM3UAccounts = int.MaxValue; // Sınırsız
        public const int MaxFavorites = int.MaxValue; // Sınırsız
        public const int MaxFavoriteGroups = int.MaxValue; // Sınırsız
        public const int EpgDays = 14; // 14 gün EPG
        public const int ThemeCount = int.MaxValue; // Tüm temalar
        public const bool HasAds = false;
        public const bool HasWatermark = false; // Filigran yok
        public const bool CanSelectAudioTrack = true;
        public const bool CanSelectSubtitleTrack = true;
        public const bool HasMiniPlayer = true;
        public const bool HasAdvancedSearch = true;
        public const bool CanHideChannels = true;
        public const bool CanReorderChannels = true;
        public const bool HasAnimatedBackgrounds = true;
        public const bool HasAvatarPacks = true;
        
        // Former Pro features merged into Premium
        public const bool HasMultiview = true;
        public const bool HasTimeshift = true;
    }
}

/// <summary>
/// Current user's subscription info
/// </summary>
public class SubscriptionInfo
{
    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;
    public DateTime? ExpiresAt { get; set; }
    public bool IsTrialPeriod { get; set; }
    public int TrialDaysRemaining => IsTrialPeriod && ExpiresAt.HasValue 
        ? Math.Max(0, (ExpiresAt.Value - DateTime.Now).Days) 
        : 0;
    
    public bool IsPremiumOrHigher => Tier >= SubscriptionTier.Premium;
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.Now;
}
