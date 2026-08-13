using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Core.DependencyInjection;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Diagnostics;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class SqliteConnectionPragmaTraceCollection
{
    public const string CollectionName = "SQLite connection PRAGMA trace";
}

[Collection(SqliteConnectionPragmaTraceCollection.CollectionName)]
public sealed class SqliteConnectionPragmaInterceptorTests
{
    [Fact]
    public async Task Factory_ReappliesDesktopPragmasWheneverConnectionOpens()
    {
        var root = CreateTempRoot();
        try
        {
            await using var provider = CreateProvider(root);
            var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

            await using (var first = await factory.CreateDbContextAsync())
            {
                await first.Database.OpenConnectionAsync();
                await AssertPragmasAsync(first.Database.GetDbConnection(), cacheSize: -64000, mmapSize: 268435456);
                await ExecuteAsync(first.Database.GetDbConnection(), "PRAGMA cache_size=-1000;");
                Assert.Equal(-1000, await ScalarAsync(first.Database.GetDbConnection(), "PRAGMA cache_size;"));
            }

            await using (var second = await factory.CreateDbContextAsync())
            {
                await second.Database.OpenConnectionAsync();
                await AssertPragmasAsync(second.Database.GetDbConnection(), cacheSize: -64000, mmapSize: 268435456);
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Factory_UsesExplicitMobileProfile()
    {
        var root = CreateTempRoot();
        try
        {
            await using var provider = CreateProvider(root, SqliteConnectionTuningOptions.Mobile);
            var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await factory.CreateDbContextAsync();

            await context.Database.OpenConnectionAsync();

            await AssertPragmasAsync(context.Database.GetDbConnection(), cacheSize: -32000, mmapSize: 134217728);
            Assert.Same(
                SqliteConnectionTuningOptions.Mobile,
                provider.GetRequiredService<SqliteConnectionTuningOptions>());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Factory_AppliesPragmasOnSynchronousConnectionOpen()
    {
        var root = CreateTempRoot();
        try
        {
            using var provider = CreateProvider(root);
            var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var context = factory.CreateDbContext();

            context.Database.OpenConnection();

            Assert.Equal(-64000, Scalar(context.Database.GetDbConnection(), "PRAGMA cache_size;"));
            Assert.Equal(268435456, Scalar(context.Database.GetDbConnection(), "PRAGMA mmap_size;"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SchemaFixup_KeepsWalButDoesNotDuplicateConnectionScopedPragmas()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Core",
            "Services",
            "DatabaseSchemaFixupService.cs"));

        Assert.Contains("PRAGMA journal_mode=WAL;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = ON;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA synchronous=NORMAL;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA cache_size=-", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA temp_store=MEMORY;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA mmap_size=", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Factory_ReportsValuesReadFromTheActiveConnection()
    {
        var root = CreateTempRoot();
        var originalProbe = PerformanceTrace.Probe;
        var probe = new RecordingPerformanceProbe();
        PerformanceTrace.Probe = probe;
        try
        {
            await using var provider = CreateProvider(root);
            var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await factory.CreateDbContextAsync();

            await context.Database.OpenConnectionAsync();

            Assert.Contains(probe.Events, item =>
                item is ("sqlite.connection.pragmas.applied.count", 1, "desktop"));
            Assert.Contains(probe.Events, item =>
                item is ("sqlite.connection.pragma.foreign_keys", 1, "desktop"));
            Assert.Contains(probe.Events, item =>
                item is ("sqlite.connection.pragma.synchronous", 1, "desktop"));
            Assert.Contains(probe.Events, item =>
                item is ("sqlite.connection.pragma.cache_size", -64000, "desktop"));
            Assert.Contains(probe.Events, item =>
                item is ("sqlite.connection.pragma.temp_store", 2, "desktop"));
            Assert.Contains(probe.Events, item =>
                item is ("sqlite.connection.pragma.mmap_size", 268435456, "desktop"));
        }
        finally
        {
            PerformanceTrace.Probe = originalProbe;
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ApplyFailure_ClosesConnectionAndPreservesOriginalException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var expected = new InvalidOperationException("injected pragma failure");
        var interceptor = new SqliteConnectionPragmaInterceptor(
            SqliteConnectionTuningOptions.Desktop,
            (_, _) => throw expected,
            static (_, _) => Task.CompletedTask);

        var actual = Assert.Throws<InvalidOperationException>(
            () => interceptor.ApplyConnection(connection));

        Assert.Same(expected, actual);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);

        connection.Open();
        new SqliteConnectionPragmaInterceptor(SqliteConnectionTuningOptions.Desktop)
            .ApplyConnection(connection);
        Assert.Equal(-64000, Scalar(connection, "PRAGMA cache_size;"));
    }

    [Fact]
    public async Task ApplyCancellation_ClosesConnectionAndPreservesCancellationToken()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var interceptor = new SqliteConnectionPragmaInterceptor(
            SqliteConnectionTuningOptions.Desktop,
            static (_, _) => { },
            static (_, token) => Task.FromCanceled(token));

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => interceptor.ApplyConnectionAsync(connection, cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);

        await connection.OpenAsync();
        await new SqliteConnectionPragmaInterceptor(SqliteConnectionTuningOptions.Desktop)
            .ApplyConnectionAsync(connection, CancellationToken.None);
        Assert.Equal(-64000, await ScalarAsync(connection, "PRAGMA cache_size;"));
    }

    private static ServiceProvider CreateProvider(
        string root,
        SqliteConnectionTuningOptions? tuning = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppPathService>(new DesktopAppPathService(root, root));
        services.AddSingleton(new HttpClient());
        if (tuning is not null)
        {
            services.AddSingleton(tuning);
        }

        services.AddNoctraCoreServices();
        return services.BuildServiceProvider();
    }

    private static async Task AssertPragmasAsync(DbConnection connection, int cacheSize, long mmapSize)
    {
        Assert.Equal(1, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(1, await ScalarAsync(connection, "PRAGMA synchronous;"));
        Assert.Equal(cacheSize, await ScalarAsync(connection, "PRAGMA cache_size;"));
        Assert.Equal(2, await ScalarAsync(connection, "PRAGMA temp_store;"));
        Assert.Equal(mmapSize, await ScalarAsync(connection, "PRAGMA mmap_size;"));
    }

    private static async Task<long> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static long Scalar(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Noctra.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ProjectSource(params string[] segments)
    {
        var parts = new[]
        {
            AppContext.BaseDirectory,
            "..", "..", "..", ".."
        }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(parts));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class RecordingPerformanceProbe : IPerformanceProbe
    {
        public bool IsEnabled => true;

        public ConcurrentQueue<(string Name, long Value, string? Scope)> Events { get; } = new();

        public void Mark(string name, long value = 0, string? scope = null)
            => Events.Enqueue((name, value, scope));
    }
}
