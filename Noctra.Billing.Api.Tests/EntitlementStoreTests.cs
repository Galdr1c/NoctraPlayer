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
            IsTrialPeriod = true,
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
        Assert.True(stored.IsTrialPeriod);

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

    [Fact]
    public async Task Initialize_MigratesLegacyTableWithoutTrialColumn()
    {
        // Eski sürümde oluşturulmuş tabloyu (is_trial_period kolonu YOK) simüle
        // eder — EntitlementStore açılışı kolonu eklemeli ve mevcut veriyi
        // korumalıdır (CREATE TABLE IF NOT EXISTS yeni kolonu eklemez).
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE entitlements (
                    installation_id      TEXT NOT NULL,
                    product_id           TEXT NOT NULL,
                    purchase_token_hash  TEXT NOT NULL,
                    entitlement_type     TEXT NOT NULL,
                    is_active            INTEGER NOT NULL,
                    expires_at_utc       TEXT,
                    auto_renew_enabled   INTEGER NOT NULL,
                    state                TEXT NOT NULL,
                    last_verified_at_utc TEXT NOT NULL,
                    PRIMARY KEY (installation_id, product_id)
                );
                INSERT INTO entitlements (installation_id, product_id, purchase_token_hash,
                    entitlement_type, is_active, expires_at_utc, auto_renew_enabled,
                    state, last_verified_at_utc)
                VALUES ('install-legacy', 'noctra_premium_monthly', 'hash-legacy',
                    'Subscription', 1, '2099-01-01T00:00:00Z', 1,
                    'SUBSCRIPTION_STATE_ACTIVE', '2026-08-06T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        var store = new EntitlementStore(_connectionString);

        // Eski kayıt korundu ve trial varsayılan olarak false okunuyor.
        var legacy = await store.GetByPurchaseTokenHashAsync("hash-legacy");
        Assert.NotNull(legacy);
        Assert.Equal("install-legacy", legacy!.InstallationId);
        Assert.False(legacy.IsTrialPeriod);

        // Migration sonrası yeni kayıtlar trial bayrağıyla çalışır.
        await store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = "install-new",
            ProductId = "noctra_premium_monthly",
            PurchaseTokenHash = "hash-new",
            EntitlementType = "Subscription",
            IsActive = true,
            ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AutoRenewEnabled = true,
            State = "SUBSCRIPTION_STATE_ACTIVE",
            IsTrialPeriod = true,
            LastVerifiedAtUtc = DateTime.UtcNow
        });

        var stored = await store.GetAllAsync("install-new");
        var row = Assert.Single(stored);
        Assert.True(row.IsTrialPeriod);
    }
}
