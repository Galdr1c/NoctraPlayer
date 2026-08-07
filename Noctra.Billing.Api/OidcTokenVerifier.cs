using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Noctra.Billing.Api;

/// <summary>
/// Pub/Sub push OIDC token'ını doğrular (Google OIDC JWKS üzerinden RS256).
/// Pub/Sub push authentication, push aboneliği için yapılandırılan audience'a
/// yönelik Google imzalı bir JWT üretir; endpoint bu token'ı doğrulamadan
/// production'da açık olmamalıdır. JWKS anahtarları 1 saat cache'lenir,
/// bilinmeyen kid görülürse bir kez yeniden çekilir.
/// </summary>
public sealed class OidcTokenVerifier
{
    private const string JwksUrl = "https://www.googleapis.com/oauth2/v3/certs";
    private static readonly TimeSpan JwksCacheDuration = TimeSpan.FromHours(1);

    /// <summary>Bilinmeyen kid'de yeniden çekim en erken bu aralıkla yapılır (flood koruması).</summary>
    private static readonly TimeSpan JwksRefetchMinInterval = TimeSpan.FromSeconds(30);

    /// <summary>İşlenecek en büyük token uzunluğu (16 KB) — dev boyutlu JWT reddedilir.</summary>
    private const int MaxTokenLength = 16 * 1024;

    private readonly HttpClient _http;
    private readonly string _expectedAudience;
    private readonly string _expectedServiceAccountEmail;

    private readonly object _cacheGate = new();
    private List<Jwk>? _keys;
    private DateTime _keysFetchedAtUtc = DateTime.MinValue;
    private DateTime _lastRefetchUtc = DateTime.MinValue;

