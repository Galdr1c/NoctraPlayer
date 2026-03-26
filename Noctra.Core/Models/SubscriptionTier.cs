namespace Noctra.Models;

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
        public const int MaxProfiles = 5;
        public const int MaxM3UAccounts = 3;
        public const int MaxCustomEpgUrls = 2;
        public const bool HasAds = true;
        public const bool HasWatermark = true;
        public const bool EpgAutoRefresh = false; // Sadece manuel
    }

    // ==========================================
    // PREMIUM TIER (Lifetime / 499.50 TL)
    // ==========================================
    public static class Premium
    {
        public const int MaxProfiles = 12;
        public const int MaxM3UAccounts = int.MaxValue;
        public const int MaxCustomEpgUrls = 10;
        public const bool HasAds = false;
        public const bool HasWatermark = false;
        public const bool EpgAutoRefresh = true;
        public const bool HasResumePlayback = true;
        public const bool HasSleepTimer = true;
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
        ? Math.Max(0, (ExpiresAt.Value - DateTime.UtcNow).Days) 
        : 0;
    
    public bool IsPremiumOrHigher => Tier >= SubscriptionTier.Premium;
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}


