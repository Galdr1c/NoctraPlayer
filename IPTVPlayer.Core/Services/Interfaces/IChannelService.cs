using IPTVPlayer.Models;

namespace IPTVPlayer.Services.Interfaces;

/// <summary>
/// Kanal ayarları ve durum yönetimi için servis interface'i
/// </summary>
public interface IChannelService
{
    /// <summary>
    /// Kanal bilgilerini günceller (Favori, Son izlenme vb.)
    /// </summary>
    Task UpdateChannelAsync(Channel channel);
    
    /// <summary>
    /// Kanal ID'sine göre tek bir kanalı getirir
    /// </summary>
    Task<Channel?> GetByIdAsync(int id);
}