    public OidcTokenVerifier(HttpClient http, string expectedAudience, string expectedServiceAccountEmail)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _expectedAudience = expectedAudience ?? throw new ArgumentNullException(nameof(expectedAudience));
        _expectedServiceAccountEmail = expectedServiceAccountEmail ??
            throw new ArgumentNullException(nameof(expectedServiceAccountEmail));
    }

    public async Task<bool> VerifyAsync(string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        var token = ParseBearer(authorizationHeader);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Kötü niyetli büyük token'ların decode/memory maliyetini sınırla.
        if (token.Length > MaxTokenLength)
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        // Header: alg RS256 + kid.
        string? kid;
        try
        {
            using var headerJson = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            var root = headerJson.RootElement;
            if (!string.Equals(root.TryGetProperty("alg", out var alg) ? alg.GetString() : null, "RS256", StringComparison.Ordinal))
            {
                return false;
            }

            kid = root.TryGetProperty("kid", out var k) ? k.GetString() : null;
        }
        catch
        {
            return false;
        }

        // Payload: aud / iss / exp / email / email_verified.
        try
        {
            using var payloadJson = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = payloadJson.RootElement;
            var aud = root.TryGetProperty("aud", out var a) ? a.GetString() : null;
            var iss = root.TryGetProperty("iss", out var i) ? i.GetString() : null;
            var exp = root.TryGetProperty("exp", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt64()
                : 0;
            var email = root.TryGetProperty("email", out var em) ? em.GetString() : null;
            var emailVerified = root.TryGetProperty("email_verified", out var ev) &&
                                ev.ValueKind == JsonValueKind.True;

            if (!string.Equals(aud, _expectedAudience, StringComparison.Ordinal))
            {
                return false;
            }

            if (iss is not ("accounts.google.com" or "https://accounts.google.com"))
            {
                return false;
            }

            if (exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return false;
            }

            // Push token'ı beklenen service account imzalamış olmalı ve Google
            // e-postayı doğrulamış olmalı — hangi hesabın gönderdiği kontrolü.
            if (!emailVerified ||
                !string.Equals(email, _expectedServiceAccountEmail, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        // İmza: JWKS'ten kid ile eşleşen RSA anahtarı.
        var key = await FindKeyAsync(kid, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return false;
        }

        var signedData = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");

        // İmza segmenti bozuk base64 içerebilir (kötü niyetli istek); decode
        // dahil bütün hatalar red olarak döner — 401 üretilmeli, retry değil.
        try
        {
            var signature = Base64UrlDecodeBytes(parts[2]);
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Base64UrlDecodeBytes(key.N),
                Exponent = Base64UrlDecodeBytes(key.E)
            });
            return rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private async Task<Jwk?> FindKeyAsync(string? kid, CancellationToken cancellationToken)
    {
        // Önce cache'teki anahtarlar.
        var cached = GetCachedKeys();
        if (cached is not null)
        {
            var hit = cached.FirstOrDefault(k => string.Equals(k.Kid, kid, StringComparison.Ordinal));
            if (hit is not null)
            {
                return hit;
            }

            // Bilinmeyen kid → anahtar döndürmesi olasılığı: cache'i bayatlatıp
            // yeniden çekilir. Refetch penceresi (30 sn) kötü niyetli/geçersiz
            // token flood'unun her çağrıda ağ isteği üretmesini önler.
            lock (_cacheGate)
            {
                if (DateTime.UtcNow - _lastRefetchUtc < JwksRefetchMinInterval)
                {
                    return null;
                }

                _lastRefetchUtc = DateTime.UtcNow;
                _keysFetchedAtUtc = DateTime.MinValue;
            }
        }

        var keys = await FetchKeysAsync(cancellationToken).ConfigureAwait(false);
        return keys.FirstOrDefault(k => string.Equals(k.Kid, kid, StringComparison.Ordinal));
    }

    private List<Jwk>? GetCachedKeys()
    {
        lock (_cacheGate)
        {
            if (_keys is not null && DateTime.UtcNow - _keysFetchedAtUtc < JwksCacheDuration)
            {
                return _keys;
            }

            return null;
        }
    }

    private async Task<List<Jwk>> FetchKeysAsync(CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            // Cache tazeyse tekrar çekilmez; başarısız olursa cache yazılmadığı
            // için bir sonraki çağrı tekrar dener. Not: HTTP GET lock dışında
            // yapılır; çok sayıda eşzamanlı çağrı cache boşken birlikte indirebilir
            // (düşük trafikli backend için kabul edilebilir, yanlış güvence vermez).
            if (_keys is not null && DateTime.UtcNow - _keysFetchedAtUtc < JwksCacheDuration)
            {
                return _keys;
            }
        }

        var keys = new List<Jwk>();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, JwksUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("keys", out var keysElement) &&
                    keysElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var keyElement in keysElement.EnumerateArray())
                    {
                        var jwk = new Jwk(
                            GetString(keyElement, "kid"),
                            GetString(keyElement, "n"),
                            GetString(keyElement, "e"));
                        if (!string.IsNullOrWhiteSpace(jwk.Kid) &&
                            !string.IsNullOrWhiteSpace(jwk.N) &&
                            !string.IsNullOrWhiteSpace(jwk.E))
                        {
                            keys.Add(jwk);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ağ hatası: boş liste döner; çağıran bu çağrıda reddeder, bir
            // sonraki istekte tekrar denenir (fail-closed).
        }

        lock (_cacheGate)
        {
            if (keys.Count > 0)
            {
                _keys = keys;
                _keysFetchedAtUtc = DateTime.UtcNow;
            }
        }

        return keys;
    }

    private static string? ParseBearer(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string scheme = "Bearer ";
        return authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[scheme.Length..].Trim()
            : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Base64UrlDecode(string value)
    {
        var padded = value.Length % 4 == 0 ? value : value + new string('=', 4 - (value.Length % 4));
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/')));
    }

    private static byte[] Base64UrlDecodeBytes(string value)
    {
        var padded = value.Length % 4 == 0 ? value : value + new string('=', 4 - (value.Length % 4));
        return Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
    }

    private sealed record Jwk(string Kid, string N, string E);
}
