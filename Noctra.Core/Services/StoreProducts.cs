namespace Noctra.Services;

/// <summary>
/// Google Play Console'da tanımlanan Premium ürün kimlikleri.
/// Bu kimliklerin Play Console'daki ürün ayarlarıyla birebir eşleşmesi gerekir:
///  - noctra_premium_monthly → abonelik (subscription)
///  - noctra_premium_lifetime → tek seferlik ürün (in-app product, non-consumable)
/// </summary>
public static class StoreProducts
{
    /// <summary>Aylık abonelik ürün kimliği.</summary>
    public const string MonthlySubscription = "noctra_premium_monthly";

    /// <summary>Tek seferlik kalıcı paket ürün kimliği.</summary>
    public const string LifetimePurchase = "noctra_premium_lifetime";
}
