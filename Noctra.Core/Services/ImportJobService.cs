using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Core.Services;

public sealed class ImportJobService : IImportJobService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ImportJobService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ImportJob> StartAsync(
        ImportJobKind kind,
        int? profileId,
        int? playlistId,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var job = new ImportJob
        {
            Kind = kind,
            ProfileId = profileId,
            PlaylistId = playlistId,
            SourceName = sourceName,
            Status = ImportJobStatus.Running,
            Stage = "Starting",
            CreatedAt = now,
            UpdatedAt = now
        };

        context.ImportJobs.Add(job);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return job;
    }

    public async Task ReportProgressAsync(
        int jobId,
        string stage,
        int liveCount,
        int vodCount,
        int seriesCount,
        int failedCategoryCount,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await GetJobOrThrowAsync(context, jobId, cancellationToken).ConfigureAwait(false);

        job.Stage = stage;
        job.LiveCount = Math.Max(0, liveCount);
        job.VodCount = Math.Max(0, vodCount);
        job.SeriesCount = Math.Max(0, seriesCount);
        job.FailedCategoryCount = Math.Max(0, failedCategoryCount);
        job.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AttachPlaylistAsync(int jobId, int playlistId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await GetJobOrThrowAsync(context, jobId, cancellationToken).ConfigureAwait(false);

        job.PlaylistId = playlistId;
        job.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(int jobId, string stage, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await GetJobOrThrowAsync(context, jobId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        job.Status = ImportJobStatus.Completed;
        job.Stage = stage;
        job.ErrorMessage = null;
        job.UpdatedAt = now;
        job.CompletedAt = now;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FailAsync(int jobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await GetJobOrThrowAsync(context, jobId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        job.Status = ImportJobStatus.Failed;
        job.Stage = "Failed";
        job.ErrorMessage = errorMessage;
        job.UpdatedAt = now;
        job.CompletedAt = now;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportJob?> GetActiveForProfileAsync(int? profileId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.ImportJobs
            .AsNoTracking()
            .Where(j => j.ProfileId == profileId &&
                        (j.Status == ImportJobStatus.Running || j.Status == ImportJobStatus.Queued))
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImportJob>> GetRecentForProfileAsync(
        int? profileId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var boundedTake = Math.Clamp(take, 1, 100);

        return await context.ImportJobs
            .AsNoTracking()
            .Where(j => j.ProfileId == profileId)
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .Take(boundedTake)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ImportJob> GetJobOrThrowAsync(
        AppDbContext context,
        int jobId,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        return job ?? throw new InvalidOperationException($"Import job {jobId} was not found.");
    }
}
