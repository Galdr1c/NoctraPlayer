namespace Noctra.Services.Interfaces;

/// <summary>
/// Uygulama sürüm bilgisini sağlayan servis.
/// Platforma göre doğru assembly veya package bilgisini kullanarak
/// mevcut sürümü döndürür.
/// </summary>
public interface IAppVersionService
{
    /// <summary>
    /// Görünen sürüm metni (örn: "1.1.0")
    /// </summary>
    string DisplayVersion { get; }

    /// <summary>
    /// Derleme numarası (Android: versionCode, Windows: Package Version)
    /// </summary>
    long BuildNumber { get; }
}
