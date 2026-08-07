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

    /// <summary>Yalnızca Play API isteklerine yanıt verir (token üretimi fake'lenir).</summary>
    public static ScriptedHttpMessageHandler PlayApi(
        Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(request => Task.FromResult(responder(request)));

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        _responder(request);
}

/// <summary>ADC yerine sabit token dönen fake — token üretimi network gerektirmez.</summary>
internal sealed class FixedAccessTokenProvider : IAccessTokenProvider
{
    private readonly string _token;

    public FixedAccessTokenProvider(string token = "test-access-token")
    {
        _token = token;
    }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_token);
}
