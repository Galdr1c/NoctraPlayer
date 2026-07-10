using Noctra.Models;

namespace Noctra.ViewModels;

public partial class MainViewModel
{
    private async Task<ImportJob?> StartProviderImportJobAsync(
        ImportJobKind kind,
        Profile profile,
        Playlist playlist,
        string stage,
        CancellationToken cancellationToken)
    {
        var result = await _providerImportJobCoordinator.StartAsync(kind, profile, playlist, stage, cancellationToken);
        if (result.Status is { } status)
        {
            ApplyActiveImportJobStatus(status);
        }

        return result.Job;
    }

    private async Task ReportProviderImportJobProgressAsync(
        ImportJob? importJob,
        string stage,
        IEnumerable<Channel> channels,
        int failedCategoryCount = 0,
        CancellationToken cancellationToken = default)
    {
        var status = await _providerImportJobCoordinator.ReportProgressAsync(
            importJob,
            stage,
            channels,
            failedCategoryCount,
            cancellationToken);

        if (status is { } activeStatus)
        {
            ApplyActiveImportJobStatus(activeStatus);
        }
    }

    private async Task ReportProviderImportJobProgressAsync(
        ImportJob? importJob,
        string stage,
        int liveCount,
        int vodCount,
        int seriesCount,
        int failedCategoryCount = 0,
        CancellationToken cancellationToken = default)
    {
        var status = await _providerImportJobCoordinator.ReportProgressAsync(
            importJob,
            stage,
            liveCount,
            vodCount,
            seriesCount,
            failedCategoryCount,
            cancellationToken);

        if (status is { } activeStatus)
        {
            ApplyActiveImportJobStatus(activeStatus);
        }
    }

    private async Task CompleteProviderImportJobAsync(
        ImportJob? importJob,
        string stage,
        CancellationToken cancellationToken)
    {
        var status = await _providerImportJobCoordinator.CompleteAsync(importJob, stage, cancellationToken);
        if (status is { } activeStatus)
        {
            ApplyActiveImportJobStatus(activeStatus);
        }
    }

    private async Task FailProviderImportJobAsync(ImportJob? importJob, Exception exception)
    {
        var status = await _providerImportJobCoordinator.FailAsync(importJob, exception);
        if (status is { } activeStatus)
        {
            ApplyActiveImportJobStatus(activeStatus);
        }
    }

    private async Task CancelProviderImportJobAsync(ImportJob? importJob, string stage = "Canceled")
    {
        var status = await _providerImportJobCoordinator.CancelAsync(importJob, stage);
        if (status is { } activeStatus)
        {
            ApplyActiveImportJobStatus(activeStatus);
        }
    }

    public async Task RefreshActiveImportJobStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _importJobStatusCoordinator.RefreshAsync(CurrentProfileId, cancellationToken);
        ApplyActiveImportJobStatus(status);
    }

    private void ClearActiveImportJobStatus()
        => ApplyActiveImportJobStatus(ActiveImportJobStatus.Clear);

    private void ApplyActiveImportJobStatus(
        string stage,
        int liveCount,
        int vodCount,
        int seriesCount,
        int failedCategoryCount)
        => ApplyActiveImportJobStatus(ImportJobStatusCoordinator.CreateProgress(
            stage,
            liveCount,
            vodCount,
            seriesCount,
            failedCategoryCount));

    private void ApplyActiveImportJobStatus(ActiveImportJobStatus status)
    {
        _dispatcherService.BeginInvoke(() =>
        {
            HasActiveImportJob = status.HasActiveImportJob;
            ActiveImportJobStage = status.Stage;
            ActiveImportJobLiveCount = status.LiveCount;
            ActiveImportJobVodCount = status.VodCount;
            ActiveImportJobSeriesCount = status.SeriesCount;
            ActiveImportJobFailedCategoryCount = status.FailedCategoryCount;
        });
    }
}
