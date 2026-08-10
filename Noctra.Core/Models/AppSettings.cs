using System;
using System.Text.Json.Serialization;

namespace Noctra.Models;

/// <summary>
/// Veri kullanımı seviyesi (video kalitesi)
/// </summary>
public enum DataUsageLevel
{
    Low,      // 480p
    Medium,   // 720p
    High,     // 1080p
    Auto      // Otomatik
}

/// <summary>
/// İndirme kalitesi
/// </summary>
public enum DownloadQuality
{
    Standard,  // Standart kalite
    High       // Yüksek kalite
}

/// <summary>
/// Video buffer boyutu
/// </summary>
public enum BufferSize
{
    Small,  // 2sn
    Normal, // 5sn
    Large   // 10sn
}

/// <summary>
/// Altyazının video içindeki dikey konumu.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubtitleVerticalPosition
{
    Bottom,
    LowerMiddle,
    UpperMiddle,
    Top
}

/// <summary>
/// Kullanıcı tarafından seçilebilir altyazı metin boyutu seviyesi.
/// Platforma özel gerçek boyut SubtitleAppearanceDefaults ile hesaplanır.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubtitleTextSize
{
    Small,
    Medium,
    Large,
    ExtraLarge
}

/// <summary>
/// Platformlar arasında ortak altyazı görünümü varsayılanları ve eski ayar dönüşümleri.
/// </summary>
public static class SubtitleAppearanceDefaults
{
    public const int CurrentSchemaVersion = 1;
    public const int BackgroundOpacityPercent = 0;

    public const SubtitleTextSize DefaultTextSize = SubtitleTextSize.Medium;

    public static int ResolveMobileFontSize(SubtitleTextSize size)
        => size switch
        {
            SubtitleTextSize.Small => 18,
            SubtitleTextSize.Medium => 24,
            SubtitleTextSize.Large => 32,
            SubtitleTextSize.ExtraLarge => 40,
            _ => 24
        };

    public static int ResolveDesktopFontSize(SubtitleTextSize size)
        => size switch
        {
            SubtitleTextSize.Small => 28,
            SubtitleTextSize.Medium => 40,
            SubtitleTextSize.Large => 60,
            SubtitleTextSize.ExtraLarge => 72,
            _ => 40
        };

    public static SubtitleTextSize ResolveTextSize(int legacyFontSize)
        => legacyFontSize switch
        {
            <= 32 => SubtitleTextSize.Small,
            <= 50 => SubtitleTextSize.Medium,
            <= 66 => SubtitleTextSize.Large,
            _ => SubtitleTextSize.ExtraLarge
        };

    public static int NormalizeOpacityPercent(int value)
        => value <= 100
            ? Math.Clamp(value, 0, 100)
            : LegacyAlphaToOpacityPercent(value);

    public static int LegacyAlphaToOpacityPercent(int alpha)
        => Math.Clamp((int)Math.Round(Math.Clamp(alpha, 0, 255) / 255d * 100d), 0, 100);

    public static byte OpacityPercentToAlpha(int percent)
        => (byte)Math.Round(Math.Clamp(percent, 0, 100) * 255d / 100d);

    public static SubtitleVerticalPosition ResolveLegacyPosition(int margin)
        => margin switch
        {
            >= 751 => SubtitleVerticalPosition.Top,
            >= 411 => SubtitleVerticalPosition.UpperMiddle,
            >= 131 => SubtitleVerticalPosition.LowerMiddle,
            _ => SubtitleVerticalPosition.Bottom
        };

    public static int ToLegacyMargin(SubtitleVerticalPosition position)
        => position switch
        {
            // Masaüstünde yalnızca Bottom ve Top güvenlidir: ara konumlar mutlak
            // margin (220/600) çözünürlüğe bağlı olduğundan (720p/4K/küçük pencere)
            // en yakın güvenli uca eşlenir. Top (>=900) VideoPlayerService'te
            // video yüksekliğine göre dinamik hesaplanır (videoHeight * 0.85).
            SubtitleVerticalPosition.Top => 900,
            SubtitleVerticalPosition.UpperMiddle => 900,
            SubtitleVerticalPosition.LowerMiddle => 40,
            _ => 40
        };
}

