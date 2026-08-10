using System;
using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// Shared, event-driven eligibility rules for the in-app review prompt.
///
/// Instead of asking after a fixed wall-clock delay (the old
/// "1 launch + 3 minutes" heuristic), the prompt is only eligible once the
/// user has built up a meaningful, positive experience:
///   - at least 3 launches,
///   - at least 1 provider added successfully,
///   - at least 3 meaningful playback sessions,
///   - at least 30 minutes of cumulative real playback time.
///
/// Surface state (no active player/import/dialog, etc.) is checked separately
/// by each platform's prompt service right before display.
/// </summary>
public static class ReviewPromptPolicy
{
    /// <summary>
    /// Minimum launches before the prompt may appear.
    /// </summary>
    public const int MinimumLaunches = 3;

    /// <summary>
    /// Minimum providers added successfully before the prompt may appear.
    /// </summary>
    public const int MinimumProviderAdds = 1;

    /// <summary>
    /// Minimum meaningful playback sessions before the prompt may appear.
    /// </summary>
    public const int MinimumPlaybackSessions = 3;

    /// <summary>
    /// Minimum cumulative real playback time before the prompt may appear.
    /// </summary>
    public static readonly TimeSpan MinimumTotalPlayback = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A playback session must reach at least this much real watch time to be
    /// counted as "meaningful" (e.g. accidental 5-second opens do not count).
    /// </summary>
    public static readonly TimeSpan MeaningfulPlaybackSession = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cooldown applied when the user chooses "Later": 14 days instead of the
    /// old 3-day snooze, so a hesitant user is not nagged.
    /// </summary>
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(14);

    /// <summary>
    /// Short settle delay between launch and showing the prompt, so the UI has
    /// time to become ready. The old 3-minute wait was a first-run gating
    /// heuristic and is no longer needed — eligibility itself is now
    /// experience-based.
    /// </summary>
    public static readonly TimeSpan LaunchSettleDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Whether the user is eligible to be asked for a review right now.
    /// </summary>
    public static bool IsEligible(AppSettings settings, DateTime nowUtc)
    {
        if (settings.ReviewPromptDismissed || settings.ReviewPromptCompletedAtUtc.HasValue)
        {
            return false;
        }

        if (settings.ReviewPromptLaunchCount < MinimumLaunches)
        {
            return false;
        }

        if (settings.ReviewPromptProviderAddCount < MinimumProviderAdds)
        {
            return false;
        }

        if (settings.ReviewPromptPlaybackCount < MinimumPlaybackSessions)
        {
            return false;
        }

        if (settings.ReviewPromptTotalPlaybackSeconds < MinimumTotalPlayback.TotalSeconds)
        {
            return false;
        }

        if (settings.ReviewPromptSnoozedUntilUtc.HasValue && settings.ReviewPromptSnoozedUntilUtc.Value > nowUtc)
        {
            return false;
        }

        if (settings.ReviewPromptLastShownAtUtc.HasValue &&
            nowUtc - settings.ReviewPromptLastShownAtUtc.Value < SnoozeDuration)
        {
            return false;
        }

        return true;
    }
}
