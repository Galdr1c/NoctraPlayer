using Microsoft.Extensions.Logging;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

internal readonly record struct ProviderImportJobStartResult(
    ImportJob? Job,
    ActiveImportJobStatus? Status);

internal sealed class ProviderImportJobCoordinator
{
    private readonly IImportJobService? _importJobService;
    private readonly ILogger? _logger;

    public ProviderImportJobCoordinator(IImportJobService? importJobService, ILogger? logger)
    {
        _importJobService = importJobService;
        _logger = logger;
    }

    public async Task<ProviderImportJobStartResult> StartAsync(
        ImportJobKind kind,
        Profile profile,
        Playlist playlist,
        string stage,
        CancellationToken cancellationToken = default)
    {
        if (_importJobService is null)
        {
            return new(null, null);
        }

        try
        {
            var job = await _importJobService.StartAsync(
                kind,
                profile.Id,
                playlist.Id,
                playlist.Name,
                cancellationToken);

            return new(job, ImportJobStatusCoordinator.CreateProgress(stage, Array.Empty<Channel>()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start provider import job for profile {ProfileId}, playlist {PlaylistId}.", profile.Id, playlist.Id);
            return new(null, null);
        }
    }

    public async Task<ActiveImportJobStatus?> ReportProgressAsync(
        ImportJob? importJob,
        string stage,
        IEnumerable<Channel> channels,
        int failedCategoryCount = 0,
        CancellationToken cancellationToken = default)
    {
        var status = ImportJobStatusCoordinator.CreateProgress(stage, channels, failedCategoryCount);
        return await ReportProgressAsync(importJob, status, cancellationToken);
    }

    public async Task<ActiveImportJobStatus?> ReportProgressAsync(
        ImportJob? importJob,
        string stage,
        int liveCount,
        int vodCount,
        int seriesCount,
        int failedCategoryCount = 0,
        CancellationToken cancellationToken = default)
    {
        var status = ImportJobStatusCoordinator.CreateProgress(
            stage,
            liveCount,
            vodCount,
            seriesCount,
            failedCategoryCount);
        return await ReportProgressAsync(importJob, status, cancellationToken);
    }

    public async Task<ActiveImportJobStatus?> CompleteAsync(
        ImportJob? importJob,
        string stage,
        CancellationToken cancellationToken = default)
    {
        if (_importJobService is null || importJob is null)
        {
            return null;
        }

        try
        {
            await _importJobService.CompleteAsync(importJob.Id, stage, cancellationToken);
            return ActiveImportJobStatus.Clear;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to complete provider import job {ImportJobId}.", importJob.Id);
            return null;
        }
    }

    public async Task<ActiveImportJobStatus?> FailAsync(ImportJob? importJob, Exception exception)
    {
        if (_importJobService is null || importJob is null)
        {
            return null;
        }

        try
        {
            await _importJobService.FailAsync(importJob.Id, exception.GetBaseException().Message);
            return ActiveImportJobStatus.Clear;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to mark provider import job {ImportJobId} as failed.", importJob.Id);
            return null;
        }
    }

    public async Task<ActiveImportJobStatus?> CancelAsync(ImportJob? importJob, string stage = "Canceled")
    {
        if (_importJobService is null || importJob is null)
        {
            return null;
        }

        try
        {
            await _importJobService.CancelAsync(importJob.Id, stage, CancellationToken.None);
            return ActiveImportJobStatus.Clear;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to mark provider import job {ImportJobId} as canceled.", importJob.Id);
            return null;
        }
    }

    private async Task<ActiveImportJobStatus?> ReportProgressAsync(
        ImportJob? importJob,
        ActiveImportJobStatus status,
        CancellationToken cancellationToken)
    {
        if (_importJobService is null || importJob is null)
        {
            return null;
        }

        try
        {
            await _importJobService.ReportProgressAsync(
                importJob.Id,
                status.Stage,
                status.LiveCount,
                status.VodCount,
                status.SeriesCount,
                status.FailedCategoryCount,
                cancellationToken);

            return status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to report provider import job progress for job {ImportJobId}.", importJob.Id);
            return null;
        }
    }
}
