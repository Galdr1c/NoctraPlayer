using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noctra.Billing.Api;

var builder = WebApplication.CreateBuilder(args);

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
// token'ını Authorization başlığında taşır. Üretimde bu token Google'ın
// açık anahtarlarıyla doğrulanmalıdır (Pub/Sub push authentication). Bu
// endpoint yalnızca mevcut token'ları yeniden doğrulatır ve daima ack
// döndürür — kötü niyetli istek en fazla gereksiz bir Play sorgusu tetikler;
// hak asla bu yolla verilmez (fail-closed: bilinmeyen token işlenmez).
app.MapPost("/billing/google/rtdn", async (
    PubSubPushEnvelope? envelope,
    EntitlementService service,
    HttpRequest http,
    CancellationToken cancellationToken) =>
{
    // Pub/Sub push mesajı — yalnızca "durum değişti" der; gerçek durum
    // subscriptionsv2.get ile tekrar sorgulanır. Hatalarda Pub/Sub yeniden
    // iletir; burada daima ack döndürülür (veri kaybı yerine gecikme).
    try
    {
        if (envelope?.Message?.Data is { Length: > 0 } data)
        {
            var payload = JsonSerializer.Deserialize<RtdnPayload>(DecodeBase64(data));
            if (payload?.SubscriptionNotification is { } notification &&
                !string.IsNullOrWhiteSpace(notification.PurchaseToken) &&
                !string.IsNullOrWhiteSpace(notification.SubscriptionId))
            {
                await service.ReverifyByTokenAsync(
                    notification.PurchaseToken,
                    notification.SubscriptionId,
                    cancellationToken);
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "RTDN processing failed");
    }

    return Results.Ok(new { ack = true });
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

/// <summary>RTDN subscriptionNotification zarfı.</summary>
public sealed class RtdnPayload
{
    [JsonPropertyName("packageName")]
    public string? PackageName { get; init; }

    [JsonPropertyName("subscriptionNotification")]
    public RtdnSubscriptionNotification? SubscriptionNotification { get; init; }
}

public sealed class RtdnSubscriptionNotification
{
    [JsonPropertyName("purchaseToken")]
    public string? PurchaseToken { get; init; }

    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; init; }
}
