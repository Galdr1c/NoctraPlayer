using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Noctra.Billing.Api;

var builder = WebApplication.CreateBuilder(args);

// Backend stateless'tir: entitlement verisi saklanmaz, RTDN yoktur, dosya
// tabanlı DB yoktur. Hak kaynağı Google'dur; client her açılışta/resume'da
// purchase token'ı buraya gönderir ve backend Play Developer API'den gerçek
// durumu okur. Google Play erişimi Cloud Run service identity (ADC) ile
// yapılır — private key env'de tutulmaz.
var config = BillingConfig.FromEnvironment();
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
});
builder.Services.AddSingleton<IAccessTokenProvider, GoogleAdcTokenProvider>();
builder.Services.AddSingleton<IPlayBillingApi, PlayBillingApiClient>();
builder.Services.AddSingleton<EntitlementService>();

// Abuse/cost koruması: her IP için 30 istek/dakika. Bu bir yetkilendirme
// mekanizması DEĞİLDİR (gerçek güvenlik Play token doğrulamasıdır) — yalnızca
// rastgele bot taramasının Cloud Run quota/cost tüketmesini sınırlar.
// Bölüm anahtarı X-Forwarded-For'daki gerçek istemci IP'sidir; RemoteIpAddress
// Cloud Run'da proxy'ye ait olduğu için ona bakılsaydı limit global olurdu.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("verify-per-ip", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIpResolver.GetClientIp(
                httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
                httpContext.Connection.RemoteIpAddress?.ToString()),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

app.UseRateLimiter();

// ADC'yi başlangıçta doğrula: Cloud Run service identity / yerel gcloud ADC
// yoksa yanlış yapılandırma ilk /verify isteğinde (her istekte 502) değil,
// deploy anında yüzeye çıksın (fail-fast).
try
{
    app.Services.GetRequiredService<IAccessTokenProvider>();
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex,
        "Google ADC (service identity) yapılandırılamadı. Cloud Run'da servise bağlı " +
        "service account olmalı; yerelde gcloud auth application-default login.");
    throw;
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ==========================================
// Satın alma token'ı doğrulama (tek uç nokta)
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
        var result = await service.VerifyAsync(request, cancellationToken);
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
}).RequireRateLimiting("verify-per-ip");

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
