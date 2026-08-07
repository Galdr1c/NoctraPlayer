using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
/// Play Developer API'ye service account (OAuth2 JWT) ile bağlanan istemci.
///
/// - Aylık abonelik: purchases.subscriptionsv2.get → lineItems.expiryTime
///   (gerçek bitiş — client tarafında asla hesaplanmaz).
/// - Tek seferlik paket: purchases.products.get (ProductPurchase).
///
/// Service account JSON'i hiçbir zaman istemciye/APK'ya konmaz; yalnızca
/// sunucu ortamında (env secret) tutulur.
/// </summary>
public sealed class PlayBillingApiClient : IPlayBillingApi
{
    private const string AndroidPublisherScope = "https://www.googleapis.com/auth/androidpublisher";
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    private const string ApiBase = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly HttpClient _http;
    private readonly BillingConfig _config;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private ServiceAccountCredentials? _credentials;
    private string? _cachedAccessToken;
    private DateTime _accessTokenExpiresAtUtc = DateTime.MinValue;

    public PlayBillingApiClient(HttpClient http, BillingConfig config)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
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

        var accessToken = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
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
        var url = $"{ApiBase}/{Uri.EscapeDataString(packageName)}/purchases/subscriptionsv2/{Uri.EscapeDataString(purchaseToken)}";
        var body = await GetJsonAsync(url, accessToken, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            // 404: token bu paket için geçerli değil → fail-closed inaktif.
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
        var autoRenewing = GetBool(root, "autoRenewing") ?? false;

        // Gerçek bitiş: lineItems[].expiryTime (Google'ın verdiği değer).
        DateTime? expiry = null;
        var productMatched = false;
        string? activeOfferId = null;
        if (root.TryGetProperty("lineItems", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in lineItems.EnumerateArray())
            {
                // subscriptionsv2.get URL'inde ürün yoktur (yalnızca token);
                // gerçek ürün yalnızca Google'ın cevabındaki lineItems[].productId'dir.
                // İstemcinin gönderdiği productId'ye asla güvenilmez — token başka
                // bir aboneliğe aitse (örn. daha ucuz bir ürün) hak verilmez.
                if (string.Equals(GetString(item, "productId"), productId, StringComparison.Ordinal))
                {
                    productMatched = true;
                    // Kullanıcının şu an üzerinde olduğu offer (trial offer da
                    // dahil) — trial tespiti için monetization detaylarıyla
                    // eşleştirilir.
                    activeOfferId = GetString(item, "offerId");
                }

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
            }
        }

        // Ürün eşleşmezse (token başka ürüne ait) veya durum hak vermiyorsa
        // fail-closed inaktif — durum bilgisi tanı için yine de korunur.
        var isActive = productMatched &&
                       IsEntitledSubscriptionState(state) &&
                       expiry.HasValue &&
                       expiry.Value > DateTime.UtcNow;

        // Trial tespiti best-effort'tur: aktif abonelik ve eşleşen offerId
        // varsa monetization product details'ten recurrenceMode doğrulanır;
        // ürün detayı çözülemezse (ağ/404/şema) false kalır ve hak asla
        // bu yüzden engellenmez.
        var isTrialPeriod = isActive &&
                            !string.IsNullOrWhiteSpace(activeOfferId) &&
                            await IsTrialOfferAsync(productId, activeOfferId, packageName, accessToken, cancellationToken)
                                .ConfigureAwait(false);

        return new PlayPurchaseVerification
        {
            EntitlementType = "Subscription",
            IsActive = isActive,
            ExpiresAtUtc = expiry,
            AutoRenewEnabled = autoRenewing,
            State = state,
            IsTrialPeriod = isTrialPeriod
        };
    }

