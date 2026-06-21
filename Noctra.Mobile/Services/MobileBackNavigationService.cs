using System;

namespace Noctra.Mobile.Services;

/// <summary>
/// Android donanım/jest geri tuşu ile MainView arasındaki köprü.
/// MainView, <see cref="BackRequested"/> handler'ını kaydeder; MainActivity.OnBackPressed
/// bu servisi çözüp <see cref="HandleBack"/> çağırır. Handler true dönerse geri olayı
/// uygulama içinde tüketilir (varsayılan davranış — uygulamadan çıkış — engellenir).
/// </summary>
public sealed class MobileBackNavigationService
{
    /// <summary>
    /// Geri tuşuna basıldığında çağrılır. Olay tüketildiyse true döndürmelidir.
    /// </summary>
    public Func<bool>? BackRequested { get; set; }

    /// <summary>
    /// Kayıtlı handler'ı çalıştırır. Handler yoksa veya false dönerse false döner,
    /// böylece çağıran taraf varsayılan geri davranışını uygulayabilir.
    /// </summary>
    public bool HandleBack()
    {
        var handler = BackRequested;
        return handler is not null && handler();
    }
}
