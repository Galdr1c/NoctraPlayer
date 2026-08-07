using Microsoft.Data.Sqlite;

namespace Noctra.Billing.Api;

/// <summary>
/// Doğrulanmış entitlement kayıtlarını SQLite'ta tutar (sıfır yapılandırma,
/// dosya tabanlı). Anahtar (installationId, productId); RTDN güncellemeleri
/// için purchase token hash üzerinden arama da desteklenir.
/// </summary>
public sealed class EntitlementStore
{
    private readonly string _connectionString;

    public EntitlementStore(string connectionString)
    {
        _connectionString = connectionString;
        Initialize();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        // WAL: eşzamanlı okuyucu/yazıcılar birbirini bloklamaz; busy_timeout,
        // verify + RTDN aynı anda yazdığında "database is locked" yerine
        // kısa süre bekleyip devam edilmesini sağlar.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS entitlements (
                installation_id      TEXT NOT NULL,
                product_id           TEXT NOT NULL,
                purchase_token_hash  TEXT NOT NULL,
                entitlement_type     TEXT NOT NULL,
                is_active            INTEGER NOT NULL,
                expires_at_utc       TEXT,
                auto_renew_enabled   INTEGER NOT NULL,
                state                TEXT NOT NULL,
                last_verified_at_utc TEXT NOT NULL,
                is_trial_period      INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (installation_id, product_id)
            );
            CREATE INDEX IF NOT EXISTS idx_entitlements_token_hash
                ON entitlements (purchase_token_hash);
            """;
        command.ExecuteNonQuery();

        MigrateSchema(connection);
    }

    /// <summary>
    /// Eski sürümden kalma tabloları günceller (CREATE TABLE IF NOT EXISTS yeni
    /// kolonları mevcut tabloya eklemez). is_trial_period öncesi oluşturulmuş
    /// veritabanlarında kolon yoksa varsayılan 0 (trial değil) ile eklenir.
    /// </summary>
    private static void MigrateSchema(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(entitlements);";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (columns.Contains("is_trial_period"))
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE entitlements ADD COLUMN is_trial_period INTEGER NOT NULL DEFAULT 0;";
        alter.ExecuteNonQuery();
    }

    public async Task UpsertAsync(StoredEntitlementRow row, CancellationToken cancellationToken = default)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO entitlements (
                installation_id, product_id, purchase_token_hash, entitlement_type,
                is_active, expires_at_utc, auto_renew_enabled, state, last_verified_at_utc,
                is_trial_period)
            VALUES ($installationId, $productId, $tokenHash, $entitlementType,
                $isActive, $expiresAtUtc, $autoRenewEnabled, $state, $lastVerifiedAtUtc,
                $isTrialPeriod)
            """;
        command.Parameters.AddWithValue("$installationId", row.InstallationId);
        command.Parameters.AddWithValue("$productId", row.ProductId);
        command.Parameters.AddWithValue("$tokenHash", row.PurchaseTokenHash);
        command.Parameters.AddWithValue("$entitlementType", row.EntitlementType);
        command.Parameters.AddWithValue("$isActive", row.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$expiresAtUtc", (object?)row.ExpiresAtUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$autoRenewEnabled", row.AutoRenewEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$state", row.State);
        command.Parameters.AddWithValue("$lastVerifiedAtUtc", row.LastVerifiedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$isTrialPeriod", row.IsTrialPeriod ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<StoredEntitlementRow>> GetAllAsync(
        string installationId,
        CancellationToken cancellationToken = default)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT installation_id, product_id, purchase_token_hash, entitlement_type,
                   is_active, expires_at_utc, auto_renew_enabled, state, last_verified_at_utc,
                   is_trial_period
            FROM entitlements
            WHERE installation_id = $installationId
            ORDER BY product_id
            """;
        command.Parameters.AddWithValue("$installationId", installationId);

        var rows = new List<StoredEntitlementRow>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    /// <summary>RTDN güncellemeleri için token hash'inden kaydı bulur.</summary>
    public async Task<StoredEntitlementRow?> GetByPurchaseTokenHashAsync(
        string purchaseTokenHash,
        CancellationToken cancellationToken = default)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT installation_id, product_id, purchase_token_hash, entitlement_type,
                   is_active, expires_at_utc, auto_renew_enabled, state, last_verified_at_utc,
                   is_trial_period
            FROM entitlements
            WHERE purchase_token_hash = $tokenHash
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$tokenHash", purchaseTokenHash);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadRow(reader)
            : null;
    }

    private static StoredEntitlementRow ReadRow(SqliteDataReader reader)
    {
        DateTime? expiresAtUtc = null;
        if (!reader.IsDBNull(5) &&
            DateTime.TryParse(reader.GetString(5), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            expiresAtUtc = parsed.ToUniversalTime();
        }

        return new StoredEntitlementRow
        {
            InstallationId = reader.GetString(0),
            ProductId = reader.GetString(1),
            PurchaseTokenHash = reader.GetString(2),
            EntitlementType = reader.GetString(3),
            IsActive = reader.GetInt32(4) == 1,
            ExpiresAtUtc = expiresAtUtc,
            AutoRenewEnabled = reader.GetInt32(6) == 1,
            State = reader.GetString(7),
            LastVerifiedAtUtc = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.AdjustToUniversal).ToUniversalTime(),
            IsTrialPeriod = reader.GetInt32(9) == 1
        };
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