/// <summary>
/// Uygulama ayarları modeli
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Bu ayarların ait olduğu profil ID'si. 0 ise global ayardır.
    /// </summary>
    public int ProfileId { get; set; } = 0;

    // ============ Oynatma Ayarları ============
    
    /// <summary>
    /// HTTP isteklerinde kullanılacak User-Agent
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Sonraki bölümü otomatik oynat
    /// </summary>
    public bool AutoPlayNext { get; set; } = true;
    
    /// <summary>
    /// Video buffer boyutu (Premium)
    /// </summary>
    public BufferSize VideoBufferSize { get; set; } = BufferSize.Normal;

    /// <summary>
    /// Veri kullanımı / video kalitesi
    /// </summary>
    public DataUsageLevel DataUsage { get; set; } = DataUsageLevel.Auto;
    
    /// <summary>
    /// Varsayılan ses seviyesi (0-100)
    /// </summary>
    public int DefaultVolume { get; set; } = 100;

    /// <summary>
    /// Ses kapalı mı (muted)?
    /// </summary>
    public bool IsMuted { get; set; } = false;

    /// <summary>
    /// Arka plan oynatmasına izin ver (Android: PiP modu yokken bile devam etsin)
    /// </summary>
    public bool AllowBackgroundPlayback { get; set; } = false;

    /// <summary>
    /// Altyazı varsayılan olarak açık mı? (VOD/Dizi için)
    /// </summary>
    public bool SubtitleEnabled { get; set; } = false;

    /// <summary>
    /// Tercih edilen altyazı dili (örn: "tr", "en")
    /// </summary>
    public string SubtitleLanguage { get; set; } = "en";

    /// <summary>
    /// Altyazı görünümü ayarlarının kalıcı veri şema sürümü.
    /// </summary>
    public int SubtitleAppearanceSchemaVersion { get; set; } = SubtitleAppearanceDefaults.CurrentSchemaVersion;

    /// <summary>
    /// Altyazı metin boyutu seviyesi.
    /// </summary>
    public SubtitleTextSize SubtitleTextSize { get; set; } = SubtitleAppearanceDefaults.DefaultTextSize;

    /// <summary>
    /// Eski sürümlerle uyumluluk için altyazı yazı tipi boyutu (DIP).
    /// Yeni kod SubtitleTextSize kullanmalıdır.
    /// </summary>
    [Obsolete("Use SubtitleTextSize instead.")]
    [JsonIgnore]
    public int SubtitleFontSize
    {
        get => SubtitleAppearanceDefaults.ResolveDesktopFontSize(SubtitleTextSize);
        set => SubtitleTextSize = SubtitleAppearanceDefaults.ResolveTextSize(value);
    }

    /// <summary>
    /// Altyazı arka plan opaklığı, yüzde olarak 0-100.
    /// </summary>
    public int SubtitleBackgroundOpacity { get; set; } = SubtitleAppearanceDefaults.BackgroundOpacityPercent;

    /// <summary>
    /// Altyazının dikey konumu.
    /// </summary>
    public SubtitleVerticalPosition SubtitlePosition { get; set; } = SubtitleVerticalPosition.Bottom;

    /// <summary>
    /// Eski sürümlerle ayar dosyası uyumluluğu. Yeni kod SubtitlePosition kullanmalıdır.
    /// </summary>
    [Obsolete("Use SubtitlePosition instead.")]
    [JsonIgnore]
    public int SubtitleMargin
    {
        get => SubtitleAppearanceDefaults.ToLegacyMargin(SubtitlePosition);
        set => SubtitlePosition = SubtitleAppearanceDefaults.ResolveLegacyPosition(value);
    }

    /// <summary>
    /// Tercih edilen ses dili (örn: "tr", "en")
    /// </summary>
    public string PreferredAudioLanguage { get; set; } = "en";

    /// <summary>
    /// Mobil oynatıcı jest ipuçları bir kez gösterildi mi?
    /// </summary>
    public bool HasSeenMobilePlayerGestureHints { get; set; } = false;

    /// <summary>
    /// Son kullanilan profili acilista otomatik sec
    /// </summary>
    public bool AutoSelectLastProfile { get; set; } = true;
    
    // ============ İndirme Ayarları ============
    
    /// <summary>
    /// İndirme kalitesi
    /// </summary>
    public DownloadQuality DownloadQuality { get; set; } = DownloadQuality.High;
    
    /// <summary>
    /// Sadece Wi-Fi'da indir
    /// </summary>
    public bool DownloadWifiOnly { get; set; } = true;
    
    /// <summary>
    /// İndirme klasörü yolu
    /// </summary>
    public string DownloadPath { get; set; } = string.Empty;

    /// <summary>
    /// İndirme tamamlandığında bildirim göster
    /// </summary>
    public bool ShowDownloadNotification { get; set; } = true;
    
    /// <summary>
    /// Ufak kesintileri "tamamlanmış" sayma eşiği (varsayılan: %99.98)
    /// </summary>
    public double DownloadCompletionTolerance { get; set; } = 0.9998;
    
    // ============ Görünüm Ayarları ============
    
    /// <summary>
    /// Koyu tema aktif mi
    /// </summary>
    public bool IsDarkTheme { get; set; } = true;

    // ============ Global Ayarlar ============
    
    public string Language { get; set; } = "en";
    public bool HardwareAcceleration { get; set; } = true;
    // ============ Legal / Privacy Consent ============

    public const string CurrentLegalConsentVersion = "2026-06-04";
    public const string CurrentPrivacyNoticeVersion = "2026-05-22";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegalConsentAccepted { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegalConsentVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LegalConsentAcceptedAtUtc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrivacyNoticeVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DiagnosticDataConsent { get; set; } = false;

    /// <summary>
    /// PIN sistemi PIN2'ye geçerken eski formatlardaki (PBKDF2/legacy SHA-256)
    /// PIN'ler sıfırlandıysa kullanıcıya bir defalık bilgi gösterilir (global).
    /// Bilgi gösterildiğinde false yapılır.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PinSystemResetNoticePending { get; set; } = false;

    /// <summary>
    /// Çocuk profili özelliği kaldırıldı (Faz 1) — eski IsChild=true profiller
    /// standart profile çevrildiyse kullanıcıya bir defalık bilgi gösterilir.
    /// Bilgi gösterildiğinde false yapılır.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ChildModeRemovedNoticePending { get; set; } = false;


    // ============ Promosyon Kodu / Süreli Premium ============

    /// <summary>
    /// Developer tarafından sağlanabilecek uzak promosyon kodu JSON adresi.
    /// Boşsa ortam değişkeni veya LicenseService içindeki varsayılan uzak URL kullanılır.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromoCodeConfigUrl { get; set; }

    /// <summary>
    /// DPAPI ile sifrelenmis promosyon hak kaydi.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromoGrant { get; set; }

    /// <summary>
    /// Kullanılan son aktif promosyon kodu.
    /// </summary>
    [JsonIgnore]
    public string? ActivePromoCode { get; set; }

    /// <summary>
    /// Promosyon ile açılan Premium bitiş zamanı (UTC). Null ise aktif promosyon yoktur.
    /// </summary>
    [JsonIgnore]
    public DateTime? PromoPremiumExpiresAtUtc { get; set; }

    /// <summary>
    /// Aynı kodun bu cihazda tekrar tekrar kullanılmasını önlemek için yerel kullanım listesi.
    /// </summary>
    [JsonIgnore]
    public List<string> RedeemedPromoCodes { get; set; } = new();

    // ============ Play Billing Doğrulama (backend) ============

    /// <summary>
    /// Son başarılı backend doğrulamasından gelen hakkın önbelleği (JSON).
    /// Doğrulama hizmetine ulaşılamadığında (çevrimdışı/sunucu hatası) son
    /// bilinen doğrulanmış değer buradan okunur — fail-safe davranış.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StoreVerifiedEntitlementJson { get; set; }

    // ============ Microsoft Store Degerlendirme Hatirlaticisi ============

    /// <summary>
    /// Ana uygulama ekraninin basarili acilis sayisi.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReviewPromptLaunchCount { get; set; } = 0;

    /// <summary>
    /// Degerlendirme hatirlaticisinin son gosterildigi UTC zaman.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReviewPromptLastShownAtUtc { get; set; }

    /// <summary>
    /// "Sonra" secenegi sonrasi tekrar sorulabilecek en erken UTC zaman.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReviewPromptSnoozedUntilUtc { get; set; }

    /// <summary>
    /// Kullanici tekrar sorulmasini istemediyse true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReviewPromptDismissed { get; set; } = false;

    /// <summary>
    /// Kullanici Store degerlendirme akisini actiysa zaman damgasi.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReviewPromptCompletedAtUtc { get; set; }

    /// <summary>
    /// Basariyla eklenen provider (profil/playlist) sayisi. Review prompt'u
    /// deneyim tabanli hale getirmek icin kullanilir.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReviewPromptProviderAddCount { get; set; } = 0;

    /// <summary>
    /// Anlamli surede tamamlanan playback oturum sayisi (orn. >= 30 saniye).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReviewPromptPlaybackCount { get; set; } = 0;

    /// <summary>
    /// Birikimli gercek izlenme suresi (saniye). Review promptu icin
    /// "toplam izleme >= 30 dakika" sarti burada takip edilir.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double ReviewPromptTotalPlaybackSeconds { get; set; } = 0;

    // ============ Senkronizasyon Ayarlari ============

    /// <summary>
    /// Kanal listesi otomatik yenileme sikligi (saat). 0 = Kapali (manuel)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ChannelListRefreshFrequencyHours { get; set; } = 0;

    /// <summary>
    /// EPG otomatik yenileme sikligi (saat). 0 = Kapali (manuel)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EpgRefreshFrequencyHours { get; set; } = 0;

    /// <summary>
    /// EPG etkin mi?
    /// </summary>
    public bool EpgEnabled { get; set; } = true;

    /// <summary>
    /// EPG için kullanıcının verdiği özel URL (opsiyonel)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomEpgUrl { get; set; }

    /// <summary>
    /// Birden fazla özel EPG kaynağı (max 10, ücretsiz kullanıcılarda max 2)
    /// </summary>
    public List<string> CustomEpgUrls { get; set; } = new();

    public const int EPG_URL_LIMIT = 10;
    public const int EPG_URL_FREE_LIMIT = 2;

    /// <summary>
    /// EPG saat dilimi ofseti (saat). Örn: +3 için 3, -5 için -5
    /// </summary>
    public int EpgTimeOffsetHours { get; set; } = 0;

    // ============ Gizlilik / Geçmiş ============

    /// <summary>
    /// İzleme geçmişini kaydet
    /// </summary>
    public bool SaveWatchHistory { get; set; } = true;

    /// <summary>
    /// Geçmişi tutma süresi (gün). 0 = sınırsız.
    /// </summary>
    public int WatchHistoryRetentionDays { get; set; } = 30;

    /// <summary>
    /// Uygulama kapandığında geçmişi temizle
    /// </summary>
    public bool ClearHistoryOnExit { get; set; } = false;

    // ============ Kategori Gizleme ============
    public List<string> HiddenLiveGroups { get; set; } = new();
    public List<string> HiddenMovieGroups { get; set; } = new();
    public List<string> HiddenSeriesGroups { get; set; } = new();
}
