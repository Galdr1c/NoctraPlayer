using Microsoft.EntityFrameworkCore;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Noctra.Tests;

public sealed class ImportJobServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly SimpleDbContextFactory _contextFactory;

    public ImportJobServiceTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"NoctraImportJobServiceTests-{Guid.NewGuid():N}.db");

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        using (var context = new AppDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _contextFactory = new SimpleDbContextFactory(_options);
    }

    public void Dispose()
    {
        try { File.Delete(_databasePath); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public async Task StartAsync_CreatesRunningJobWithSourceScope()
    {
        var service = new ImportJobService(_contextFactory);

        var job = await service.StartAsync(
            ImportJobKind.PlaylistRefresh,
            profileId: 12,
            playlistId: 34,
            sourceName: "Large Provider");

        using var context = new AppDbContext(_options);
        var persisted = await context.ImportJobs.SingleAsync();

        Assert.Equal(job.Id, persisted.Id);
        Assert.Equal(ImportJobKind.PlaylistRefresh, persisted.Kind);
        Assert.Equal(ImportJobStatus.Running, persisted.Status);
        Assert.Equal(12, persisted.ProfileId);
        Assert.Equal(34, persisted.PlaylistId);
        Assert.Equal("Large Provider", persisted.SourceName);
        Assert.Equal("Starting", persisted.Stage);
    }

    [Fact]
    public async Task ReportProgressAsync_UpdatesCountsAndStage()
    {
        var service = new ImportJobService(_contextFactory);
        var job = await service.StartAsync(ImportJobKind.M3U, profileId: 7, playlistId: null, sourceName: "M3U");

        await service.ReportProgressAsync(
            job.Id,
            stage: "Writing batch",
            liveCount: 4200,
            vodCount: 180,
            seriesCount: 25,
            failedCategoryCount: 2);

        using var context = new AppDbContext(_options);
        var persisted = await context.ImportJobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(ImportJobStatus.Running, persisted.Status);
        Assert.Equal("Writing batch", persisted.Stage);
        Assert.Equal(4200, persisted.LiveCount);
        Assert.Equal(180, persisted.VodCount);
        Assert.Equal(25, persisted.SeriesCount);
        Assert.Equal(2, persisted.FailedCategoryCount);
        Assert.True(persisted.UpdatedAt >= persisted.CreatedAt);
    }

    [Fact]
    public async Task CompleteAsync_MarksJobCompleted()
    {
        var service = new ImportJobService(_contextFactory);
        var job = await service.StartAsync(ImportJobKind.Xtream, profileId: 9, playlistId: 3, sourceName: "Xtream");

        await service.CompleteAsync(job.Id, stage: "Ready");

        using var context = new AppDbContext(_options);
        var persisted = await context.ImportJobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(ImportJobStatus.Completed, persisted.Status);
        Assert.Equal("Ready", persisted.Stage);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Null(persisted.ErrorMessage);
    }

    [Fact]
    public async Task AttachPlaylistAsync_SetsPlaylistIdWithoutCompletingJob()
    {
        var service = new ImportJobService(_contextFactory);
        var job = await service.StartAsync(ImportJobKind.M3U, profileId: 9, playlistId: null, sourceName: "M3U");

        await service.AttachPlaylistAsync(job.Id, playlistId: 42);

        using var context = new AppDbContext(_options);
        var persisted = await context.ImportJobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(42, persisted.PlaylistId);
        Assert.Equal(ImportJobStatus.Running, persisted.Status);
        Assert.True(persisted.UpdatedAt >= persisted.CreatedAt);
    }

    [Fact]
    public async Task FailAsync_MarksJobFailedWithError()
    {
        var service = new ImportJobService(_contextFactory);
        var job = await service.StartAsync(ImportJobKind.Stalker, profileId: 9, playlistId: 3, sourceName: "Stalker");

        await service.FailAsync(job.Id, "Provider timed out");

        using var context = new AppDbContext(_options);
        var persisted = await context.ImportJobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(ImportJobStatus.Failed, persisted.Status);
        Assert.Equal("Failed", persisted.Stage);
        Assert.Equal("Provider timed out", persisted.ErrorMessage);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task CancelAsync_MarksJobCanceledAndRemovesItFromActiveJobs()
    {
        var service = new ImportJobService(_contextFactory);
        var job = await service.StartAsync(ImportJobKind.PlaylistRefresh, profileId: 9, playlistId: 3, sourceName: "Refresh");

        await service.CancelAsync(job.Id, stage: "Canceled");

        using var context = new AppDbContext(_options);
        var persisted = await context.ImportJobs.SingleAsync(j => j.Id == job.Id);
        var active = await service.GetActiveForProfileAsync(profileId: 9);

        Assert.Equal(ImportJobStatus.Canceled, persisted.Status);
        Assert.Equal("Canceled", persisted.Stage);
        Assert.Null(persisted.ErrorMessage);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Null(active);
    }

    [Fact]
    public async Task GetActiveForProfileAsync_ReturnsNewestRunningOrQueuedJob()
    {
        var service = new ImportJobService(_contextFactory);
        var older = await service.StartAsync(ImportJobKind.M3U, profileId: 9, playlistId: null, sourceName: "Older");
        await Task.Delay(5);
        var newer = await service.StartAsync(ImportJobKind.PlaylistRefresh, profileId: 9, playlistId: 22, sourceName: "Newer");
        await service.CompleteAsync(older.Id, "Completed");

        var active = await service.GetActiveForProfileAsync(profileId: 9);

        Assert.NotNull(active);
        Assert.Equal(newer.Id, active.Id);
        Assert.Equal(ImportJobStatus.Running, active.Status);
    }

    [Fact]
    public async Task StartAsync_CancelsAbandonedActiveJobForSameProfile()
    {
        var service = new ImportJobService(_contextFactory);
        var abandoned = await service.StartAsync(ImportJobKind.Xtream, profileId: 9, playlistId: 22, sourceName: "Abandoned");

        var replacement = await service.StartAsync(ImportJobKind.Xtream, profileId: 9, playlistId: 22, sourceName: "Replacement");

        await using var context = new AppDbContext(_options);
        var oldPersisted = await context.ImportJobs.SingleAsync(job => job.Id == abandoned.Id);
        Assert.Equal(ImportJobStatus.Canceled, oldPersisted.Status);
        Assert.Equal("Recovered after interruption", oldPersisted.Stage);
        Assert.NotNull(oldPersisted.CompletedAt);
        Assert.Equal(replacement.Id, (await service.GetActiveForProfileAsync(9))?.Id);
        Assert.Equal(1, await context.ImportJobs.CountAsync(job => job.ProfileId == 9 &&
            (job.Status == ImportJobStatus.Queued || job.Status == ImportJobStatus.Running)));
    }

    [Fact]
    public async Task GetRecentForProfileAsync_ReturnsNewestJobsLimitedToProfile()
    {
        var service = new ImportJobService(_contextFactory);
        await service.StartAsync(ImportJobKind.M3U, profileId: 1, playlistId: null, sourceName: "First");
        await Task.Delay(5);
        var second = await service.StartAsync(ImportJobKind.Xtream, profileId: 1, playlistId: 10, sourceName: "Second");
        await Task.Delay(5);
        var third = await service.StartAsync(ImportJobKind.Stalker, profileId: 1, playlistId: 11, sourceName: "Third");
        await service.StartAsync(ImportJobKind.M3U, profileId: 2, playlistId: null, sourceName: "Other Profile");

        var recent = await service.GetRecentForProfileAsync(profileId: 1, take: 2);

        Assert.Equal(new[] { third.Id, second.Id }, recent.Select(j => j.Id).ToArray());
    }
}
