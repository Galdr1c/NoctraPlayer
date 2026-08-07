using Microsoft.Data.Sqlite;

namespace Noctra.Billing.Api.Tests;

public sealed class EntitlementStoreTests : IDisposable
{
    private readonly string _connectionString =
        $"Data Source=file:billing_tests_{Guid.NewGuid():N}?mode=memory&cache=shared";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task UpsertThenGetAll_ReturnsStoredRows()
    {
        var store = new EntitlementStore(_connectionString);
        var row = new StoredEntitlementRow
        {
            InstallationId = "install-1",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = "hash-1",
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2026, 9, 6, 15, 42, 10, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            LastVerifiedAtUtc = DateTime.UtcNow
        };

        await store.UpsertAsync(row);

        var rows = await store.GetAllAsync("install-1");
        var stored = Assert.Single(rows);
        Assert.Equal("install-1", stored.InstallationId);
        Assert.Equal("Subscription", stored.EntitlementType);
        Assert.True(stored.IsActive);
        Assert.Equal(row.ExpiresAtUtc, stored.ExpiresAtUtc);
        Assert.True(stored.AutoRenewEnabled);

        // Başka kurulum kaydı görmez.
        Assert.Empty(await store.GetAllAsync("install-other"));
    }

    [Fact]
    public async Task UpsertSameKey_ReplacesExistingRow()
    {
        var store = new EntitlementStore(_connectionString);
        await store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-1",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = "hash-old",
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            LastVerifiedAtUtc = DateTime.UtcNow
        });

        await store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-1",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = "hash-new",
            EntitlementType = "Subscription",
            IsActive = false,
            ExpiresAtUtc = null,
            AutoRenewEnabled = false,
            State = "SUBSCRIPTION_STATE_EXPIRED",
            LastVerifiedAtUtc = DateTime.UtcNow
        });

        var rows = await store.GetAllAsync("install-1");
        var stored = Assert.Single(rows);
        Assert.False(stored.IsActive);
        Assert.Equal("hash-new", stored.PurchaseTokenHash);
    }

    [Fact]
    public async Task GetByPurchaseTokenHash_FindsRowAcrossInstallations()
    {
        var store = new EntitlementStore(_connectionString);
        await store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-9",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = "token-hash-abc",
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            LastVerifiedAtUtc = DateTime.UtcNow
        });

        var found = await store.GetByPurchaseTokenHashAsync("token-hash-abc");
        Assert.NotNull(found);
        Assert.Equal("install-9", found!.InstallationId);

        Assert.Null(await store.GetByPurchaseTokenHashAsync("unknown-hash"));
    }
}
