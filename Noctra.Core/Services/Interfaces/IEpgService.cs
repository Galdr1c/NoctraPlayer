using Noctra.Models;

namespace Noctra.Services.Interfaces;

public enum EpgLoadStatus
{
    Idle,
    Downloading,
    Decompressing,
    Parsing,
    Matching,
    Saving,
    Completed,
    Failed
}

public class EpgProgressInfo
{
    public EpgLoadStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public double ProgressPercent { get; set; }
    public int LoadedCount { get; set; }
}

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
    /// <param name="progress">İlerleme raporlama arayüzü</param>
    /// <returns>Yüklenen ve eslesen program sayisi</returns>
    Task<int> LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1, IProgress<EpgProgressInfo>? progress = null);

    /// <summary>
    /// EPG veritabanını temizler
    /// </summary>
    Task ClearEpgAsync();
    
    /// <summary>
    /// Verilen kanal için şu anki programı getirir (Fallback mekanizmasıyla)
    /// </summary>
    /// <param name="channel">Kanal nesnesi</param>
    Task<EpgProgram?> GetCurrentProgramAsync(Channel channel);
    
    /// <summary>
    /// Kanal için program listesini getirir
    /// </summary>
    /// <param name="channelId">tvg-id</param>
    /// <param name="from">Başlangıç tarihi</param>
    /// <param name="to">Bitiş tarihi</param>
    Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to);

    /// <summary>
    /// Gelecek programları getirir
    /// </summary>
    Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5);

    /// <summary>
    /// Bugünün tüm programlarını getirir
    /// </summary>
    Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId);
    
    /// <summary>
    /// EPG verisinin güncelliğini kontrol eder
    /// </summary>
    bool IsLoaded { get; }
    
    /// <summary>
    /// Son güncelleme zamanı
    /// </summary>
    DateTime? LastUpdated { get; }
    
    /// <summary>
    /// Son hata mesajı
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Kaydedilmiş son EPG hatasını temizler
    /// </summary>
    void ClearLastError();

    /// <summary>
    /// Toplam EPG program sayısını getirir (İstatistik için)
    /// </summary>
    Task<int> GetTotalProgramCountAsync();

    /// <summary>
    /// EPG verisi olan benzersiz kanal sayısını getirir (İstatistik için)
    /// </summary>
    Task<int> GetDistinctChannelCountAsync();
}


