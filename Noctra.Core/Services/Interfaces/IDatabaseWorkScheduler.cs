namespace Noctra.Services.Interfaces;

public interface IDatabaseWorkScheduler
{
    Task<T> ScheduleAsync<T>(
        DatabaseWorkLane lane,
        DatabaseWorkPriority priority,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}

