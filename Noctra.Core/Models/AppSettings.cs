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
/// Uygulama ayarları modeli
/// </summary>
public class AppSettings
{
    // ============ Oynatma Ayarları ============
    
    /// <summary>
    /// Sonraki bölümü otomatik oynat
    /// </summary>
    public bool AutoPlayNext { get; set; } = true;
    
    /// <summary>
    /// Veri kullanımı / video kalitesi
    /// </summary>
    public DataUsageLevel DataUsage { get; set; } = DataUsageLevel.Auto;
    
    /// <summary>
    /// Varsayılan ses seviyesi (0-100)
    /// </summary>
    public int DefaultVolume { get; set; } = 100;

    /// <summary>
    /// Altyazı varsayılan olarak açık mı? (VOD/Dizi için)
    /// </summary>
    public bool SubtitleEnabled { get; set; } = false;

    /// <summary>
    /// Tercih edilen altyazı dili (örn: "tr", "en")
    /// </summary>
    public string SubtitleLanguage { get; set; } = "tr";

    /// <summary>
    /// Tercih edilen ses dili (örn: "tr", "en")
    /// </summary>
    public string PreferredAudioLanguage { get; set; } = "tr";

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
    
    public string Language { get; set; } = "tr";
    public bool AutoUpdate { get; set; } = true;
    public bool HardwareAcceleration { get; set; } = true;
    public bool Analytics { get; set; } = false;

    // ============ Senkronizasyon Ayarlari ============

    /// <summary>
    /// Kanal listesi otomatik yenileme sikligi (saat). 0 = Kapali (manuel)
    /// </summary>
    public int ChannelListRefreshFrequencyHours { get; set; } = 0;

    /// <summary>
    /// EPG otomatik yenileme sikligi (saat). 0 = Kapali (manuel)
    /// </summary>
    public int EpgRefreshFrequencyHours { get; set; } = 24;

    /// <summary>
    /// EPG etkin mi?
    /// </summary>
    public bool EpgEnabled { get; set; } = true;

    /// <summary>
    /// EPG icin kullanicinin verdigi ozel URL (opsiyonel)
    /// </summary>
    public string? CustomEpgUrl { get; set; }

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
}


