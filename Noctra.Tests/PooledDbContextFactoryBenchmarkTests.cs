using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Data;

namespace Noctra.Tests;

public sealed class PooledDbContextFactoryBenchmarkTests
{
    [Fact]
    public async Task CompareNormalAndPooledFactoryContextOverhead()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"noctra-pool-benchmark-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using (var setup = new AppDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            using var normalProvider = BuildProvider(databasePath, pooled: false);
            using var pooledProvider = BuildProvider(databasePath, pooled: true);
            var normal = normalProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var pooled = pooledProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

            // Warm model/query compilation before measuring factory overhead.
            _ = await ReadChannelCountAsync(normal);
            _ = await ReadChannelCountAsync(pooled);
            await AssertPooledContextStateIsResetAsync(pooled);

            var normalResult = await MeasureAsync(normal, 200);
            var pooledResult = await MeasureAsync(pooled, 200);

            Assert.Equal(0, normalResult.ChannelCount);
            Assert.Equal(0, pooledResult.ChannelCount);
            Assert.True(normalResult.ElapsedMilliseconds >= 0);
            Assert.True(pooledResult.ElapsedMilliseconds >= 0);

            Console.WriteLine(
                $"P3-02 SQLite factory benchmark: normal={normalResult.ElapsedMilliseconds}ms/{normalResult.AllocatedBytes}B, " +
                $"pooled={pooledResult.ElapsedMilliseconds}ms/{pooledResult.AllocatedBytes}B");
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch
            {
            }
        }
    }

    private static ServiceProvider BuildProvider(string databasePath, bool pooled)
    {
        var services = new ServiceCollection();
        if (pooled)
        {
            services.AddPooledDbContextFactory<AppDbContext>(
                options => options.UseSqlite($"Data Source={databasePath}"),
                poolSize: 64);
        }
        else
        {
            services.AddDbContextFactory<AppDbContext>(
                options => options.UseSqlite($"Data Source={databasePath}"));
        }

        return services.BuildServiceProvider();
    }

    private static async Task<int> ReadChannelCountAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Channels.AsNoTracking().CountAsync();
    }

    private static async Task AssertPooledContextStateIsResetAsync(
        IDbContextFactory<AppDbContext> factory)
    {
        await using (var first = await factory.CreateDbContextAsync())
        {
            first.Channels.Add(new Noctra.Models.Channel
            {
                Name = "Uncommitted benchmark entity",
                StreamUrl = "https://benchmark.invalid/uncommitted",
                PlaylistId = 1
            });
            Assert.Single(first.ChangeTracker.Entries());
        }

        await using (var second = await factory.CreateDbContextAsync())
        {
            Assert.Empty(second.ChangeTracker.Entries());
            await second.Database.OpenConnectionAsync();
            await using var transaction = await second.Database.BeginTransactionAsync();
            await transaction.RollbackAsync();
        }
    }

    private static async Task<BenchmarkResult> MeasureAsync(
        IDbContextFactory<AppDbContext> factory,
        int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        var channelCount = 0;
        for (var i = 0; i < iterations; i++)
        {
            channelCount = await ReadChannelCountAsync(factory);
        }

        stopwatch.Stop();
        return new BenchmarkResult(
            stopwatch.ElapsedMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
            channelCount);
    }

    private readonly record struct BenchmarkResult(
        long ElapsedMilliseconds,
        long AllocatedBytes,
        int ChannelCount);
}
