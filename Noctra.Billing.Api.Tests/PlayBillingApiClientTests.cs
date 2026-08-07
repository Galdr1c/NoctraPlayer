using System.Net;

namespace Noctra.Billing.Api.Tests;

[Collection(BillingEnvCollection.Name)]
public sealed class PlayBillingApiClientTests
{
    // ==========================================
    // Subscription (subscriptionsv2.get — GERÇEK Google şeması)
    // ==========================================

    [Fact]
    public async Task VerifyAsync_ActiveSubscription_UsesPlayExpiryAndLineItemAutoRenew()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "latestOrderId": "GPA.1234",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2026-09-06T15:42:10Z",
                      "autoRenewingPlan": { "autoRenewEnabled": true, "planId": "baseplan.monthly" },
                      "offerDetails": { "basePlanId": "baseplan.monthly", "offerId": "base_plan_offer" },
                      "offerPhase": { "basePrice": { "priceAmountMicros": "59990000", "priceCurrencyCode": "TRY" } }
                    }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.Equal("Subscription", result.EntitlementType);
        Assert.True(result.IsActive);
        Assert.Equal(new DateTime(2026, 9, 6, 15, 42, 10, DateTimeKind.Utc), result.ExpiresAtUtc);
        // autoRenewing root'ta DEĞİL, lineItems[].autoRenewingPlan.autoRenewEnabled'da.
        Assert.True(result.AutoRenewEnabled);
        Assert.Equal("SUBSCRIPTION_STATE_ACTIVE", result.State);
        Assert.False(result.IsTrialPeriod);
    }

    [Fact]
    public async Task VerifyAsync_CanceledSubscription_KeepsAccessUntilPaidThroughExpiry()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_CANCELED",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": false },
                      "offerPhase": { "basePrice": { "priceAmountMicros": "59990000" } }
                    }
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
        // (lineItems[].productId farklı) aittir. Farklı ürünün line item'ı
        // istenen hakkın expiry'sine KATKIDA BULUNAMaz — hatta expiry 2099
        // olsa bile.
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    {
                      "productId": "noctra_some_other_sub",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": true },
                      "offerPhase": { "basePrice": { "priceAmountMicros": "10000" } }
                    }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        // Token premium ürüne ait değil → fail-closed inaktif.
        Assert.False(result.IsActive);
        Assert.Null(result.ExpiresAtUtc);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredSubscription_IsInactive()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_EXPIRED",
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
    public async Task VerifyAsync_PausedSubscription_IsInactive()
    {
        // PAUSED: kullanıcı duraklattı — faturalama da erişim de durur.
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_PAUSED",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": false }
                    }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task VerifyAsync_OnHoldSubscription_IsInactive()
    {
        // ON_HOLD (account hold): ödeme sorunu — Google kullanıcının erişimini
        // kaldırır. (Eski "SUBSCRIPTION_STATE_ACCOUNT_HOLD" değeri yoktur;
        // güncel isim SUBSCRIPTION_STATE_ON_HOLD.)
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ON_HOLD",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": false }
                    }
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

    // ==========================================
    // Lifetime (purchases.products.get — purchaseState: 0=Purchased, 1=Canceled, 2=Pending)
    // ==========================================

    [Fact]
    public async Task VerifyAsync_LifetimePurchased_IsActiveWithoutExpiry()
    {
        using var env = new BillingEnvScope();
        // GERÇEK şema: 0 = Purchased (BillingClient'ın 1'i değil!).
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"purchaseState":0,"acknowledgementState":1,"consumptionState":1,"purchaseTimeMillis":"1700000000000"}"""));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_lifetime", "token-xyz", "studio.kynora.noctra");

        Assert.Equal("Lifetime", result.EntitlementType);
        Assert.True(result.IsActive);
        Assert.Null(result.ExpiresAtUtc);
        Assert.Equal("PRODUCT_PURCHASED", result.State);
    }

    [Fact]
    public async Task VerifyAsync_LifetimeCanceled_IsInactive()
    {
        using var env = new BillingEnvScope();
        // GERÇEK şema: 1 = Canceled (iptal edilmiş purchase AKTİF sayılamaz).
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """{"purchaseState":1,"acknowledgementState":1}"""));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_lifetime", "token-xyz", "studio.kynora.noctra");

        Assert.False(result.IsActive);
        Assert.Equal("PRODUCT_CANCELED", result.State);
    }

    [Fact]
    public async Task VerifyAsync_LifetimePending_IsInactive()
    {
        using var env = new BillingEnvScope();
        // GERÇEK şema: 2 = Pending — ödeme tamamlanmadı, hak verilmez.
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """{"purchaseState":2,"acknowledgementState":0}"""));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_lifetime", "token-xyz", "studio.kynora.noctra");

        Assert.False(result.IsActive);
        Assert.Equal("PRODUCT_PENDING", result.State);
    }

    // ==========================================
    // Trial tespiti — offerPhase.freeTrial (tek istek, Monetization çağrısı yok)
    // ==========================================

    [Fact]
    public async Task VerifyAsync_ActiveTrialSubscription_IsTrialPeriodTrue()
    {
        using var env = new BillingEnvScope();
        var nonOAuthRequests = 0;
        var handler = new ScriptedHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"access_token":"test-access-token","expires_in":3600,"token_type":"Bearer"}""");
            }

            Interlocked.Increment(ref nonOAuthRequests);
            return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": true },
                      "offerDetails": { "basePlanId": "baseplan.monthly", "offerId": "trial_monthly_offer" },
                      "offerPhase": { "freeTrial": { "offerId": "trial_monthly_offer", "priceAmountMicros": "0" } }
                    }
                  ]
                }
                """);
        });

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.True(result.IsActive);
        Assert.True(result.IsTrialPeriod);
        // Trial tespiti tek istekle yapılır — Monetization API'ye EK istek yok.
        Assert.Equal(1, nonOAuthRequests);
    }

    [Fact]
    public async Task VerifyAsync_ActiveRegularSubscription_IsTrialPeriodFalse()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": true },
                      "offerPhase": { "basePrice": { "priceAmountMicros": "59990000" } }
                    }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.True(result.IsActive);
        Assert.False(result.IsTrialPeriod);
    }

    [Fact]
    public async Task VerifyAsync_IntroPricedOffer_IsNotTrial()
    {
        // Ücretli tanışma (introductory) fazı freeTrial DEĞİLDİR — kullanıcı
        // indirimli ücret ödüyor, trial'da değil.
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "offerPhase": { "introductoryPrice": { "priceAmountMicros": "29990000" } }
                    }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.True(result.IsActive);
        Assert.False(result.IsTrialPeriod);
    }

    [Fact]
    public async Task VerifyAsync_NoOfferPhase_IsNotTrialButStillActive()
    {
        // offerPhase yoksa trial tespiti yapılamaz ama hak asla bu yüzden
        // engellenmez — trial değil kabul edilir, abonelik yine aktif.
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.TokenPlus(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    { "productId": "noctra_premium_monthly", "expiryTime": "2099-01-01T00:00:00Z" }
                  ]
                }
                """));

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.True(result.IsActive);
        Assert.False(result.IsTrialPeriod);
    }

    // ==========================================
    // OAuth
    // ==========================================

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
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z",
                      "autoRenewingPlan": { "autoRenewEnabled": true },
                      "offerPhase": { "basePrice": { "priceAmountMicros": "59990000" } }
                    }
                  ]
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
