using Noctra.Models;

namespace Noctra.Services.Interfaces;

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
    Task<Playlist> AddFromChannelsAsync(string name, string sourceUrl, IReadOnlyCollection<Channel> channels, int? profileId = null, string? detectedEpgUrl = null);
    
    /// <summary>
    /// Stalker aşamalı yükleme için boş playlist oluşturur.
    /// </summary>
    Task<Playlist> CreateEmptyPlaylistAsync(string name, string sourceUrl, int? profileId = null, string? epgUrl = null);

    /// <summary>
    /// Var olan playlist'e kanallar ekler (aşamalı yükleme için).
    /// </summary>
    Task AppendChannelsAsync(int playlistId, IReadOnlyCollection<Channel> channels);

    /// <summary>
    /// Geçici (Dummy) kanalları siler ve yerine gerçek kanalları ekler.
    /// Lazy loading mekanizmasında anlık kategori gösterimi için kullanılır.
    /// </summary>
    Task ReplaceDummyWithRealChannelsAsync(int playlistId, string groupTitle, IReadOnlyCollection<Channel> realChannels);

    /// <summary>
    /// Stalker aşamalı yüklemesinde henüz indirilmemiş (geçici kanalı bulunan) kategorileri döndürür.
    /// </summary>
    Task<List<string>> GetPendingDummyGroupsAsync(int playlistId);

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
    Task<List<Channel>> GetChannelsFilteredAsync(int playlistId, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, int limit = 1000, ChannelSortOrder sortOrder = ChannelSortOrder.NewestFirst, List<string>? hiddenGroups = null);

    /// <summary>
    /// Filtrelenmis kanallari sayfali getirir (incremental loading icin)
    /// </summary>
    Task<List<Channel>> GetChannelsFilteredPageAsync(int playlistId, int skip, int take, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, ChannelSortOrder sortOrder = ChannelSortOrder.NewestFirst, List<string>? hiddenGroups = null);

    /// <summary>
    /// Sadece grup isimlerini getirir (hızlı başlangıç için)
    /// </summary>
    Task<List<string>> GetGroupsAsync(int playlistId);

    /// <summary>
    /// Belirli içerik tipine göre grup isimlerini getirir.
    /// </summary>
    Task<List<string>> GetGroupsByTypeAsync(int playlistId, ChannelType type);

    /// <summary>
    /// Tüm grup ve kanal sayısı verilerini tek seferde (single-pass) getirir
    /// </summary>
    Task<(int TotalCount, List<string> AllGroups, List<string> LiveGroups, List<string> VodGroups, List<string> SeriesGroups)> GetChannelGroupMetadataAsync(int playlistId);

    /// <summary>
    /// Kanal sayısını getirir (tümünü yüklemeden)
    /// </summary>
    Task<int> GetChannelCountAsync(int playlistId);
    
    Task UpdateProviderExpirationAsync(int providerId, DateTime expirationDate);

    /// <summary>
    /// Sağlayıcı hesabının bitiş tarihini temizler (null yapar).
    /// API erişim hatası veya geçersiz URL durumlarında kullanılır.
    /// </summary>
    Task ClearProviderExpirationAsync(int providerId);

    /// <summary>
    /// Playlist'in EPG verisini yeniler
    /// </summary>
    Task RefreshEpgAsync(int playlistId);

    /// <summary>
    /// Tam yenileme için playlist'e ait TÜM kanalları siler.
    /// Sunucudan başarılı yanıt geldikten sonra, yeni dummy kanallar eklenmeden önce çağrılır.
    /// </summary>
    Task DeleteAllChannelsForRefreshAsync(int playlistId);

    /// <summary>
    /// Tüm geçici (Dummy) kanalları siler.
    /// Aşamalı yükleme bittiğinde veya iptal edildiğinde temizlik için kullanılır.
    /// </summary>
    Task DeleteAllDummiesAsync(int playlistId);
    Task ClearRefreshBackupAsync(int playlistId);
}


