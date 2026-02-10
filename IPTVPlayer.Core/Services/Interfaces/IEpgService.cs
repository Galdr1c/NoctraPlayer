using IPTVPlayer.Models;

namespace IPTVPlayer.Services.Interfaces;

/// <summary>
/// EPG (Electronic Program Guide) servis interface'i
/// </summary>
public interface IEpgService
{
    /// <summary>
    /// EPG URL'sinden program bilgilerini yükler
    /// </summary>
    /// <param name="epgUrl">EPG XML URL</param>
    /// <param name="isPrimary">Ana EPG mi? (Evet ise veritabanını temizler)</param>
    /// <param name="channelsForMapping">Yedek EPG için isim eşleşmesi yapılacak kanallar</param>
    Task LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null);
    
    /// <summary>
    /// Kanal için mevcut programı getirir
    /// </summary>
    /// <param name="channelId">tvg-id</param>
    Task<EpgProgram?> GetCurrentProgramAsync(string channelId);
    
    /// <summary>
    /// Kanal için program listesini getirir
    /// </summary>
    /// <param name="channelId">tvg-id</param>
    /// <param name="from">Başlangıç tarihi</param>
    /// <param name="to">Bitiş tarihi</param>
    Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to);
    
    /// <summary>
    /// EPG verisinin güncelliğini kontrol eder
    /// </summary>
    bool IsLoaded { get; }
    
    /// <summary>
    /// Son güncelleme zamanı
    /// </summary>
    DateTime? LastUpdated { get; }
}
