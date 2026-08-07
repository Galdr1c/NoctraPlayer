using System.Net;
using System.Text;
using System.Text.Json;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

public sealed class BillingVerificationClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _responder(request);
    }

    private static BillingVerifyRequest CreateRequest() => new()
    {
        InstallationId = "install-1",
        PurchaseToken = "token-abc",
        ProductId = "noctra_premium_monthly",
        PackageName = "studio.kynora.noctra"
    };

    [Fact]
    public async Task VerifyAsync_Success_ParsesVerifiedEntitlement()
    {
        var handler = new ScriptedHandler(_ =>
            Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, """
                {
                  "isActive": true,
                  "entitlementType": "Subscription",
                  "expiresAtUtc": "2026-09-06T15:42:10Z",
                  "autoRenewEnabled": true,
                  "state": "SUBSCRIPTION_STATE_ACTIVE",
                  "verifiedAtUtc": "2026-08-06T17:05:00Z"
                }
                """)));

        var client = new HttpBillingVerificationClient(new HttpClient(handler), "https://billing.example.com");

        var result = await client.VerifyAsync(CreateRequest());

        Assert.NotNull(result);
        Assert.True(result!.IsActive);
        Assert.Equal("Subscription", result.EntitlementType);
        Assert.Equal(new DateTime(2026, 9, 6, 15, 42, 10, DateTimeKind.Utc), result.ExpiresAtUtc);
        Assert.True(result.AutoRenewEnabled);
    }

    [Fact]
    public async Task VerifyAsync_ServerError_ReturnsNull()
    {
        var handler = new ScriptedHandler(_ =>
            Task.FromResult(ScriptedHandler.Json(HttpStatusCode.BadGateway, """{"error":"unavailable"}""")));

        var client = new HttpBillingVerificationClient(new HttpClient(handler), "https://billing.example.com");

        Assert.Null(await client.VerifyAsync(CreateRequest()));
    }

    [Fact]
    public async Task VerifyAsync_MalformedJson_ReturnsNull()
    {
        var handler = new ScriptedHandler(_ =>
            Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, "not-json{")));

        var client = new HttpBillingVerificationClient(new HttpClient(handler), "https://billing.example.com");

        Assert.Null(await client.VerifyAsync(CreateRequest()));
    }

    [Fact]
    public async Task VerifyAsync_EmptyBaseUrl_ReturnsNullWithoutNetwork()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));

        var client = new HttpBillingVerificationClient(new HttpClient(handler), "   ");

        Assert.Null(await client.VerifyAsync(CreateRequest()));
    }

    [Fact]
    public async Task VerifyAsync_PostsToVerifyEndpointWithApiKeyHeader()
    {
        // İstek client tarafından dispose edildiği için alanlar handler içinde yakalanır.
        string? method = null;
        string? uri = null;
        string? apiKeyHeader = null;
        string? body = null;
        var handler = new ScriptedHandler(async request =>
        {
            method = request.Method.ToString();
            uri = request.RequestUri!.AbsoluteUri;
            apiKeyHeader = request.Headers.TryGetValues("X-Noctra-Billing-Key", out var values)
                ? values.Single()
                : null;
            body = await request.Content!.ReadAsStringAsync();
            return ScriptedHandler.Json(HttpStatusCode.OK, """{"isActive":false,"entitlementType":"Subscription"}""");
        });

        var client = new HttpBillingVerificationClient(
            new HttpClient(handler),
            "https://billing.example.com/",
            apiKey: "secret-key");

        await client.VerifyAsync(CreateRequest());

        Assert.Equal(HttpMethod.Post.ToString(), method);
        Assert.Equal("https://billing.example.com/billing/google/verify", uri);
        Assert.Equal("secret-key", apiKeyHeader);

        using var json = JsonDocument.Parse(body!);
        Assert.Equal("token-abc", json.RootElement.GetProperty("PurchaseToken").GetString());
        Assert.Equal("studio.kynora.noctra", json.RootElement.GetProperty("PackageName").GetString());
        Assert.Equal("install-1", json.RootElement.GetProperty("InstallationId").GetString());
        Assert.Equal("noctra_premium_monthly", json.RootElement.GetProperty("ProductId").GetString());
    }
}
