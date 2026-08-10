namespace Noctra.Services.Interfaces;

public interface IReviewPromptService
{
    /// <summary>
    /// Launch-time entry point: increments the launch counter, checks the
    /// event-driven eligibility rules and (if eligible) schedules the prompt
    /// to appear after a short settle delay.
    /// </summary>
    Task TryShowMainWindowPromptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event-driven entry point called after meaningful user experiences
    /// (e.g. a completed playback session). Does not bump the launch counter;
    /// only checks eligibility and shows the prompt immediately if all
    /// conditions are met and the surface is ready.
    /// </summary>
    Task TryShowPromptAsync(CancellationToken cancellationToken = default);
}
