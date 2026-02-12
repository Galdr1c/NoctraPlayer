namespace IPTVPlayer.Models;

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
    /// İntro'yu otomatik geç
    /// </summary>
    public bool AutoSkipIntro { get; set; } = false;
    
    /// <summary>
    /// Jenerik bittiğinde sonraki bölüme otomatik geç
    /// </summary>
    public bool AutoSkipCredits { get; set; } = false;
    
    /// <summary>
    /// Veri kullanımı / video kalitesi
    /// </summary>
    public DataUsageLevel DataUsage { get; set; } = DataUsageLevel.Auto;
    
    /// <summary>
    /// Varsayılan ses seviyesi (0-100)
    /// </summary>
    public int DefaultVolume { get; set; } = 100;
    
    /// <summary>
    /// Son kanalı hatırla
    /// </summary>
    public bool RememberLastChannel { get; set; } = true;

    /// <summary>
    /// Son kullanilan profili acilista otomatik sec
    /// </summary>
    public bool AutoSelectLastProfile { get; set; } = true;
    
    // ============ Altyazı Ayarları ============
    
    /// <summary>
    /// Varsayılan altyazı dili (tr, en, none)
    /// </summary>
    public string SubtitleLanguage { get; set; } = "tr";
    
    /// <summary>
    /// Altyazı yazı boyutu (12-32)
    /// </summary>
    public int SubtitleFontSize { get; set; } = 20;
    
    /// <summary>
    /// Altyazı arka plan opaklığı (0-100)
    /// </summary>
    public int SubtitleBackgroundOpacity { get; set; } = 50;
    
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
    
    // ============ Görünüm Ayarları ============
    
    /// <summary>
    /// Koyu tema aktif mi
    /// </summary>
    public bool IsDarkTheme { get; set; } = true;
    
    /// <summary>
    /// TMDB API anahtarı
    /// </summary>
    public string? TmdbApiKey { get; set; }

    // ============ Global Ayarlar ============
    
    public string Language { get; set; } = "tr";
    public bool AutoUpdate { get; set; } = true;
    public bool HardwareAcceleration { get; set; } = true;
    public bool Analytics { get; set; } = false;
}
