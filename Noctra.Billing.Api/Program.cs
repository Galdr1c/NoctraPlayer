using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noctra.Billing.Api;

var builder = WebApplication.CreateBuilder(args);

// BİLİNEN SINIRLAMA (bilinçli ertelendi): EntitlementStore dosya tabanlı
// SQLite kullanır. Cloud Run container dosya sistemi geçicidir (RAM tabanlı,
// instance kapanınca/scale-to-zero'da veri gider, instance'lar bağımsızdır).
// Client doğrulama akışı her istekte Play'e sorgu attığı için ÇEKİRDEK hak
// akışı bundan etkilenmez; yalnızca RTDN token eşleştirmesi ve
// GET /billing/entitlement listesi bu kayıtlara dayanır. Production için
// shared persistent store (Firestore / Cloud SQL / Supabase Postgres)
// planlanmalıdır.
var config = BillingConfig.FromEnvironment();
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
});
builder.Services.AddSingleton<IPlayBillingApi, PlayBillingApiClient>();
builder.Services.AddSingleton(sp =>
    new EntitlementStore(ConnectionStringFor(sp.GetRequiredService<BillingConfig>())));
builder.Services.AddSingleton<EntitlementService>();

// Pub/Sub push OIDC doğrulayıcı — audience/email BillingConfig tarafında
// fail-fast ile zorunlu kılınır (RTDN açıkken backend doğrulamasız başlamaz).
if (!config.RtdnDisabled)
{
    builder.Services.AddSingleton(sp => new OidcTokenVerifier(
        sp.GetRequiredService<HttpClient>(),
        config.RtdnAudience,
        config.RtdnServiceAccountEmail));
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ==========================================
// Satın alma token'ı doğrulama
// ==========================================
app.MapPost("/billing/google/verify", async (
    BillingVerifyRequest request,
    EntitlementService service,
    HttpRequest http,
    CancellationToken cancellationToken) =>
{
    if (!ValidateApiKey(http, config))
    {
        return Results.Unauthorized();
    }

    try
    {
        var result = await service.VerifyAndStoreAsync(request, cancellationToken);
        return Results.Ok(result);
    }
    catch (BillingRequestException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Purchase verification failed");
        return Results.Json(
            new { error = "Doğrulama hizmeti şu anda kullanılamıyor." },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

// ==========================================
// Kurulum başına kayıtlı entitlement listesi
// ==========================================
app.MapGet("/billing/entitlement", async (
    string installationId,
    EntitlementStore store,
    HttpRequest http,
    CancellationToken cancellationToken) =>
{
    if (!ValidateApiKey(http, config))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(installationId))
    {
        return Results.BadRequest(new { error = "installationId gerekli." });
    }

    var rows = await store.GetAllAsync(installationId, cancellationToken);
    return Results.Ok(rows);
});

// ==========================================
// Google Play RTDN (Pub/Sub push) — opsiyonel
// ==========================================
// Güvenlik notu: Pub/Sub push mesajları, push aboneliği için üretilen OIDC
// token'ını Authorization başlığında taşır. Endpoint bu token'ı Google'ın açık
// anahtarlarıyla (JWKS, RS256) doğrular (OidcTokenVerifier) — aud, iss, exp,
// email ve email_verified claim'leri; geçersiz istek 401 döner. Audience ve
// service account e-postası BillingConfig'te fail-fast ile zorunludur; RTDN
// kullanılmayacaksa NOCTRA_RTDN_DISABLED=1 ile açıkça kapatılır. Bu endpoint
// yalnızca mevcut token'ları yeniden doğrulatır — hak asla bu yolla verilmez
// (fail-closed: bilinmeyen token işlenmez).
//
// Pub/Sub push sözleşmesi: 2xx → mesaj acknowledge edilir, 5xx → yeniden
// iletilir. Bu nedenle yalnızca GERÇEK geçici hatalarda (Play API 503 vb.)
// 503 dönülür; bozuk/alakasız zarf ve bilinmeyen token 2xx ile ack edilir
// (retry anlamsızdır — veri kaybı değil gecikme).
app.MapPost("/billing/google/rtdn", async (
    PubSubPushEnvelope? envelope,
    EntitlementService service,
    OidcTokenVerifier? oidc,
    HttpRequest http,
    CancellationToken cancellationToken) =>
{
    // Pub/Sub push authentication: audience yapılandırıldıysa token zorunludur.
    // Google imzalı JWT (RS256) Google'ın açık anahtarlarıyla doğrulanır;
    // doğrulanamayan istek retry edilmez (401) ve işlenmez.
    if (oidc is not null)
    {
        var authorized = await oidc.VerifyAsync(
            http.Headers.Authorization.ToString(),
            cancellationToken);
        if (!authorized)
        {
            return Results.Unauthorized();
        }
    }

    try
    {
        var data = envelope?.Message?.Data;
        if (string.IsNullOrEmpty(data))
        {
            // Boş zarf: işlenecek bir şey yok → ack.
            return Results.NoContent();
        }

        RtdnPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RtdnPayload>(DecodeBase64(data));
        }
        catch (Exception)
        {
            // Bozuk base64/JSON — yeniden deneme başarıyı getirmez; ack.
            return Results.NoContent();
        }

        // packageName Google'ın hangi uygulamaya ait olduğunu belirttiği
        // alandır — başka uygulamanın bildirimi işlenmez (ack, retry yok).
        if (!string.Equals(
                payload?.PackageName,
                config.PackageName,
                StringComparison.Ordinal))
        {
            return Results.NoContent();
        }

        // Abonelik bildirimi: gerçek durum her zaman subscriptionsv2.get ile
        // yeniden sorgulanır. subscriptionId Google tarafından deprecated
        // edildiği için ürün kimliği store kayıtlarından türetilir.
        if (payload?.SubscriptionNotification is { PurchaseToken: { } subscriptionToken } &&
            !string.IsNullOrWhiteSpace(subscriptionToken))
        {
            await service.ReverifyByTokenAsync(subscriptionToken, cancellationToken);
        }

        // Tek seferlik ürün (lifetime) bildirimi: satın alma/iptal olayları
        // aynı token reverify akışından geçer — hak yalnızca Play'den yeniden
        // okunan gerçek duruma göre güncellenir (sku deprecated: store'dan).
        if (payload?.OneTimeProductNotification is { PurchaseToken: { } oneTimeToken } &&
            !string.IsNullOrWhiteSpace(oneTimeToken))
        {
            await service.ReverifyByTokenAsync(oneTimeToken, cancellationToken);
        }

        // Voided/refund bildirimi: tamamlanmış bir satın almanın sonradan
        // iadesi/iptali. productType (0=in-app, 1=subscription) burada karar
        // vermek için kullanılmaz — gerçek durum Play'den yeniden okunur.
        if (payload?.VoidedPurchaseNotification is { PurchaseToken: { } voidedToken } &&
            !string.IsNullOrWhiteSpace(voidedToken))
        {
            await service.ReverifyByTokenAsync(voidedToken, cancellationToken);
        }

        return Results.NoContent();
    }
    catch (Exception ex)
    {
        // Geçici teknik hata (Play API geçici 503, ağ vb.): Pub/Sub yeniden
        // iletsin — mesaj sessizce düşürülmez.
        app.Logger.LogError(ex, "RTDN processing failed; Pub/Sub will retry");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static bool ValidateApiKey(HttpRequest http, BillingConfig config)
{
    if (string.IsNullOrWhiteSpace(config.ApiKey))
    {
        return true; // Anahtar yapılandırılmamışsa açık (yalnız HTTPS üzerinden güvenli).
    }

    var provided = http.Headers["X-Noctra-Billing-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(provided))
    {
        return false;
    }

    var expected = Encoding.UTF8.GetBytes(config.ApiKey);
    var actual = Encoding.UTF8.GetBytes(provided);
    return expected.Length == actual.Length &&
           CryptographicOperations.FixedTimeEquals(expected, actual);
}

static string ConnectionStringFor(BillingConfig config) =>
    $"Data Source={config.DatabasePath}";

static string DecodeBase64(string value)
{
    var padded = value.Length % 4 == 0 ? value : value + new string('=', 4 - (value.Length % 4));
    return Encoding.UTF8.GetString(Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/')));
}

/// <summary>RTDN zarfı — subscription, one-time product ve voided bildirimleri.</summary>
public sealed class RtdnPayload
{
    [JsonPropertyName("packageName")]
    public string? PackageName { get; init; }

    [JsonPropertyName("subscriptionNotification")]
    public RtdnSubscriptionNotification? SubscriptionNotification { get; init; }

    [JsonPropertyName("oneTimeProductNotification")]
    public RtdnOneTimeProductNotification? OneTimeProductNotification { get; init; }

    [JsonPropertyName("voidedPurchaseNotification")]
    public RtdnVoidedPurchaseNotification? VoidedPurchaseNotification { get; init; }
}

/// <summary>
/// Abonelik bildirimi. Google subscriptionId alanını deprecated etti ve yerine
/// yenisini koymadı — ürün kimliği bu modelden okunmaz, store kayıtlarından
/// türetilir (ReverifyByTokenAsync). notificationType yalnızca bilgidir;
/// gerçek durum her zaman subscriptionsv2.get ile yeniden sorgulanır.
/// </summary>
public sealed class RtdnSubscriptionNotification
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("notificationType")]
    public int NotificationType { get; init; }

    [JsonPropertyName("purchaseToken")]
    public string? PurchaseToken { get; init; }
}

/// <summary>
/// Tek seferlik ürün (lifetime) bildirimi. Google notificationType tanımı:
/// 1 = ONE_TIME_PRODUCT_PURCHASED, 2 = ONE_TIME_PRODUCT_CANCELED. sku alanı
/// da deprecated — ürün kimliği store'dan türetilir; gerçek durum Play'den
/// yeniden okunur (yalnızca reverify tetikleyicisidir).
/// </summary>
public sealed class RtdnOneTimeProductNotification
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("notificationType")]
    public int NotificationType { get; init; }

    [JsonPropertyName("purchaseToken")]
    public string? PurchaseToken { get; init; }
}

/// <summary>
/// Voided purchase bildirimi — tamamlanmış bir satın almanın sonradan iadesi
/// veya iptali (refund, chargeback, developer-initiated void). productType:
/// 0 = in-app (lifetime), 1 = subscription; refundType: 1 = user-initiated,
/// 2 = developer-initiated, 3 = fraudulent. Karar bu alanlarla verilmez —
/// gerçek durum her zaman Play'den yeniden okunur.
/// </summary>
public sealed class RtdnVoidedPurchaseNotification
{
    [JsonPropertyName("purchaseToken")]
    public string? PurchaseToken { get; init; }

    [JsonPropertyName("orderId")]
    public string? OrderId { get; init; }

    [JsonPropertyName("productType")]
    public int ProductType { get; init; }

    [JsonPropertyName("refundType")]
    public int RefundType { get; init; }
}
