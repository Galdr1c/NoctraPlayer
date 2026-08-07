using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Noctra.Billing.Api.Tests;

/// <summary>
/// OidcTokenVerifier — Pub/Sub push OIDC token'ını Google'ın JWKS anahtarlarıyla
/// (RS256) doğrular. Testler gerçek bir RSA anahtarı üretir, JWKS yanıtını script
/// eder ve token'ı o anahtarla imzalar; böylece production şeması birebir test edilir.
/// </summary>
public sealed class OidcTokenVerifierTests
{
    private const string Audience = "https://pubsub.example.com/noctra-rtdn";
    private const string Kid = "test-jwks-key";

    [Fact]
    public void ValidToken_SignedByJwksKey_IsAccepted()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.True(verifier.VerifyAsync($"Bearer {token}").GetAwaiter().GetResult());
    }

    [Fact]
    public void TamperedSignature_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(5));
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{InvertLastChar(parts[2])}";

        Assert.False(verifier.VerifyAsync($"Bearer {tampered}").GetAwaiter().GetResult());
    }

    [Fact]
    public void WrongAudience_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var token = CreateToken(rsa, aud: "some-other-audience", exp: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(verifier.VerifyAsync($"Bearer {token}").GetAwaiter().GetResult());
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.False(verifier.VerifyAsync($"Bearer {token}").GetAwaiter().GetResult());
    }

    [Fact]
    public void MissingOrMalformedBearer_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(verifier.VerifyAsync(null).GetAwaiter().GetResult());
        Assert.False(verifier.VerifyAsync("Basic abc").GetAwaiter().GetResult());
        Assert.False(verifier.VerifyAsync($"Bearer {token.Split('.')[0]}").GetAwaiter().GetResult());
    }

    [Fact]
    public void MalformedSignatureSegment_IsRejectedWithoutThrowing()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(5));
        var parts = token.Split('.');

        // Base64 alfabesinin dışında karakter içeren imza: FormatException
        // üretmemeli (401 = retry değil, red anlamına gelir).
        var malformed = $"{parts[0]}.{parts[1]}." + "!!!not-base64!!!";

        Assert.False(verifier.VerifyAsync($"Bearer {malformed}").GetAwaiter().GetResult());
    }

    [Fact]
    public void OversizedToken_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var http = HttpWithJwks(rsa);
        var verifier = new OidcTokenVerifier(http, Audience);

        var huge = new string('a', 200 * 1024);

        Assert.False(verifier.VerifyAsync($"Bearer {huge}.{huge}.{huge}").GetAwaiter().GetResult());
    }

    [Fact]
    public void UnknownKid_FetchesJwksAgain_AndAcceptsKey()
    {
        using var rsa = RSA.Create(2048);
        // İlk JWKS isteği anahtarı bilinmeyen bir kid ile döndürür; cache'te
        // kid bulunamayınca verifier yeniden çeker — ikinci istek gerçek kid'i verir.
        var firstRequest = true;
        var handler = new ScriptedHttpMessageHandler(_ =>
        {
            if (firstRequest)
            {
                firstRequest = false;
                return Task.FromResult(JsonKeys(new[] { ("other-kid", rsa) }));
            }

            return Task.FromResult(JsonKeys(new[] { (Kid, rsa) }));
        });

        var verifier = new OidcTokenVerifier(new HttpClient(handler), Audience);
        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(5));

        // İlk çağrı: cache boş → ilk çekim other-kid → kid eşleşmez → red.
        Assert.False(verifier.VerifyAsync($"Bearer {token}").GetAwaiter().GetResult());

        // İkinci çağrı: cache'te kid yok → JWKS yeniden çekilir → gerçek kid → kabul.
        Assert.True(verifier.VerifyAsync($"Bearer {token}").GetAwaiter().GetResult());
    }

    [Fact]
    public void JwksUnreachable_FailsClosed()
    {
        using var rsa = RSA.Create(2048);
        var handler = new ScriptedHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var verifier = new OidcTokenVerifier(new HttpClient(handler), Audience);
        var token = CreateToken(rsa, aud: Audience, exp: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(verifier.VerifyAsync($"Bearer {token}").GetAwaiter().GetResult());
    }

    // ===== Yardımcılar =====

    private static HttpClient HttpWithJwks(RSA rsa)
    {
        var handler = new ScriptedHttpMessageHandler(_ =>
            Task.FromResult(JsonKeys(new[] { (Kid, rsa) })));
        return new HttpClient(handler);
    }

    private static HttpResponseMessage JsonKeys(IEnumerable<(string Kid, RSA Rsa)> keys)
    {
        var keyObjects = keys.Select(k => new
        {
            kty = "RSA",
            use = "sig",
            kid = k.Kid,
            alg = "RS256",
            n = Base64UrlEncode(k.Rsa.ExportParameters(false).Modulus!),
            e = Base64UrlEncode(k.Rsa.ExportParameters(false).Exponent!)
        });

        var json = JsonSerializer.Serialize(new { keys = keyObjects });
        return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, json);
    }

    /// <summary>Gerçek RS256 imzalı JWT üretir (header + payload + signature).</summary>
    private static string CreateToken(RSA rsa, string aud, DateTimeOffset exp)
    {
        var header = JsonSerializer.Serialize(new { alg = "RS256", kid = Kid, typ = "JWT" });
        var payload = JsonSerializer.Serialize(new
        {
            aud,
            iss = "accounts.google.com",
            exp = exp.ToUnixTimeSeconds(),
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";

        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string InvertLastChar(string value) =>
        value[..^1] + (value[^1] == 'A' ? 'B' : 'A');

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
