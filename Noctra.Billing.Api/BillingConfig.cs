namespace Noctra.Billing.Api;

/// <summary>
/// Backend yapılandırması — yalnızca ortam değişkenlerinden gelir
/// (kodda sabit tutulmaz). Zorunlu: NOCTRA_PACKAGE_NAME, NOCTRA_SUBSCRIPTION_PRODUCT_IDS,
/// NOCTRA_LIFETIME_PRODUCT_IDS ve Google service account kimliği.
/// </summary>
public sealed class BillingConfig
{
    public string PackageName { get; private init; } = string.Empty;

    /// <summary>Abonelik ürün kimlikleri (subscriptionsv2.get ile doğrulanır).</summary>
    public IReadOnlySet<string> SubscriptionProductIds { get; private init; } = new HashSet<string>();

    /// <summary>Tek seferlik ürün kimlikleri (products.get ile doğrulanır).</summary>
    public IReadOnlySet<string> LifetimeProductIds { get; private init; } = new HashSet<string>();

    /// <summary>İstemci→backend çağrıları için opsiyonel paylaşılan sır.</summary>
    public string? ApiKey { get; private init; }

    public string DatabasePath { get; private init; } = "noctra-billing.db";

    /// <summary>
    /// Pub/Sub push aboneliği için yapılandırılan OIDC audience. RTDN endpoint'i
    /// yalnızca bu değer tanımlıysa push token doğrulaması yapar; tanımlı
    /// değilse yerel geliştirme için açık kalır (üretimde tanımlanmalıdır).
    /// </summary>
    public string? RtdnAudience { get; private init; }

    public string? GoogleCredentialsJson { get; private init; }
    public string? GoogleCredentialsPath { get; private init; }

    public static BillingConfig FromEnvironment()
    {
        var packageName = GetRequired("NOCTRA_PACKAGE_NAME");
        var subscriptions = SplitIds(GetRequired("NOCTRA_SUBSCRIPTION_PRODUCT_IDS"));
        var lifetime = SplitIds(GetRequired("NOCTRA_LIFETIME_PRODUCT_IDS"));

        if (subscriptions.Count == 0 && lifetime.Count == 0)
        {
            throw new InvalidOperationException(
                "NOCTRA_SUBSCRIPTION_PRODUCT_IDS ve NOCTRA_LIFETIME_PRODUCT_IDS boş olamaz.");
        }

        return new BillingConfig
        {
            PackageName = packageName,
            SubscriptionProductIds = subscriptions,
            LifetimeProductIds = lifetime,
            ApiKey = Environment.GetEnvironmentVariable("NOCTRA_BILLING_API_KEY"),
            DatabasePath = Environment.GetEnvironmentVariable("NOCTRA_BILLING_DB_PATH") ?? "noctra-billing.db",
            RtdnAudience = Environment.GetEnvironmentVariable("NOCTRA_RTDN_AUDIENCE"),
            GoogleCredentialsJson = Environment.GetEnvironmentVariable("NOCTRA_GOOGLE_CREDENTIALS_JSON"),
            GoogleCredentialsPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")
        };
    }

    public bool IsKnownProduct(string productId) =>
        SubscriptionProductIds.Contains(productId) || LifetimeProductIds.Contains(productId);

    public string ResolveEntitlementType(string productId)
    {
        if (SubscriptionProductIds.Contains(productId))
        {
            return "Subscription";
        }

        if (LifetimeProductIds.Contains(productId))
        {
            return "Lifetime";
        }

        throw new InvalidOperationException($"Bilinmeyen ürün: {productId}");
    }

    private static string GetRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Ortam değişkeni eksik: {name}");
        }

        return value.Trim();
    }

    private static IReadOnlySet<string> SplitIds(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}
