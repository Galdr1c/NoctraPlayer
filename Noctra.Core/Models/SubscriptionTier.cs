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
/// Etkin Premium hakkının kaynağı. Ücretli abonelik ile promosyon süresi
/// artık tek bir "trial" gibi işaretlenmez; UI (ör. upsell) kaynağa göre
/// davranır:
///  - Kalıcı paket sahibine aylık satın alma sunulmaz.
///  - Kalıcı paket alındıktan sonra aylık abonelik Google Play'de yenilenmeye
///    devam edebilir — kullanıcıya iptal hatırlatması gösterilir.
/// </summary>
public enum PremiumSource
{
    None = 0,
    GooglePlaySubscription = 1,
    GooglePlayLifetime = 2,
    Promo = 3,
    PremiumEdition = 4
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
        public const int MaxCustomEpgUrls = 2;
        public const bool HasAds = true;
        public const bool HasWatermark = true;
        public const bool EpgAutoRefresh = false; // Sadece manuel
    }

    // ==========================================
    // PREMIUM TIER
    // ==========================================
    public static class Premium
    {
        public const int MaxProfiles = 12;
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

    /// <summary>
    /// Yalnızca gerçek bir trial offer kullanıldığında true olmalıdır.
    /// Süreli mağaza aboneliği veya promosyon "trial" DEĞİLDİR — ücretli
    /// aylık kullanıcı trial gibi görünmemelidir. Mevcut sistemde trial
    /// tespiti bulunmadığından her zaman false kalır.
    /// </summary>
    public bool IsTrialPeriod { get; set; }
    public int TrialDaysRemaining => IsTrialPeriod && ExpiresAt.HasValue 
        ? Math.Max(0, (ExpiresAt.Value - DateTime.UtcNow).Days) 
        : 0;

    /// <summary>
    /// Etkin Premium'un kaynağı (kalıcı paket / abonelik / promosyon / edisyon).
    /// </summary>
    public PremiumSource Source { get; set; } = PremiumSource.None;
    
    public bool IsPremiumOrHigher => Tier >= SubscriptionTier.Premium;
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}

