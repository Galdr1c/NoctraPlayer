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
    /// Altyazı varsayılan olarak açık mı? (VOD/Dizi için)
    /// </summary>
    public bool SubtitleEnabled { get; set; } = false;

    /// <summary>
    /// Tercih edilen altyazı dili (örn: "tr", "en")
    /// </summary>
    public string SubtitleLanguage { get; set; } = "en";

    /// <summary>
    /// Altyazı yazı tipi boyutu (varsayılan: 40)
    /// </summary>
    public int SubtitleFontSize { get; set; } = 40;

    /// <summary>
    /// Altyazı arka plan şeffaflığı (0: Kapalı, 128: Yarı Saydam, 255: Siyah)
    /// </summary>
    public int SubtitleBackgroundOpacity { get; set; } = 0;

    /// <summary>
    /// Altyazı alttan boşluk (Margin) miktarı
    /// </summary>
    public int SubtitleMargin { get; set; } = 40;

    /// <summary>
    /// Tercih edilen ses dili (örn: "tr", "en")
    /// </summary>
    public string PreferredAudioLanguage { get; set; } = "en";

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
    public bool AutoUpdate { get; set; } = true;
    public bool HardwareAcceleration { get; set; } = true;
    public bool Analytics { get; set; } = false;


    // ============ Promosyon Kodu / Süreli Premium ============

    /// <summary>
    /// Developer tarafından sağlanabilecek uzak promosyon kodu JSON adresi.
    /// Boşsa ortam değişkeni veya LicenseService içindeki varsayılan uzak URL kullanılır.
    /// </summary>
    public string? PromoCodeConfigUrl { get; set; }

    /// <summary>
    /// Kullanılan son aktif promosyon kodu.
    /// </summary>
    public string? ActivePromoCode { get; set; }

    /// <summary>
    /// Promosyon ile açılan Premium bitiş zamanı (UTC). Null ise aktif promosyon yoktur.
    /// </summary>
    public DateTime? PromoPremiumExpiresAtUtc { get; set; }

    /// <summary>
    /// Aynı kodun bu cihazda tekrar tekrar kullanılmasını önlemek için yerel kullanım listesi.
    /// </summary>
    public List<string> RedeemedPromoCodes { get; set; } = new();

    // ============ Senkronizasyon Ayarlari ============

    /// <summary>
    /// Kanal listesi otomatik yenileme sikligi (saat). 0 = Kapali (manuel)
    /// </summary>
    public int ChannelListRefreshFrequencyHours { get; set; } = 0;

    /// <summary>
    /// EPG otomatik yenileme sikligi (saat). 0 = Kapali (manuel)
    /// </summary>
    public int EpgRefreshFrequencyHours { get; set; } = 0;

    /// <summary>
    /// EPG etkin mi?
    /// </summary>
    public bool EpgEnabled { get; set; } = true;

    /// <summary>
    /// EPG için kullanıcının verdiği özel URL (opsiyonel)
    /// </summary>
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

