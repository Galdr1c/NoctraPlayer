using System.Net;

namespace Noctra.Billing.Api.Tests;

[Collection(BillingEnvCollection.Name)]
public sealed class PlayBillingApiClientTests
{
    [Fact]
    public async Task VerifyAsync_ActiveSubscription_UsesPlayExpiryTime()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "autoRenewing": true,
                  "acknowledgementState": "ACKNOWLEDGEMENT_STATE_ACKED",
                  "lineItems": [
                    { "productId": "noctra_premium_monthly", "expiryTime": "2026-09-06T15:42:10Z" }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.Equal("Subscription", result.EntitlementType);
        Assert.True(result.IsActive);
        Assert.Equal(new DateTime(2026, 9, 6, 15, 42, 10, DateTimeKind.Utc), result.ExpiresAtUtc);
        Assert.True(result.AutoRenewEnabled);
        Assert.Equal("SUBSCRIPTION_STATE_ACTIVE", result.State);
    }

    [Fact]
    public async Task VerifyAsync_CanceledSubscription_KeepsAccessUntilPaidThroughExpiry()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_CANCELED",
                  "autoRenewing": false,
                  "lineItems": [
                    { "productId": "noctra_premium_monthly", "expiryTime": "2099-01-01T00:00:00Z" }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        // İptal edilmiş abonelik ödenen sürenin sonuna kadar hak verir.
        Assert.True(result.IsActive);
        Assert.Equal(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.ExpiresAtUtc);
        Assert.False(result.AutoRenewEnabled);
    }

    [Fact]
    public async Task VerifyAsync_SubscriptionTokenForDifferentProduct_IsInactive()
    {
        using var env = new BillingEnvScope();
        // İstemci premium productId ile gönderir ama token başka bir aboneliğe
        // (lineItems[].productId farklı) aittir — subscriptionsv2.get URL'inde
        // ürün olmadığı için yalnızca Google'ın cevabı belirleyicidir.
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "autoRenewing": true,
                  "lineItems": [
                    { "productId": "noctra_some_other_sub", "expiryTime": "2099-01-01T00:00:00Z" }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        // Token premium ürüne ait değil → fail-closed inaktif.
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredSubscription_IsInactive()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_EXPIRED",
                  "autoRenewing": false,
                  "lineItems": [
                    { "productId": "noctra_premium_monthly", "expiryTime": "2020-01-01T00:00:00Z" }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task VerifyAsync_TokenNotFound_FailsClosedInactive()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"error":{"message":"not found"}}"""));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.False(result.IsActive);
        Assert.Equal("SUBSCRIPTION_STATE_EXPIRED", result.State);
    }

    [Fact]
    public async Task VerifyAsync_LifetimePurchased_IsActiveWithoutExpiry()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"purchaseState":1,"acknowledgementState":1,"consumptionState":1,"purchaseTimeMillis":"1700000000000"}"""));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_lifetime", "token-xyz", "studio.kynora.noctra");

        Assert.Equal("Lifetime", result.EntitlementType);
        Assert.True(result.IsActive);
        Assert.Null(result.ExpiresAtUtc);
    }

    [Fact]
    public async Task VerifyAsync_LifetimeCanceled_IsInactive()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """{"purchaseState":2,"acknowledgementState":1}"""));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_lifetime", "token-xyz", "studio.kynora.noctra");

        Assert.False(result.IsActive);
        Assert.Equal("PRODUCT_CANCELED", result.State);
    }

    [Fact]
    public async Task VerifyAsync_OAuthToken_IsCachedAcrossCalls()
    {
        using var env = new BillingEnvScope();
        var tokenCalls = 0;
        var handler = new ScriptedHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref tokenCalls);
                return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"access_token":"test-access-token","expires_in":3600,"token_type":"Bearer"}""");
            }

            return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "autoRenewing": true,
                  "lineItems": [{ "productId": "noctra_premium_monthly", "expiryTime": "2099-01-01T00:00:00Z" }]
                }
                """);
        });

        var client = CreateClient(handler);

        await client.VerifyAsync("noctra_premium_monthly", "token-a", "studio.kynora.noctra");
        await client.VerifyAsync("noctra_premium_monthly", "token-b", "studio.kynora.noctra");

        // İlk çağrıda alınan access token ikinci çağrıda yeniden kullanılır.
        Assert.Equal(1, tokenCalls);
    }

    [Fact]
    public async Task OAuthAssertion_IsThreePartBase64UrlRs256Jwt()
    {
        using var env = new BillingEnvScope();
        string? assertion = null;
        var handler = new ScriptedHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync();
                assertion = body.Split('&')
                    .Where(pair => pair.StartsWith("assertion=", StringComparison.Ordinal))
                    .Select(pair => Uri.UnescapeDataString(pair["assertion=".Length..]))
                    .FirstOrDefault();
                return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"access_token":"test-access-token","expires_in":3600}""");
            }

            return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"subscriptionState":"SUBSCRIPTION_STATE_ACTIVE","lineItems":[{"productId":"noctra_premium_monthly","expiryTime":"2099-01-01T00:00:00Z"}]}""");
        });

        var client = CreateClient(handler);
        await client.VerifyAsync("noctra_premium_monthly", "token-a", "studio.kynora.noctra");

        // JWT assertion 3 parçalı base64url (header.claim.sig) olmalı.
        Assert.NotNull(assertion);
        var parts = assertion!.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.DoesNotContain('+', assertion);
        Assert.DoesNotContain('/', assertion);

        // Header RS256 imza algoritmasını belirtmeli.
        var header = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(PadBase64Url(parts[0])));
        Assert.Contains("RS256", header);
    }

    private static string PadBase64Url(string value)
    {
        var padded = value;
        while (padded.Length % 4 != 0)
        {
            padded += "=";
        }

        return padded;
    }

    private static PlayBillingApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = BillingConfig.FromEnvironment();
        return new PlayBillingApiClient(new HttpClient(handler), config);
    }
}
