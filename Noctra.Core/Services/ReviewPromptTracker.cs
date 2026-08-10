using System;
using System.Threading.Tasks;

namespace Noctra.Services;

/// <summary>
/// Records the user experiences that gate the in-app review prompt
/// (successful provider adds and meaningful playback sessions).
///
/// Platform prompt services read the accumulated counters via
/// <see cref="ReviewPromptPolicy.IsEligible"/>; this tracker simply persists
/// the events as they happen. Events are global (stored with the global
/// settings), so they survive profile switching.
/// </summary>
public sealed class ReviewPromptTracker
{
    private readonly ISettingsService _settingsService;

    public ReviewPromptTracker(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Raised after a recorded experience may have made the user eligible for
    /// the review prompt. Platform prompt services subscribe and call their
    /// event-driven <c>TryShowPromptAsync</c> entry point.
    /// </summary>
    public event Action? PromptRequested;

    /// <summary>
    /// Records that a provider (profile/playlist) was added successfully.
    /// </summary>
    public void RecordProviderAdded()
    {
        var settings = _settingsService.Settings;
        settings.ReviewPromptProviderAddCount++;
        _ = SaveBestEffortAsync();
        RaisePromptRequestedIfEligible();
    }

    /// <summary>
    /// Records a completed playback session with the real watched duration.
    /// Only sessions that reached a meaningful length increment the session
    /// count; cumulative watch time always accrues.
    /// </summary>
    public void RecordPlaybackSession(TimeSpan watchedDuration)
    {
        if (watchedDuration <= TimeSpan.Zero)
        {
            return;
        }

        var settings = _settingsService.Settings;
        settings.ReviewPromptTotalPlaybackSeconds += watchedDuration.TotalSeconds;

        if (watchedDuration >= ReviewPromptPolicy.MeaningfulPlaybackSession)
        {
            settings.ReviewPromptPlaybackCount++;
        }

        _ = SaveBestEffortAsync();
        RaisePromptRequestedIfEligible();
    }

    private void RaisePromptRequestedIfEligible()
    {
        if (PromptRequested is null)
        {
            return;
        }

        if (!ReviewPromptPolicy.IsEligible(_settingsService.Settings, DateTime.UtcNow))
        {
            return;
        }

        PromptRequested?.Invoke();
    }

    private Task SaveBestEffortAsync()
        => _settingsService.SaveAsyncBestEffort();
}
