using System.Reflection;
using System.Text;
using System.Text.Json;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Doğrulama backend'ine (Noctra.Billing.Api) HTTPS üzerinden bağlanan istemci.
///
/// Uç nokta adresi ve opsiyonel paylaşılan anahtar, promosyon uç noktasıyla
/// aynı güvenlik modelini kullanır: Release'de build-time AssemblyMetadata
/// olarak gömülür (kaynak kodda/ayarlarda tutulmaz), DEBUG'da yalnızca ortam
/// değişkeninden gelir. Kullanıcının değiştirebileceği settings.json alanından
/// asla okunmaz — aksi hâlde kullanıcı kendi doğrulama sunucusunu işaret edip
/// hak üretebilirdi.
/// </summary>
public sealed class HttpBillingVerificationClient : IBillingVerificationClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _apiKey;

    static HttpBillingVerificationClient()
    {
        EnvFileLoader.Load();
    }

    public HttpBillingVerificationClient(HttpClient http)
        : this(http, TrustedVerifyUrl, TrustedApiKey)
    {
    }

    public HttpBillingVerificationClient(HttpClient http, string baseUrl, string? apiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public async Task<BillingVerifiedEntitlement?> VerifyAsync(
        BillingVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl) ||
            string.IsNullOrWhiteSpace(request.PurchaseToken) ||
            string.IsNullOrWhiteSpace(request.ProductId))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/billing/google/verify")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            httpRequest.Headers.Accept.ParseAdd("application/json");
            if (_apiKey is not null)
            {
                httpRequest.Headers.Add("X-Noctra-Billing-Key", _apiKey);
            }

            using var response = await _http.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<BillingVerifiedEntitlement>(body, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BillingVerification] Verify failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Release'te derleme metadata'sından, DEBUG'da ortam değişkeninden.</summary>
    private static string TrustedVerifyUrl
    {
        get
        {
#if DEBUG
            var overrideUrl = Environment.GetEnvironmentVariable("NOCTRA_BILLING_VERIFY_URL");
            if (!string.IsNullOrWhiteSpace(overrideUrl))
            {
                return overrideUrl.Trim();
            }
#endif
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "Noctra.BillingVerifyUrl", StringComparison.Ordinal))
                ?.Value ?? string.Empty;
        }
    }

    private static string? TrustedApiKey
    {
        get
        {
#if DEBUG
            var overrideKey = Environment.GetEnvironmentVariable("NOCTRA_BILLING_API_KEY");
            if (!string.IsNullOrWhiteSpace(overrideKey))
            {
                return overrideKey.Trim();
            }
#endif
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "Noctra.BillingApiKey", StringComparison.Ordinal))
                ?.Value ?? null;
        }
    }
}