    /// <summary>
    /// Aktif offer'ın trial olup olmadığını monetization product details'ten
    /// doğrular. Play Console'da free trial, faz listesinde NON_RECURRING faz
    /// içeren bir offer'dır (standart abonelik fazları RECURRING'dir).
    /// Kullanıcı trial'dayken lineItems[].offerId trial offer'ı gösterir;
    /// trial bitip standart faza geçince offerId değişir, tespit kendiliğinden
    /// false'a döner. Ürün detayı okunamazsa (ağ, 404, şema değişikliği) false
    /// döner — trial tespiti best-effort'tur ve hak doğrulamasını asla
    /// engellemez.
    /// </summary>
    private async Task<bool> IsTrialOfferAsync(
        string productId,
        string offerId,
        string packageName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{ApiBase}/{Uri.EscapeDataString(packageName)}/monetization/subscriptions/{Uri.EscapeDataString(productId)}";
            var body = await GetJsonAsync(url, accessToken, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                return false;
            }

            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("basePlans", out var basePlans) ||
                basePlans.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var basePlan in basePlans.EnumerateArray())
            {
                if (!basePlan.TryGetProperty("offers", out var offers) ||
                    offers.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var offer in offers.EnumerateArray())
                {
                    if (!string.Equals(GetString(offer, "offerId"), offerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // GERÇEK free trial: sıfır fiyatlı NON_RECURRING faz içerir.
                    // Ücretli tanışma (introductory) offer'ı trial sayılmaz.
                    return HasFreeTrialPhase(offer);
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            // İptal, best-effort tespit hatası değil — çağıranın iptalini saygı
            // göster ve yay (trial değil varsayımına dönüştürme).
            throw;
        }
        catch (Exception ex)
        {
            // Ürün detayı çözülemedi — trial tespiti yapılamaz ama hak asla
            // bu yüzden düşmez; trial değil kabul edilir.
            System.Diagnostics.Debug.WriteLine($"[BillingApi] Trial offer lookup failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Offer GERÇEK bir free trial içeriyor mu? Faz listesinde ÜCRETSİZ
    /// (sıfır fiyat) NON_RECURRING faz olmalıdır. Ücretli tanışma (introductory)
    /// offer'ları da NON_RECURRING'dir ama trial DEĞİLDİR — sıfır fiyat şartı
    /// ikisini ayırır ve ücretli kullanıcı asla "trial" görünmez.
    /// </summary>
    private static bool HasFreeTrialPhase(JsonElement offer)
    {
        if (!offer.TryGetProperty("pricingPhases", out var pricingPhases) ||
            !pricingPhases.TryGetProperty("pricingPhaseList", out var phases) ||
            phases.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var phase in phases.EnumerateArray())
        {
            if (string.Equals(GetString(phase, "recurrenceMode"), "NON_RECURRING", StringComparison.Ordinal) &&
                string.Equals(GetString(phase, "priceAmountMicros"), "0", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEntitledSubscriptionState(string state) => state switch
    {
        "SUBSCRIPTION_STATE_ACTIVE" => true,
        "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => true,
        "SUBSCRIPTION_STATE_ACCOUNT_HOLD" => true,
        "SUBSCRIPTION_STATE_ON_HOLD" => true,
        "SUBSCRIPTION_STATE_PAUSED" => true,
        // İptal edilmiş (canceled) abonelik ödenen sürenin (expiryTime) sonuna
        // kadar erişimi korur — bitiş, expiryTime'dan okunur.
        "SUBSCRIPTION_STATE_CANCELED" => true,
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
        var body = await GetJsonAsync(url, accessToken, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            // 404: token geçersiz → fail-closed inaktif.
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

        // purchaseState: 0 = yayınlanmadı, 1 = satın alındı, 2 = iptal edildi/geri alındı.
        var purchaseState = root.TryGetProperty("purchaseState", out var state) && state.ValueKind == JsonValueKind.Number
            ? state.GetInt32()
            : -1;

        var isActive = purchaseState == 1;

        return new PlayPurchaseVerification
        {
            EntitlementType = "Lifetime",
            IsActive = isActive,
            ExpiresAtUtc = null,
            AutoRenewEnabled = false,
            State = purchaseState switch
            {
                1 => "PRODUCT_PURCHASED",
                2 => "PRODUCT_CANCELED",
                _ => "PRODUCT_NOT_PURCHASED"
            }
        };
    }

    // ==========================================
    // OAuth2 service account (RS256 JWT)
    // ==========================================

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedAccessToken is not null && DateTime.UtcNow < _accessTokenExpiresAtUtc)
        {
            return _cachedAccessToken;
        }

        await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedAccessToken is not null && DateTime.UtcNow < _accessTokenExpiresAtUtc)
            {
                return _cachedAccessToken;
            }

            var credentials = LoadCredentials();
            var now = DateTimeOffset.UtcNow;
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claimSet = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = credentials.ClientEmail,
                scope = AndroidPublisherScope,
                aud = credentials.TokenUri,
                iat = now.ToUnixTimeSeconds(),
                exp = now.AddSeconds(3600).ToUnixTimeSeconds()
            }));
            var unsigned = $"{header}.{claimSet}";
            var signature = SignAssertion(credentials.PrivateKeyPem, Encoding.ASCII.GetBytes(unsigned));
            var assertion = $"{unsigned}.{Base64Url(signature)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, credentials.TokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                })
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"OAuth token isteği başarısız: {(int)response.StatusCode} {Truncate(body, 200)}");
            }

            using var json = JsonDocument.Parse(body);
            var token = GetString(json.RootElement, "access_token")
                        ?? throw new InvalidOperationException("OAuth yanıtında access_token yok.");
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number
                ? exp.GetInt32()
                : 3600;

            _cachedAccessToken = token;
            _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 60));
            return token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private ServiceAccountCredentials LoadCredentials()
    {
        if (_credentials is not null)
        {
            return _credentials;
        }

        string? json;
        if (!string.IsNullOrWhiteSpace(_config.GoogleCredentialsJson))
        {
            json = _config.GoogleCredentialsJson;
        }
        else if (!string.IsNullOrWhiteSpace(_config.GoogleCredentialsPath) && File.Exists(_config.GoogleCredentialsPath))
        {
            json = File.ReadAllText(_config.GoogleCredentialsPath);
        }
        else
        {
            throw new InvalidOperationException(
                "Google service account kimliği yapılandırılmadı. NOCTRA_GOOGLE_CREDENTIALS_JSON veya GOOGLE_APPLICATION_CREDENTIALS ayarlayın.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var clientEmail = GetString(root, "client_email")
                          ?? throw new InvalidOperationException("Service account JSON'inde client_email yok.");
        var privateKey = GetString(root, "private_key")
                         ?? throw new InvalidOperationException("Service account JSON'inde private_key yok.");
        var tokenUri = GetString(root, "token_uri") ?? DefaultTokenUri;

        _credentials = new ServiceAccountCredentials(clientEmail, privateKey, tokenUri);
        return _credentials;
    }

    private static byte[] SignAssertion(string privateKeyPem, byte[] data)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ==========================================
    // HTTP helpers
    // ==========================================

    private async Task<string?> GetJsonAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
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

    private static bool? GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private sealed record ServiceAccountCredentials(string ClientEmail, string PrivateKeyPem, string TokenUri);
}
