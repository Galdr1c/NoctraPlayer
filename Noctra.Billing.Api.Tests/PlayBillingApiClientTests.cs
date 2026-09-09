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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
            ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "latestOrderId": "GPA.1234",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-09-06T15:42:10Z",
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
        Assert.Equal(new DateTime(2099, 9, 6, 15, 42, 10, DateTimeKind.Utc), result.ExpiresAtUtc);
        // autoRenewing root'ta DEĞİL, lineItems[].autoRenewingPlan.autoRenewEnabled'da.
        Assert.True(result.AutoRenewEnabled);
        Assert.Equal("SUBSCRIPTION_STATE_ACTIVE", result.State);
        Assert.False(result.IsTrialPeriod);
    }

    [Fact]
    public async Task VerifyAsync_ActiveSubscription_UsesSubscriptionsV2TokensPath()
    {
        using var env = new BillingEnvScope();
        Uri? requestUri = null;
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
        {
            requestUri = request.RequestUri;
            return ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    {
                      "productId": "noctra_premium_monthly",
                      "expiryTime": "2099-01-01T00:00:00Z"
                    }
                  ]
                }
                """);
        });

        var client = CreateClient(handler);

        await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.NotNull(requestUri);
        Assert.Equal(
            "/androidpublisher/v3/applications/studio.kynora.noctra/purchases/subscriptionsv2/tokens/token-abc",
            requestUri!.AbsolutePath);
    }

    [Fact]
    public async Task VerifyAsync_CanceledSubscription_KeepsAccessUntilPaidThroughExpiry()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var playRequests = 0;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref playRequests);
            return Task.FromResult(ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
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
                """));
        });

        var client = CreateClient(handler);

        var result = await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        Assert.True(result.IsActive);
        Assert.True(result.IsTrialPeriod);
        // Trial tespiti tek istekle yapılır — Monetization API'ye EK istek yok.
        Assert.Equal(1, playRequests);
    }

    [Fact]
    public async Task VerifyAsync_ActiveRegularSubscription_IsTrialPeriodFalse()
    {
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
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
    // ADC token provider entegrasyonu
    // ==========================================

    [Fact]
    public async Task VerifyAsync_UsesAccessTokenFromProvider()
    {
        using var env = new BillingEnvScope();
        string? authorization = null;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
                  "lineItems": [
                    { "productId": "noctra_premium_monthly", "expiryTime": "2099-01-01T00:00:00Z" }
                  ]
                }
                """));
        });

        var client = CreateClient(handler);

        await client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra");

        // ADC'den alınan token Authorization başlığına taşınır (manuel JWT yok).
        Assert.Equal("Bearer test-access-token", authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]       // 401: credential/permission hatası
    [InlineData(HttpStatusCode.Forbidden)]          // 403: Play Console permission hatası
    [InlineData(HttpStatusCode.TooManyRequests)]    // 429: quota davranışı
    [InlineData(HttpStatusCode.InternalServerError)] // 500: Play geçici/sürekli hatası
    [InlineData(HttpStatusCode.BadGateway)]         // 503 benzeri gateway hatası
    public async Task VerifyAsync_PlayApiError_ThrowsInvalidOperation(HttpStatusCode status)
    {
        // Play API hatası (401/403/429/500/503) 404 DEĞİLDİR — token geçersiz
        // demek değildir. GetJsonAsync hata fırlatır; endpoint 502'ye çevirir ve
        // client son bilinen doğrulanmış önbelleği kullanmaya devam eder
        // (hak asla bu hatalarda erken düşmez). 404 ise fail-closed inaktif döner.
        using var env = new BillingEnvScope();
        var handler = ScriptedHttpMessageHandler.PlayApi(request =>
            ScriptedHttpMessageHandler.Json(status, """{"error":{"message":"upstream"}}"""));

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.VerifyAsync("noctra_premium_monthly", "token-abc", "studio.kynora.noctra"));
    }

    [Fact]
    public async Task VerifyAsync_UnknownProduct_ThrowsBeforeCallingPlay()
    {
        using var env = new BillingEnvScope();
        var calls = 0;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(ScriptedHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        });

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.VerifyAsync("noctra_unknown", "token-abc", "studio.kynora.noctra"));

        Assert.Equal(0, calls);
    }

    private static PlayBillingApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = BillingConfig.FromEnvironment();
        return new PlayBillingApiClient(
            new HttpClient(handler),
            config,
            new FixedAccessTokenProvider());
    }
}
