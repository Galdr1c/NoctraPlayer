using IPTVPlayer.Models;

namespace IPTVPlayer.Services;

/// <summary>
/// Uygulama ayarlarını yöneten servis interface'i
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Mevcut ayarlar
    /// </summary>
    AppSettings Settings { get; }
    
    /// <summary>
    /// Ayarları dosyadan yükler
    /// </summary>
    Task LoadAsync();
    
    /// <summary>
    /// Ayarları dosyaya kaydeder
    /// </summary>
    Task SaveAsync();
    
    /// <summary>
    /// Ayarları varsayılana sıfırlar
    /// </summary>
    void ResetToDefaults();
    
    /// <summary>
    /// Ayar değiştiğinde tetiklenir
    /// </summary>
    event Action? SettingsChanged;
}
