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
}
