using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Playlist otomatik organizasyon servisi
/// Duplicate temizleme, kategorileme, sıralama ve zenginleştirme
/// </summary>
public interface IPlaylistOrganizerService
{
    /// <summary>
    /// Tam organizasyon pipeline'ı çalıştırır
    /// </summary>
    List<Channel> Organize(List<Channel> channels, bool trustProviderTypes = false);

    /// <summary>
    /// Benzer/aynı kanalları kaldırır, yüksek kaliteyi tercih eder
    /// </summary>
    List<Channel> RemoveDuplicates(List<Channel> channels);

    /// <summary>
    /// Kategorisiz kanalları otomatik kategorize eder
    /// </summary>
    void AutoCategorize(List<Channel> channels);

    /// <summary>
    /// Grup isimlerini normalleştirir (SPOR → Spor, News → Haber)
    /// </summary>
    void NormalizeGroupNames(List<Channel> channels);

    /// <summary>
    /// Akıllı sıralama: Grup → Numara → Alfabe
    /// </summary>
    List<Channel> SmartSort(List<Channel> channels);

    /// <summary>
    /// Eksik TvgId'leri kanal adından türetir
    /// </summary>
    void EnrichMetadata(List<Channel> channels);
}


