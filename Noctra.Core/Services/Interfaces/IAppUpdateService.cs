namespace Noctra.Services.Interfaces;

/// <summary>
/// Uygulama güncelleme servisi arayüzü.
/// Platforma göre farklı implementasyonlar kullanılır:
/// - Android: Google Play In-App Updates (flexible/immediate)
/// - Windows Store: StoreContext API
/// - Debug/Unpackaged: NoOp (güncelleme kontrolü yapmaz)
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// Güncelleme kontrolü yapar. Sonuç her zaman döner (hata dahil).
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Güncelleme işlemini başlatır (kullanıcıya mağaza sayfası açar veya
    /// uygulama içi güncelleme başlatır).
    /// </summary>
    /// <returns>True eğer güncelleme başladıysa, false iptal/hata.</returns>
    Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flexible update indirme tamamlandığında çağrılır.
    /// Kullanıcı "yeniden başlat ve yükle" onayı verdikten sonra kullanılmalı.
    /// </summary>
    Task<bool> CompleteUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uygulama foreground'a döndüğünde bekleyen indirilmiş güncellemeyi kontrol eder.
    /// Flexible update tamamlanmış ama henüz complete edilmemiş olabilir.
    /// </summary>
    Task<UpdateCheckResult> CheckPendingUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Güncelleme durumu değişikliklerini ViewModel'e iletmek için event.
    /// Android flexible/immediate akışından ve Microsoft Store indirme/kurulum
    /// işleminden tetiklenir. Event farklı bir thread'den gelebilir; UI katmanı
    /// kendi dispatcher'ına geçmelidir.
    /// </summary>
    event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;
}

/// <summary>
/// Güncelleme durumu değişiklik event'i için event argümanları
/// </summary>
public sealed class UpdateStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Yeni durum kodu
    /// </summary>
    public UpdateCheckStatus Status { get; }

    /// <summary>
    /// İndirme ilerlemesi (yüzde, varsa)
    /// </summary>
    public double? ProgressPercent { get; }

    /// <summary>
    /// İndirilen byte (varsa)
    /// </summary>
    public long? BytesDownloaded { get; }

    /// <summary>
    /// Toplam byte (varsa)
    /// </summary>
    public long? TotalBytes { get; }

    /// <summary>
    /// Hata mesajı (varsa)
    /// </summary>
    public string? ErrorMessage { get; }

    public UpdateStateChangedEventArgs(UpdateCheckStatus status)
    {
        Status = status;
    }

    public UpdateStateChangedEventArgs(UpdateCheckStatus status, double progressPercent)
    {
        Status = status;
        ProgressPercent = progressPercent;
    }

    public UpdateStateChangedEventArgs(UpdateCheckStatus status, string errorMessage)
    {
        Status = status;
        ErrorMessage = errorMessage;
    }

    public UpdateStateChangedEventArgs(long bytesDownloaded, long totalBytes)
    {
        Status = UpdateCheckStatus.Downloading;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
        if (totalBytes > 0)
            ProgressPercent = Math.Round((double)bytesDownloaded / totalBytes * 100, 1);
    }
}

/// <summary>
/// Güncelleme kontrol durumu
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>Uygulama güncel, güncelleme yok.</summary>
    UpToDate,

    /// <summary>Yeni güncelleme mevcut.</summary>
    UpdateAvailable,

    /// <summary>Kontrol sırasında hata oluştu (ağ, API vb.).</summary>
    Error,

    /// <summary>Bu platform güncelleme desteklemiyor (sideload vb.).</summary>
    Unsupported,

    /// <summary>Güncelleme indiriliyor (flexible update).</summary>
    Downloading,

    /// <summary>Güncelleme indirildi, yükleme bekliyor (kullanıcı onayı gerekli).</summary>
    Downloaded,

    /// <summary>Güncelleme iptal edildi.</summary>
    Canceled
}

/// <summary>
/// Güncelleme kontrol sonucu
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>
    /// Kontrol durumu (güncel, güncelleme var, hata vb.)
    /// </summary>
    public UpdateCheckStatus Status { get; init; }

    /// <summary>
    /// Güncelleme mevcut mu? (Status == UpdateAvailable için kısayol)
    /// </summary>
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;

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

    /// <summary>
    /// Hata mesajı (Status == Error için)
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Windows Store modal dialog'ları için pencere handle'ı sağlayıcı.
/// StoreContext.RequestDownloadAndInstallStorePackageUpdatesAsync
/// desktop'ta HWND bağlantısı gerektirir.
/// </summary>
public interface IWindowHandleProvider
{
    /// <summary>
    /// Ana uygulama penceresinin handle'ı (HWND veya IntPtr)
    /// </summary>
    IntPtr WindowHandle { get; }
}
