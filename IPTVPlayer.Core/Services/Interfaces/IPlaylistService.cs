using IPTVPlayer.Models;

namespace IPTVPlayer.Services.Interfaces;

/// <summary>
/// Playlist yönetim servis interface'i
/// </summary>
public interface IPlaylistService
{
    /// <summary>
    /// URL'den playlist ekler
    /// </summary>
    Task<Playlist> AddFromUrlAsync(string name, string url, int? profileId = null);

    /// <summary>
    /// Hazir kanal listesi ile playlist olusturur
    /// </summary>
    Task<Playlist> AddFromChannelsAsync(string name, string sourceUrl, IReadOnlyCollection<Channel> channels, int? profileId = null);
    
    /// <summary>
    /// Dosyadan playlist ekler
    /// </summary>
    Task<Playlist> AddFromFileAsync(string name, string filePath, int? profileId = null);
    
    /// <summary>
    /// Tüm playlistleri getirir (Opsiyonel profil filtresi)
    /// </summary>
    Task<List<Playlist>> GetAllAsync(int? profileId = null);
    
    /// <summary>
    /// Playlist'i günceller (yeniden indirir)
    /// </summary>
    Task<Playlist> RefreshAsync(int playlistId);
    
    /// <summary>
    /// Playlist'i siler
    /// </summary>
    Task DeleteAsync(int playlistId);
    
    /// <summary>
    /// Playlist kanallarını getirir
    /// </summary>
    Task<List<Channel>> GetChannelsAsync(int playlistId);

    /// <summary>
    /// Filtrelenmiş kanalları getirir (lazy loading için)
    /// </summary>
    Task<List<Channel>> GetChannelsFilteredAsync(int playlistId, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, int limit = 1000);

    /// <summary>
    /// Filtrelenmis kanallari sayfali getirir (incremental loading icin)
    /// </summary>
    Task<List<Channel>> GetChannelsFilteredPageAsync(int playlistId, int skip, int take, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false);

    /// <summary>
    /// Sadece grup isimlerini getirir (hızlı başlangıç için)
    /// </summary>
    Task<List<string>> GetGroupsAsync(int playlistId);

    /// <summary>
    /// Kanal sayısını getirir (tümünü yüklemeden)
    /// </summary>
    Task<int> GetChannelCountAsync(int playlistId);
    
    Task UpdateProviderExpirationAsync(int providerId, DateTime expirationDate);

    /// <summary>
    /// Playlist'in EPG verisini yeniler
    /// </summary>
    Task RefreshEpgAsync(int playlistId);
}
