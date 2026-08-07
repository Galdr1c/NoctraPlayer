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

// Pub/Sub push OIDC doğrulayıcı — yalnızca audience yapılandırıldıysa
// RTDN endpoint'i push token doğrulaması yapar (üretimde zorunlu).
if (!string.IsNullOrWhiteSpace(config.RtdnAudience))
{
    builder.Services.AddSingleton(sp => new OidcTokenVerifier(
        sp.GetRequiredService<HttpClient>(),
        config.RtdnAudience!));
}

var app = builder.Build();

if (string.IsNullOrWhiteSpace(config.RtdnAudience))
{
    // Audience yoksa RTDN doğrulamasız çalışır — üretimde sessizce kalmasın.
    app.Logger.LogWarning(
        "NOCTRA_RTDN_AUDIENCE yapılandırılmadı: RTDN push token doğrulaması KAPALI " +
        "(yalnızca yerel geliştirme için güvenlidir; üretimde Pub/Sub push " +
        "authentication audience değerini set edin).");
}

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
// token'ını Authorization başlığında taşır. NOCTRA_RTDN_AUDIENCE yapılandırıldığında
// endpoint bu token'ı Google'ın açık anahtarlarıyla (JWKS, RS256) doğrular
// (OidcTokenVerifier) ve geçersiz istek 401 döner. Audience yapılandırılmadığı
// sürece yerel geliştirme için doğrulama kapalıdır. Bu endpoint yalnızca mevcut
// token'ları yeniden doğrulatır — hak asla bu yolla verilmez (fail-closed:
// bilinmeyen token işlenmez).
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

        // Abonelik bildirimi: gerçek durum her zaman subscriptionsv2.get ile
        // yeniden sorgulanır.
        if (payload?.SubscriptionNotification is { } subscription &&
            !string.IsNullOrWhiteSpace(subscription.PurchaseToken) &&
            !string.IsNullOrWhiteSpace(subscription.SubscriptionId))
        {
            await service.ReverifyByTokenAsync(
                subscription.PurchaseToken,
                subscription.SubscriptionId,
                cancellationToken);
        }

        // Tek seferlik ürün (lifetime) bildirimi: satın alma/iptal/pending
        // olayları aynı token+sku reverify akışından geçer — hak yalnızca
        // Play'den yeniden okunan gerçek duruma göre güncellenir.
        if (payload?.OneTimeProductNotification is { } oneTime &&
            !string.IsNullOrWhiteSpace(oneTime.PurchaseToken) &&
            !string.IsNullOrWhiteSpace(oneTime.Sku))
        {
            await service.ReverifyByTokenAsync(
                oneTime.PurchaseToken,
                oneTime.Sku,
                cancellationToken);
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

/// <summary>RTDN zarfı — subscription ve one-time product bildirimleri.</summary>
public sealed class RtdnPayload
{
    [JsonPropertyName("packageName")]
    public string? PackageName { get; init; }

    [JsonPropertyName("subscriptionNotification")]
    public RtdnSubscriptionNotification? SubscriptionNotification { get; init; }

    [JsonPropertyName("oneTimeProductNotification")]
    public RtdnOneTimeProductNotification? OneTimeProductNotification { get; init; }
}

public sealed class RtdnSubscriptionNotification
{
    [JsonPropertyName("purchaseToken")]
    public string? PurchaseToken { get; init; }

    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; init; }
}

/// <summary>
/// Tek seferlik ürün (lifetime) bildirimi. notificationType: 1 = CANCELED,
/// 2 = PURCHASED, 3 = ACTIVE, 4 = PENDING — yalnızca reverify tetikleyicisidir;
/// gerçek durum her zaman Play'den yeniden okunur.
/// </summary>
public sealed class RtdnOneTimeProductNotification
{
    [JsonPropertyName("notificationType")]
    public int NotificationType { get; init; }

    [JsonPropertyName("purchaseToken")]
    public string? PurchaseToken { get; init; }

    [JsonPropertyName("sku")]
    public string? Sku { get; init; }
}
