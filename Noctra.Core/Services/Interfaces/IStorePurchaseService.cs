using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// Platform mağazası (Android'de Google Play Billing) üzerinden Premium ürün
/// listeleme, satın alma ve hak doğrulama servisi. Yalnızca mağaza desteği
/// olan platformlarda kayıtlıdır; diğer platformlarda null kalır ve
/// LicenseService davranışı değişmez.
/// </summary>
public interface IStorePurchaseService
{
    /// <summary>Bu platformda mağaza satın alımı destekleniyor mu?</summary>
    bool IsSupported { get; }

    /// <summary>Yapılandırılmış Premium ürünlerini fiyatlarıyla birlikte döner.</summary>
    Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>Belirtilen ürün için mağaza satın alma akışını başlatır.</summary>
    Task<StorePurchaseResult> LaunchPurchaseAsync(StoreProduct product, CancellationToken cancellationToken = default);

    /// <summary>Kullanıcının mevcut satın alma haklarını mağazadan doğrular.</summary>
    Task<StoreEntitlement> GetEntitlementAsync(CancellationToken cancellationToken = default);

    /// <summary>Geçmiş satın alımları yeniden senkronize eder (geri yükleme).</summary>
    Task RestorePurchasesAsync(CancellationToken cancellationToken = default);

    /// <summary>Haklar değiştiğinde tetiklenir.</summary>
    event EventHandler? EntitlementChanged;
}
