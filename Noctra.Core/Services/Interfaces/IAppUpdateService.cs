namespace Noctra.Services.Interfaces;

/// <summary>
/// Uygulama güncelleme servisi arayüzü.
/// Platforma göre farklı implementasyonlar kullanılır:
/// - Android: Google Play In-App Updates
/// - Windows Store: StoreContext API
/// - Debug/Unpackaged: NoOp (güncelleme kontrolü yapmaz)
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// Güncelleme varsa bilgi döndürür, yoksa null döner.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Güncelleme işlemini başlatır (kullanıcıya mağaza sayfası açar veya
    /// uygulama içi güncelleme başlatır).
    /// </summary>
    Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Güncelleme kontrol sonucu
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>
    /// Güncelleme mevcut mu?
    /// </summary>
    public bool IsUpdateAvailable { get; init; }

    /// <summary>
    /// Güncel sürüm numarası (mağazadaki/en son sürüm)
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// Değişiklik notları
    /// </summary>
    public string? Changelog { get; init; }

    /// <summary>
    /// Zorunlu güncelleme mi?
    /// </summary>
    public bool IsMandatory { get; init; }
}
