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
    /// bu değeri zorunlu tutar — production'da sessizce doğrulamasız kalmaması
    /// için eksikse başlangıçta hata verilir (fail-fast).
    /// </summary>
    public string RtdnAudience { get; private init; } = string.Empty;

    /// <summary>
    /// Pub/Sub push aboneliğini imzalayan Google service account e-postası
    /// (OIDC token'ın email + email_verified claim'leriyle eşleştirilir).
    /// Audience gibi zorunludur.
    /// </summary>
    public string RtdnServiceAccountEmail { get; private init; } = string.Empty;

    /// <summary>RTDN hiç kullanılmayacaksa açıkça kapatma (NOCTRA_RTDN_DISABLED=1).</summary>
    public bool RtdnDisabled { get; private init; }

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

        // RTDN auth fail-fast: audience/email eksikse backend doğrulamasız
        // çalışmaz — üretimde "RTDN authentication KAPALI" uyarısıyla sessizce
        // kalmaktan iyidir. RTDN hiç kullanılmayacaksa NOCTRA_RTDN_DISABLED=1.
        var rtdnDisabled = string.Equals(
            Environment.GetEnvironmentVariable("NOCTRA_RTDN_DISABLED"), "1", StringComparison.Ordinal) ||
            string.Equals(
                Environment.GetEnvironmentVariable("NOCTRA_RTDN_DISABLED"), "true", StringComparison.OrdinalIgnoreCase);
        var rtdnAudience = Environment.GetEnvironmentVariable("NOCTRA_RTDN_AUDIENCE") ?? string.Empty;
        var rtdnEmail = Environment.GetEnvironmentVariable("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL") ?? string.Empty;

        if (!rtdnDisabled)
        {
            if (string.IsNullOrWhiteSpace(rtdnAudience))
            {
                throw new InvalidOperationException(
                    "NOCTRA_RTDN_AUDIENCE eksik: RTDN push doğrulaması olmadan backend başlatılmaz. " +
                    "Pub/Sub push aboneliğinde ayarladığınız audience değerini verin veya RTDN " +
                    "kullanmayacaksanız NOCTRA_RTDN_DISABLED=1 ile açıkça kapatın.");
            }

            if (string.IsNullOrWhiteSpace(rtdnEmail))
            {
                throw new InvalidOperationException(
                    "NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL eksik: push aboneliğini imzalayan service " +
                    "account e-postasını verin (OIDC token email claim'i ile eşleşir) veya RTDN " +
                    "kullanmayacaksanız NOCTRA_RTDN_DISABLED=1 ile açıkça kapatın.");
            }
        }

        return new BillingConfig
        {
            PackageName = packageName,
            SubscriptionProductIds = subscriptions,
            LifetimeProductIds = lifetime,
            ApiKey = Environment.GetEnvironmentVariable("NOCTRA_BILLING_API_KEY"),
            DatabasePath = Environment.GetEnvironmentVariable("NOCTRA_BILLING_DB_PATH") ?? "noctra-billing.db",
            RtdnAudience = rtdnAudience,
            RtdnServiceAccountEmail = rtdnEmail,
            RtdnDisabled = rtdnDisabled,
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
