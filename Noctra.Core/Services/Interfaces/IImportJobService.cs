using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IImportJobService
{
    Task<ImportJob> StartAsync(
        ImportJobKind kind,
        int? profileId,
        int? playlistId,
        string sourceName,
        CancellationToken cancellationToken = default);

    Task ReportProgressAsync(
        int jobId,
        string stage,
        int liveCount,
        int vodCount,
        int seriesCount,
        int failedCategoryCount,
        CancellationToken cancellationToken = default);

    Task AttachPlaylistAsync(int jobId, int playlistId, CancellationToken cancellationToken = default);

    Task CompleteAsync(int jobId, string stage, CancellationToken cancellationToken = default);

    Task FailAsync(int jobId, string errorMessage, CancellationToken cancellationToken = default);

    Task CancelAsync(int jobId, string stage, CancellationToken cancellationToken = default);

    Task<ImportJob?> GetActiveForProfileAsync(int? profileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportJob>> GetRecentForProfileAsync(
        int? profileId,
        int take = 10,
        CancellationToken cancellationToken = default);
}
