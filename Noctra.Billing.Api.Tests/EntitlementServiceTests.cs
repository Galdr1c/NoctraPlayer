using Moq;

namespace Noctra.Billing.Api.Tests;

[Collection(BillingEnvCollection.Name)]
public sealed class EntitlementServiceTests : IDisposable
{
    private readonly BillingEnvScope _env;
    private readonly BillingConfig _config;
    private readonly Mock<IPlayBillingApi> _api;

    public EntitlementServiceTests()
    {
        _env = new BillingEnvScope();
        _config = BillingConfig.FromEnvironment();
        _api = new Mock<IPlayBillingApi>();
    }

    public void Dispose() => _env.Dispose();

    [Fact]
    public async Task Verify_WrongPackage_RejectsWithoutCallingGoogle()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAsync(new BillingVerifyRequest
            {
                PurchaseToken = "token-1234567890",
                ProductId = "noctra_premium_monthly",
                PackageName = "com.evil.other"
            }));

        Assert.Contains("packageName", exception.Message);
        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Verify_UnknownProduct_Rejects()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAsync(new BillingVerifyRequest
            {
                PurchaseToken = "token-1234567890",
                ProductId = "noctra_unknown",
                PackageName = "studio.kynora.noctra"
            }));

        Assert.Contains("productId", exception.Message);
        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Verify_MissingToken_Rejects()
    {
        var service = CreateService();

        var noToken = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAsync(new BillingVerifyRequest
            {
                PurchaseToken = string.Empty,
                ProductId = "noctra_premium_monthly",
                PackageName = "studio.kynora.noctra"
            }));
        Assert.Contains("purchaseToken", noToken.Message);

        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Verify_TooShortOrTooLongToken_Rejects()
    {
        // Abuse/cost koruması: Play token'ları ~100-400 karakterdir; 10'un
        // altı veya 4096'nın üzeri reddedilir (bot taraması, dev payload).
        var service = CreateService();

        var tooShort = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAsync(new BillingVerifyRequest
            {
                PurchaseToken = "short",
                ProductId = "noctra_premium_monthly",
                PackageName = "studio.kynora.noctra"
            }));
        Assert.Contains("purchaseToken", tooShort.Message);

        var tooLong = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAsync(new BillingVerifyRequest
            {
                PurchaseToken = new string('x', 5000),
                ProductId = "noctra_premium_monthly",
                PackageName = "studio.kynora.noctra"
            }));
        Assert.Contains("purchaseToken", tooLong.Message);

        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Verify_ActiveSubscription_ReturnsPlayExpiry()
    {
        var expiry = new DateTime(2026, 9, 6, 15, 42, 10, DateTimeKind.Utc);
        _api.Setup(a => a.VerifyAsync("noctra_premium_monthly", "token-1234567890", "studio.kynora.noctra", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayPurchaseVerification
            {
                EntitlementType = "Subscription",
                IsActive = true,
                ExpiresAtUtc = expiry,
                AutoRenewEnabled = true,
                State = "SUBSCRIPTION_STATE_ACTIVE",
                // Gerçek trial tespiti backend'den gelir ve uçtan uca taşınır.
                IsTrialPeriod = true
            });

        var service = CreateService();

        var result = await service.VerifyAsync(new BillingVerifyRequest
        {
            PurchaseToken = "token-1234567890",
            ProductId = "noctra_premium_monthly",
            PackageName = "studio.kynora.noctra"
        });

        Assert.True(result.IsActive);
        Assert.Equal(expiry, result.ExpiresAtUtc);
        Assert.Equal("Subscription", result.EntitlementType);
        Assert.True(result.IsTrialPeriod);
        Assert.True(result.VerifiedAtUtc != default);
    }

    [Fact]
    public async Task Verify_LifetimeInactive_ReturnsStateWithoutExpiry()
    {
        _api.Setup(a => a.VerifyAsync("noctra_premium_lifetime", "token-1234567890", "studio.kynora.noctra", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayPurchaseVerification
            {
                EntitlementType = "Lifetime",
                IsActive = false,
                ExpiresAtUtc = null,
                AutoRenewEnabled = false,
                State = "PRODUCT_CANCELED"
            });

        var service = CreateService();

        var result = await service.VerifyAsync(new BillingVerifyRequest
        {
            PurchaseToken = "token-1234567890",
            ProductId = "noctra_premium_lifetime",
            PackageName = "studio.kynora.noctra"
        });

        Assert.False(result.IsActive);
        Assert.Null(result.ExpiresAtUtc);
        Assert.Equal("PRODUCT_CANCELED", result.State);
    }

    private EntitlementService CreateService() =>
        new(_config, _api.Object);
}
