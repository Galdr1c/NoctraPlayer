using Noctra.Models;

namespace Noctra.Services;

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
    /// Belirli bir profilin ayarlarını yükler. profileId 0 ise global ayarları yükler.
    /// </summary>
    Task LoadProfileSettingsAsync(int profileId);

    /// <summary>
    /// Bir profilin ayarlarını aktif hale getirmeden okur.
    /// </summary>
    Task<AppSettings?> PeekProfileSettingsAsync(int profileId);
    
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


