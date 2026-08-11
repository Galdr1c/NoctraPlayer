namespace Noctra.Services.Interfaces;

public interface ITmdbEnrichmentScheduler
{
    Task ScheduleAsync(
        string key,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}
