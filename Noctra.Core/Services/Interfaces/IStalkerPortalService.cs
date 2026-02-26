using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Bir Stalker kategori/genre bilgisini taşır.
/// Veritabanına yazılmaz — sadece hafızada tutulur.
/// </summary>
public class StalkerCategory
{
    public string Id      { get; set; } = string.Empty;
    public string Name    { get; set; } = string.Empty;
    public string Type    { get; set; } = string.Empty; // "itv" | "vod" | "series"
    public int?   Count   { get; set; }
}

public interface IStalkerPortalService
{
    /// <summary>
    /// MAC adresini doğrular.
    /// </summary>
    Task<bool> AuthenticateAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [ESKİ — Geriye dönük uyum için korunuyor]
    /// Tüm kanalları tek seferde çeker. 230k+ hesaplarda çok yavaş.
    /// Yeni kodlar için GetChannelsProgressiveAsync kullanın.
    /// </summary>
    Task<List<Channel>> GetChannelsAsync(
        string portalUrl,
        string macAddress,
        bool includeVod = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// YENİ — Kategori listesini hızlıca döndürür (~500ms).
    /// UI bu sonuçla anında açılabilir.
    /// </summary>
    Task<List<StalkerCategory>> GetCategoriesAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// YENİ — Belirli bir kategorinin kanallarını çeker.
    /// Lazy loading için: kullanıcı o kategoriye girince çağrılır.
    /// </summary>
    Task<List<Channel>> GetChannelsByCategoryAsync(
        string portalUrl,
        string macAddress,
        string categoryId,
        string categoryType,   // "itv" | "vod" | "series"
        CancellationToken cancellationToken = default);

    /// <summary>
    /// YENİ — Kategori bazlı aşamalı yükleme.
    /// İlk adımda tüm kategoriler çekildiğinde `onCategoriesDiscovered` çağrılır (UI'ın anında açılması için).
    /// İkinci parametre (Action<string>), UI'da tıklanan kategoriyi önceliklendirmek için kullanılır.
    /// Geriye döndürülen liste, indirilmesi gereken KATEGORİLERİ temsil etmelidir (filtreleme için).
    /// Her kategori tamamlandığında `onCategoryLoaded` çağrılır.
    /// </summary>
    Task GetChannelsProgressiveAsync(
        string portalUrl,
        string macAddress,
        bool includeVod,
        Func<List<StalkerCategory>, Action<string>, Task<List<StalkerCategory>>> onCategoriesDiscovered,
        Func<List<Channel>, StalkerCategory, Task> onCategoryLoaded,
        IProgress<StalkerLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stalker portalı için olası XMLTV (EPG) URL'sini döndürür.
    /// </summary>
    string GetEpgUrl(string portalUrl);
}

/// <summary>
/// Aşamalı yükleme için ilerleme bilgisi.
/// </summary>
public class StalkerLoadProgress
{
    public int   LoadedChannels  { get; set; }
    public int?  TotalChannels   { get; set; }
    public int   LoadedCategories { get; set; }
    public int   TotalCategories  { get; set; }
    public string CurrentCategory { get; set; } = string.Empty;

    public string Message =>
        TotalChannels.HasValue
            ? $"{CurrentCategory} • {LoadedChannels:N0} / {TotalChannels.Value:N0} içerik"
            : $"{CurrentCategory} • {LoadedChannels:N0} içerik yüklendi ({LoadedCategories}/{TotalCategories} kategori)";
}
