using Microsoft.Data.Sqlite;
using Moq;

namespace Noctra.Billing.Api.Tests;

[Collection(BillingEnvCollection.Name)]
public sealed class EntitlementServiceTests : IDisposable
{
    private readonly BillingEnvScope _env;
    private readonly string _connectionString;
    private readonly BillingConfig _config;
    private readonly EntitlementStore _store;
    private readonly Mock<IPlayBillingApi> _api;

    public EntitlementServiceTests()
    {
        _env = new BillingEnvScope();
        _connectionString = $"Data Source=file:billing_service_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _config = BillingConfig.FromEnvironment();
        _store = new EntitlementStore(_connectionString);
        _api = new Mock<IPlayBillingApi>();
    }

    public void Dispose()
    {
        _env.Dispose();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task VerifyAndStore_WrongPackage_RejectsWithoutCallingGoogle()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAndStoreAsync(new BillingVerifyRequest
            {
                InstallationId = "install-1",
                PurchaseToken = "token-1",
                ProductId = "noctra_premium_monthly",
                PackageName = "com.evil.other"
            }));

        Assert.Contains("packageName", exception.Message);
        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAndStore_UnknownProduct_Rejects()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<BillingRequestException>(() =>
            service.VerifyAndStoreAsync(new BillingVerifyRequest
            {
                InstallationId = "install-1",
                PurchaseToken = "token-1",
                ProductId = "noctra_unknown",
                PackageName = "studio.kynora.noctra"
            }));

        Assert.Contains("productId", exception.Message);
        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAndStore_ActiveSubscription_PersistsAndReturnsPlayExpiry()
    {
        var expiry = new DateTime(2026, 9, 6, 15, 42, 10, DateTimeKind.Utc);
        _api.Setup(a => a.VerifyAsync("noctra_premium_monthly", "token-1", "studio.kynora.noctra", It.IsAny<CancellationToken>()))
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

        var result = await service.VerifyAndStoreAsync(new BillingVerifyRequest
        {
            InstallationId = "install-1",
            PurchaseToken = "token-1",
            ProductId = "noctra_premium_monthly",
            PackageName = "studio.kynora.noctra"
        });

        Assert.True(result.IsActive);
        Assert.Equal(expiry, result.ExpiresAtUtc);
        Assert.Equal("Subscription", result.EntitlementType);
        Assert.True(result.IsTrialPeriod);

        // Kalıcılık: kayıt token hash'i ile bulunabilir (ham token saklanmaz).
        var rows = await _store.GetAllByPurchaseTokenHashAsync(EntitlementService.HashToken("token-1"));
        var stored = Assert.Single(rows);
        Assert.Equal("install-1", stored.InstallationId);
        Assert.Equal(expiry, stored.ExpiresAtUtc);
        Assert.True(stored.IsTrialPeriod);
        Assert.DoesNotContain("token-1", stored.PurchaseTokenHash);
    }

    [Fact]
    public async Task ReverifyByToken_UnknownToken_DoesNotCallGoogle()
    {
        var service = CreateService();

        var handled = await service.ReverifyByTokenAsync("never-seen-token", "noctra_premium_monthly");

        Assert.False(handled);
        _api.Verify(a => a.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReverifyByToken_KnownToken_UpdatesRowWithNewState()
    {
        await _store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-1",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = EntitlementService.HashToken("token-1"),
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            LastVerifiedAtUtc = DateTime.UtcNow
        });

        _api.Setup(a => a.VerifyAsync("noctra_premium_monthly", "token-1", "studio.kynora.noctra", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayPurchaseVerification
            {
                EntitlementType = "Subscription",
                IsActive = false,
                ExpiresAtUtc = null,
                AutoRenewEnabled = false,
                State = "SUBSCRIPTION_STATE_EXPIRED"
            });

        var service = CreateService();

        var handled = await service.ReverifyByTokenAsync("token-1", "noctra_premium_monthly");

        Assert.True(handled);
        var rows = await _store.GetAllByPurchaseTokenHashAsync(EntitlementService.HashToken("token-1"));
        var stored = Assert.Single(rows);
        Assert.False(stored.IsActive);
        Assert.Equal("SUBSCRIPTION_STATE_EXPIRED", stored.State);
    }

    [Fact]
    public async Task ReverifyByToken_MultiInstallation_UpdatesAllRowsSharingToken()
    {
        // Aynı satın alma telefon + tablette restore edilmiş (aynı token hash,
        // iki kurulum). RTDN reverify Google'a TEK sorgu atıp token'ı paylaşan
        // BÜTÜN kurulum satırlarını güncellemelidir — yalnızca birini güncellemek
        // diğer cihazı eski state'te bırakırdı.
        var tokenHash = EntitlementService.HashToken("token-1");
        await _store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-phone",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = tokenHash,
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            LastVerifiedAtUtc = DateTime.UtcNow
        });
        await _store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-tablet",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = tokenHash,
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            LastVerifiedAtUtc = DateTime.UtcNow
        });

        // Play artık aboneliği iptal etti (expiry geçti) → her iki kurulum da güncellenmeli.
        _api.Setup(a => a.VerifyAsync("noctra_premium_monthly", "token-1", "studio.kynora.noctra", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayPurchaseVerification
            {
                EntitlementType = "Subscription",
                IsActive = false,
                ExpiresAtUtc = null,
                AutoRenewEnabled = false,
                State = "SUBSCRIPTION_STATE_EXPIRED"
            });

        var service = CreateService();

        var handled = await service.ReverifyByTokenAsync("token-1", "noctra_premium_monthly");

        Assert.True(handled);
        _api.Verify(a => a.VerifyAsync("noctra_premium_monthly", "token-1", "studio.kynora.noctra", It.IsAny<CancellationToken>()),
            Times.Once);

        var rows = await _store.GetAllByPurchaseTokenHashAsync(tokenHash);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.False(row.IsActive);
            Assert.Equal("SUBSCRIPTION_STATE_EXPIRED", row.State);
        });
    }

    private EntitlementService CreateService() =>
        new(_config, _api.Object, _store);
}
