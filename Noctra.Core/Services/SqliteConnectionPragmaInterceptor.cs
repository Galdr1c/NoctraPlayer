using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Noctra.Diagnostics;

namespace Noctra.Services;

public sealed class SqliteConnectionPragmaInterceptor : DbConnectionInterceptor
{
    private readonly SqliteConnectionTuningOptions _options;
    private readonly Action<DbConnection, CancellationToken>? _applyOverride;
    private readonly Func<DbConnection, CancellationToken, Task>? _applyAsyncOverride;
    private readonly ConcurrentDictionary<string, byte> _verifiedDatabases =
        new(StringComparer.OrdinalIgnoreCase);

    public SqliteConnectionPragmaInterceptor(SqliteConnectionTuningOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal SqliteConnectionPragmaInterceptor(
        SqliteConnectionTuningOptions options,
        Action<DbConnection, CancellationToken> applyOverride,
        Func<DbConnection, CancellationToken, Task> applyAsyncOverride)
        : this(options)
    {
        _applyOverride = applyOverride ?? throw new ArgumentNullException(nameof(applyOverride));
        _applyAsyncOverride = applyAsyncOverride ?? throw new ArgumentNullException(nameof(applyAsyncOverride));
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ApplyConnection(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    internal void ApplyConnection(DbConnection connection)
    {
        try
        {
            if (_applyOverride is not null)
            {
                _applyOverride(connection, CancellationToken.None);
                return;
            }

            ApplyCore(connection);
        }
        catch
        {
            TryClose(connection);
            throw;
        }
    }

    internal async Task ApplyConnectionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_applyAsyncOverride is not null)
            {
                await _applyAsyncOverride(connection, cancellationToken).ConfigureAwait(false);
                return;
            }

            await ApplyCoreAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryCloseAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    private void ApplyCore(DbConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = BuildPragmaBatch();
            command.ExecuteNonQuery();
        }

        PerformanceTrace.Mark(
            "sqlite.connection.pragmas.applied.count",
            1,
            _options.ProfileName);

        if (PerformanceTrace.Probe.IsEnabled)
        {
            VerifyAndTraceOnce(connection);
        }
    }

    private async Task ApplyCoreAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = BuildPragmaBatch();
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        PerformanceTrace.Mark(
            "sqlite.connection.pragmas.applied.count",
            1,
            _options.ProfileName);

        if (PerformanceTrace.Probe.IsEnabled)
        {
            await VerifyAndTraceOnceAsync(connection, cancellationToken).ConfigureAwait(false);
        }
    }

    private string BuildPragmaBatch()
        => $"""
            PRAGMA foreign_keys=ON;
            PRAGMA synchronous=NORMAL;
            PRAGMA cache_size={_options.CacheSizePragmaValue};
            PRAGMA temp_store=MEMORY;
            PRAGMA mmap_size={_options.MemoryMappedIoBytes};
            """;

    private void VerifyAndTraceOnce(DbConnection connection)
    {
        var databaseKey = GetDatabaseKey(connection);
        if (!_verifiedDatabases.TryAdd(databaseKey, 0))
        {
            return;
        }

        try
        {
            TraceValue("foreign_keys", ReadInt64(connection, "PRAGMA foreign_keys;"));
            TraceValue("synchronous", ReadInt64(connection, "PRAGMA synchronous;"));
            TraceValue("cache_size", ReadInt64(connection, "PRAGMA cache_size;"));
            TraceValue("temp_store", ReadInt64(connection, "PRAGMA temp_store;"));
            TraceValue("mmap_size", ReadInt64(connection, "PRAGMA mmap_size;"));
            PerformanceTrace.Mark("sqlite.connection.pragmas.verified.count", 1, _options.ProfileName);
        }
        catch
        {
            _verifiedDatabases.TryRemove(databaseKey, out _);
            throw;
        }
    }

    private async Task VerifyAndTraceOnceAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var databaseKey = GetDatabaseKey(connection);
        if (!_verifiedDatabases.TryAdd(databaseKey, 0))
        {
            return;
        }

        try
        {
            TraceValue("foreign_keys", await ReadInt64Async(connection, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false));
            TraceValue("synchronous", await ReadInt64Async(connection, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false));
            TraceValue("cache_size", await ReadInt64Async(connection, "PRAGMA cache_size;", cancellationToken).ConfigureAwait(false));
            TraceValue("temp_store", await ReadInt64Async(connection, "PRAGMA temp_store;", cancellationToken).ConfigureAwait(false));
            TraceValue("mmap_size", await ReadInt64Async(connection, "PRAGMA mmap_size;", cancellationToken).ConfigureAwait(false));
            PerformanceTrace.Mark("sqlite.connection.pragmas.verified.count", 1, _options.ProfileName);
        }
        catch
        {
            _verifiedDatabases.TryRemove(databaseKey, out _);
            throw;
        }
    }

    private void TraceValue(string name, long value)
        => PerformanceTrace.Mark(
            $"sqlite.connection.pragma.{name}",
            value,
            _options.ProfileName);

    private static long ReadInt64(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static async Task<long> ReadInt64Async(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static string GetDatabaseKey(DbConnection connection)
        => string.IsNullOrWhiteSpace(connection.DataSource)
            ? connection.ConnectionString
            : connection.DataSource;

    private static void TryClose(DbConnection connection)
    {
        try
        {
            connection.Close();
        }
        catch
        {
            // Preserve the PRAGMA configuration failure as the primary exception.
        }
    }

    private static async Task TryCloseAsync(DbConnection connection)
    {
        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // Preserve the PRAGMA configuration failure as the primary exception.
        }
    }
}
