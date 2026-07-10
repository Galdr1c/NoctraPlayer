using Microsoft.Extensions.Logging;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

internal readonly record struct ActiveImportJobStatus(
    bool HasActiveImportJob,
    string Stage,
    int LiveCount,
    int VodCount,
    int SeriesCount,
    int FailedCategoryCount)
{
    public static ActiveImportJobStatus Clear { get; } = new(false, string.Empty, 0, 0, 0, 0);
}

internal sealed class ImportJobStatusCoordinator
{
    private readonly IImportJobService? _importJobService;
    private readonly ILogger? _logger;

    public ImportJobStatusCoordinator(IImportJobService? importJobService, ILogger? logger)
    {
        _importJobService = importJobService;
        _logger = logger;
    }

    public static ActiveImportJobStatus CreateProgress(
        string stage,
        IEnumerable<Channel> channels,
        int failedCategoryCount = 0)
    {
        var liveCount = 0;
        var vodCount = 0;
        var seriesCount = 0;

        foreach (var channel in channels)
        {
            switch (channel.Type)
            {
                case ChannelType.Live:
                    liveCount++;
                    break;
                case ChannelType.VOD:
                    vodCount++;
                    break;
                case ChannelType.Series:
                    seriesCount++;
                    break;
            }
        }

        return CreateProgress(stage, liveCount, vodCount, seriesCount, failedCategoryCount);
    }

    public static ActiveImportJobStatus CreateProgress(
        string stage,
        int liveCount,
        int vodCount,
        int seriesCount,
        int failedCategoryCount = 0)
        => new(true, stage, liveCount, vodCount, seriesCount, failedCategoryCount);

    public async Task<ActiveImportJobStatus> RefreshAsync(
        int? currentProfileId,
        CancellationToken cancellationToken = default)
    {
        if (_importJobService is null || currentProfileId is null)
        {
            return ActiveImportJobStatus.Clear;
        }

        try
        {
            var job = await _importJobService.GetActiveForProfileAsync(currentProfileId.Value, cancellationToken);
            return job is null
                ? ActiveImportJobStatus.Clear
                : CreateProgress(
                    job.Stage,
                    job.LiveCount,
                    job.VodCount,
                    job.SeriesCount,
                    job.FailedCategoryCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to refresh active import job status for profile {ProfileId}.", currentProfileId);
            return ActiveImportJobStatus.Clear;
        }
    }
}
