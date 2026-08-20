namespace Noctra.Tests;

public sealed class EpgWriteSchedulingContractTests
{
    [Fact]
    public void EpgWriterUsesBoundedBackgroundDatabaseLane()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "EpgService.cs"));
        var registration = File.ReadAllText(ProjectSource(
            "Noctra.Core", "DependencyInjection", "ServiceCollectionExtensions.cs"));

        Assert.Contains("const int EpgWriteBatchSize = 500", source, StringComparison.Ordinal);
        Assert.Contains("DatabaseWorkLane.Write", source, StringComparison.Ordinal);
        Assert.Contains("DatabaseWorkPriority.Background", source, StringComparison.Ordinal);
        Assert.Contains("PersistEpgBatchThroughSchedulerAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IDatabaseWorkScheduler>()", registration, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
