using System.Net;

namespace Noctra.Billing.Api.Tests;

/// <summary>
/// BillingEnvScope süreç geneli ortam değişkenlerini değiştirdiği için onu
/// kullanan bütün test sınıfları aynı xUnit koleksiyonunda seri çalışmalıdır;
/// aksi hâlde paralel sınıflardan birinin Dispose'ı diğerinin env'ini bozar.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BillingEnvCollection : ICollectionFixture<BillingEnvFixture>
{
    public const string Name = "BillingEnv";
}

/// <summary>Koleksiyon düzeyinde kullanılan boş fixture.</summary>
public sealed class BillingEnvFixture
{
}

/// <summary>
/// BillingConfig.FromEnvironment() için gereken ortam değişkenlerini kurar ve
/// test sonunda eski değerlerine döndürür (süreç geneli değişkenler).
/// </summary>
internal sealed class BillingEnvScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();

    public BillingEnvScope()
    {
        Set("NOCTRA_PACKAGE_NAME", "studio.kynora.noctra");
        Set("NOCTRA_SUBSCRIPTION_PRODUCT_IDS", "noctra_premium_monthly");
        Set("NOCTRA_LIFETIME_PRODUCT_IDS", "noctra_premium_lifetime");
        Set("NOCTRA_BILLING_DB_PATH", ":memory:");
        Set("NOCTRA_GOOGLE_CREDENTIALS_JSON", BuildTestServiceAccountJson());

        // RTDN OIDC: BillingConfig fail-fast zorunlu kıldığı için test ortamı
        // audience + service account e-postası verir (disable edilmez).
        Set("NOCTRA_RTDN_AUDIENCE", "https://test-pubsub.example.com/push");
        Set("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL", "push-sa@test-project.iam.gserviceaccount.com");
        Set("NOCTRA_RTDN_DISABLED", null);
    }

    /// <summary>Çalışma anında gerçek bir RSA anahtarıyla service account JSON'i üretir.</summary>
    private static string BuildTestServiceAccountJson()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "test-project",
            private_key_id = "key-id",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            client_email = "billing@test-project.iam.gserviceaccount.com",
            client_id = "12345",
            auth_uri = "https://accounts.google.com/o/oauth2/auth",
            token_uri = "https://oauth2.googleapis.com/token"
        });
    }

    private void Set(string name, string? value)
    {
        _previous[name] = Environment.GetEnvironmentVariable(name);
        if (value is null)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        else
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var pair in _previous)
        {
            if (pair.Value is null)
            {
                Environment.SetEnvironmentVariable(pair.Key, null);
            }
            else
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}

/// <summary>URL'ye göre yanıt döndüren basit scripted HTTP handler.</summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public ScriptedHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public static ScriptedHttpMessageHandler TokenPlus(
        Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(async request =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"access_token":"test-access-token","expires_in":3600,"token_type":"Bearer"}""");
            }

            return responder(request);
        });

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        _responder(request);
}
