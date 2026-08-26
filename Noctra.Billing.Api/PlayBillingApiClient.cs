using System.Net.Http.Headers;
using System.Text.Json;

namespace Noctra.Billing.Api;

/// <summary>Google Play Developer API erişimini soyutlar (test için fake'lenir).</summary>
public interface IPlayBillingApi
{
    Task<PlayPurchaseVerification> VerifyAsync(
        string productId,
        string purchaseToken,
        string packageName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Play Developer API'ye ADC (Application Default Credentials) ile bağlanan
/// istemci — Cloud Run'da service identity kullanılır, private key yoktur.
///
/// - Aylık abonelik: purchases.subscriptionsv2.get → lineItems.expiryTime
///   (gerçek bitiş — client tarafında asla hesaplanmaz).
/// - Tek seferlik paket: purchases.products.get (ProductPurchase).
/// </summary>
public sealed class PlayBillingApiClient : IPlayBillingApi
{
    private const string ApiBase = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly HttpClient _http;
    private readonly BillingConfig _config;
    private readonly IAccessTokenProvider _tokenProvider;

    public PlayBillingApiClient(
        HttpClient http,
        BillingConfig config,
        IAccessTokenProvider tokenProvider)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async Task<PlayPurchaseVerification> VerifyAsync(
        string productId,
        string purchaseToken,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        if (!_config.IsKnownProduct(productId))
        {
            throw new InvalidOperationException($"Bilinmeyen ürün: {productId}");
        }

        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken)
            .ConfigureAwait(false);
        var entitlementType = _config.ResolveEntitlementType(productId);

        if (entitlementType == "Lifetime")
        {
            return await VerifyLifetimeAsync(productId, purchaseToken, packageName, accessToken, cancellationToken)
                .ConfigureAwait(false);
        }

        return await VerifySubscriptionAsync(productId, purchaseToken, packageName, accessToken, cancellationToken)
            .ConfigureAwait(false);
    }

    // ==========================================
    // Abonelik (subscriptionsv2.get)
    // ==========================================

    private async Task<PlayPurchaseVerification> VerifySubscriptionAsync(
        string productId,
        string purchaseToken,
        string packageName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}/{Uri.EscapeDataString(packageName)}/purchases/subscriptionsv2/tokens/{Uri.EscapeDataString(purchaseToken)}";
        var body = await GetJsonAsync(
                url,
                accessToken,
                cancellationToken,
                operation: "subscription")
            .ConfigureAwait(false);
        if (body is null)
        {
            // 404: token bu paket için geçerli değil → fail-closed inaktif.
            Console.WriteLine(
                $"[Billing] subscription verification returned 404 product={productId}");
            return new PlayPurchaseVerification
            {
                EntitlementType = "Subscription",
                IsActive = false,
                ExpiresAtUtc = null,
                AutoRenewEnabled = false,
                State = "SUBSCRIPTION_STATE_EXPIRED"
            };
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var state = GetString(root, "subscriptionState") ?? string.Empty;

        // Her line item kendi expiry/auto-renew/offer faz bilgisini taşır.
        // subscriptionsv2.get URL'inde ürün yoktur (yalnızca token); gerçek ürün
        // yalnızca Google'ın cevabındaki lineItems[].productId'dir. İstemcinin
        // gönderdiği productId'ye asla güvenilmez — token başka bir aboneliğe
        // aitse (örn. daha ucuz bir ürün) hak verilmez ve o line item istenen
        // hakkın HİÇBİR alanına katkıda bulunmaz.
        DateTime? expiry = null;
        var autoRenewEnabled = false;
        var isTrialPeriod = false;
        var productMatched = false;
        if (root.TryGetProperty("lineItems", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in lineItems.EnumerateArray())
            {
                if (!string.Equals(GetString(item, "productId"), productId, StringComparison.Ordinal))
                {
                    // Farklı ürünün line item'ı (ek ürün, başka plan) — istenen
                    // hakkın expiry/auto-renew/trial alanlarına katkıda bulunmaz.
                    continue;
                }

                productMatched = true;

                // Gerçek bitiş: lineItems[].expiryTime (Google'ın verdiği değer).
                var expiryText = GetString(item, "expiryTime");
                if (expiryText is not null &&
                    DateTime.TryParse(expiryText, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    var candidate = parsed.ToUniversalTime();
                    if (expiry is null || candidate > expiry)
                    {
                        expiry = candidate;
                    }
                }

                // Otomatik yenileme: lineItems[].autoRenewingPlan.autoRenewEnabled.
                // SubscriptionPurchaseV2 ROOT'unda autoRenewing diye bir alan
                // YOKTUR — eski okuma her zaman false döndürürdü.
                if (item.TryGetProperty("autoRenewingPlan", out var autoRenewingPlan) &&
                    autoRenewingPlan.TryGetProperty("autoRenewEnabled", out var autoRenewEnabledValue) &&
                    autoRenewEnabledValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    autoRenewEnabled = autoRenewEnabledValue.GetBoolean();
                }

                // Trial: Google kullanıcının ŞU AN hangi fazda olduğunu offerPhase
                // ile DOĞRUDAN söyler — Monetization API'ye ek istek gerekmez.
                // offerPhase.freeTrial'ın VARLIĞI = kullanıcı free trial fazında;
                // introductoryPrice ve basePrice ayrı faz tipleridir.
                if (item.TryGetProperty("offerPhase", out var offerPhase) &&
                    offerPhase.TryGetProperty("freeTrial", out _))
                {
                    isTrialPeriod = true;
                }
            }
        }

        // Ürün eşleşmezse (token başka ürüne ait) veya durum hak vermiyorsa
        // fail-closed inaktif — durum bilgisi tanı için yine de korunur.
        var isActive = productMatched &&
                       IsEntitledSubscriptionState(state) &&
                       expiry.HasValue &&
                       expiry.Value > DateTime.UtcNow;

        Console.WriteLine(
            $"[Billing] subscription verification product={productId} " +
            $"state={state} matched={productMatched} " +
            $"expiry={expiry?.ToString("O") ?? "none"} active={isActive}");

        return new PlayPurchaseVerification
        {
            EntitlementType = "Subscription",
            IsActive = isActive,
            ExpiresAtUtc = expiry,
            AutoRenewEnabled = autoRenewEnabled,
            State = state,
            // Trial yalnızca AKTİF abonelikte anlamlıdır.
            IsTrialPeriod = isActive && isTrialPeriod
        };
    }

    /// <summary>
    /// Google'ın gerçek erişim kuralları (Play Billing lifecycle):
    ///  - ACTIVE → erişim var.
    ///  - IN_GRACE_PERIOD → erişim devam eder (ödeme gecikti, avantaj sürer).
    ///  - CANCELED → ödenen sürenin (expiryTime) sonuna kadar erişim devam eder.
    ///  - PAUSED → erişim KALDIRILIR (kullanıcı duraklattı; faturalama + erişim durur).
    ///  - ON_HOLD → erişim KALDIRILIR (hesap tutuldu, ödeme sorunu).
    ///  - EXPIRED / PENDING / PENDING_PURCHASE_CANCELED / bilinmeyen → erişim yok.
    /// NOT: "SUBSCRIPTION_STATE_ACCOUNT_HOLD" güncel enum'da YOKTUR; doğru isim
    /// SUBSCRIPTION_STATE_ON_HOLD'dur (bilinmeyen değer zaten fail-closed false).
    /// </summary>
    private static bool IsEntitledSubscriptionState(string state) => state switch
    {
        "SUBSCRIPTION_STATE_ACTIVE" => true,
        "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => true,
        // İptal edilmiş abonelik ödenen sürenin sonuna kadar erişimi korur.
        "SUBSCRIPTION_STATE_CANCELED" => true,
        "SUBSCRIPTION_STATE_PAUSED" => false,
        "SUBSCRIPTION_STATE_ON_HOLD" => false,
        "SUBSCRIPTION_STATE_EXPIRED" => false,
        "SUBSCRIPTION_STATE_PENDING" => false,
        _ => false
    };

    // ==========================================
    // Tek seferlik paket (products.get)
    // ==========================================

    private async Task<PlayPurchaseVerification> VerifyLifetimeAsync(
        string productId,
        string purchaseToken,
        string packageName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}/{Uri.EscapeDataString(packageName)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(purchaseToken)}";
        var body = await GetJsonAsync(
                url,
                accessToken,
                cancellationToken,
                operation: "lifetime")
            .ConfigureAwait(false);
        if (body is null)
        {
            // 404: token geçersiz → fail-closed inaktif.
            Console.WriteLine(
                $"[Billing] lifetime verification returned 404 product={productId}");
            return new PlayPurchaseVerification
            {
                EntitlementType = "Lifetime",
                IsActive = false,
                ExpiresAtUtc = null,
                AutoRenewEnabled = false,
                State = "PRODUCT_NOT_PURCHASED"
            };
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        // purchaseState (Play Developer API purchases.products — BillingClient
        // enum'u DEĞİLDİR): 0 = Purchased, 1 = Canceled, 2 = Pending.
        // Eski kod 1'i "satın alındı" sanıyordu (BillingClient'ın client-side
        // enum değeri) — gerçek lifetime satın alma state=0 geldiği için ters
        // çalışıyor, iptal edilmiş purchase state=1 iken AKTİF görünüyordu.
        var purchaseState = root.TryGetProperty("purchaseState", out var state) && state.ValueKind == JsonValueKind.Number
            ? state.GetInt32()
            : -1;

        var isActive = purchaseState == 0;

        Console.WriteLine(
            $"[Billing] lifetime verification product={productId} " +
            $"purchaseState={purchaseState} active={isActive}");

        return new PlayPurchaseVerification
        {
            EntitlementType = "Lifetime",
            IsActive = isActive,
            ExpiresAtUtc = null,
            AutoRenewEnabled = false,
            State = purchaseState switch
            {
                0 => "PRODUCT_PURCHASED",
                1 => "PRODUCT_CANCELED",
                2 => "PRODUCT_PENDING",
                _ => "PRODUCT_UNKNOWN"
            }
        };
    }

    // ==========================================
    // HTTP helpers
    // ==========================================

    private async Task<string?> GetJsonAsync(
        string url,
        string accessToken,
        CancellationToken cancellationToken,
        string operation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(
                $"[Billing] Play API {operation} request failed status={(int)response.StatusCode} " +
                $"body={Truncate(body, 300)}");
            // 404 = token bu paket/ürün için geçerli değil (iptal edilmiş veya
            // başka pakete ait). Çağıran fail-closed inaktif sonuç üretir;
            // diğer hatalar sunucu hatası olarak fırlatılır.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            throw new InvalidOperationException(
                $"Play API hatası: {(int)response.StatusCode} {Truncate(body, 300)}");
        }

        return body;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
