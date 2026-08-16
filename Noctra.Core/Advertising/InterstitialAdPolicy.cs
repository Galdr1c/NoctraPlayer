namespace Noctra.Core.Advertising;

public enum AdDecisionReason
{
    Eligible = 0,
    Disabled,
    Premium,
    ConsentUnavailable,
    AdNotReady,
    PlaybackNotEstablished,
    PlaybackFailed,
    PictureInPicture,
    BlockingOverlay,
    LiveContent,
    SessionTooYoung,
    PlaybackTooShort,
    Cooldown,
    HourlyCap,
    DailyCap
}

public readonly record struct AdDecision(
    bool ShouldShow,
    AdDecisionReason Reason)
{
    public static AdDecision Allow() => new(true, AdDecisionReason.Eligible);
    public static AdDecision Deny(AdDecisionReason reason) => new(false, reason);
}

public sealed record InterstitialAdContext(
    DateTimeOffset Now,
    DateTimeOffset SessionStartedAt,
    TimeSpan PlaybackDuration,
    bool PlaybackEstablished,
    bool IsLiveContent,
    bool PlaybackFailed,
    bool WasPictureInPicture,
    bool HasBlockingOverlay);

public readonly record struct AdRuntimeEligibility(
    bool IsPremium,
    bool CanRequestAds,
    bool AdReady);

public sealed record InterstitialAdHistory(
    IReadOnlyList<DateTimeOffset> Impressions)
{
    public static InterstitialAdHistory Empty { get; } =
        new(Array.Empty<DateTimeOffset>());
}

/// <summary>
/// Pure, deterministic interstitial policy. It deliberately knows nothing about
/// AdMob/AppLovin/Meta; a mediation provider supplies readiness and records impressions.
/// </summary>
public static class InterstitialAdPolicy
{
    public static AdDecision Evaluate(
        InterstitialAdContext context,
        AdRuntimeEligibility runtime,
        InterstitialAdOptions options,
        InterstitialAdHistory history)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(history);

        if (!options.Enabled)
            return AdDecision.Deny(AdDecisionReason.Disabled);
        if (runtime.IsPremium)
            return AdDecision.Deny(AdDecisionReason.Premium);
        if (!runtime.CanRequestAds)
            return AdDecision.Deny(AdDecisionReason.ConsentUnavailable);
        if (!runtime.AdReady)
            return AdDecision.Deny(AdDecisionReason.AdNotReady);
        if (!context.PlaybackEstablished)
            return AdDecision.Deny(AdDecisionReason.PlaybackNotEstablished);
        if (context.PlaybackFailed)
            return AdDecision.Deny(AdDecisionReason.PlaybackFailed);
        if (context.WasPictureInPicture)
            return AdDecision.Deny(AdDecisionReason.PictureInPicture);
        if (context.HasBlockingOverlay)
            return AdDecision.Deny(AdDecisionReason.BlockingOverlay);
        if (context.IsLiveContent && !options.AllowLiveContent)
            return AdDecision.Deny(AdDecisionReason.LiveContent);
        if (context.Now - context.SessionStartedAt < options.MinSessionAge)
            return AdDecision.Deny(AdDecisionReason.SessionTooYoung);
        if (context.PlaybackDuration < options.MinPlaybackDuration)
            return AdDecision.Deny(AdDecisionReason.PlaybackTooShort);

        var impressions = history.Impressions
            .Where(timestamp => timestamp <= context.Now)
            .OrderByDescending(timestamp => timestamp)
            .ToArray();

        // Caps are fail-closed: 0 (or negative) means "no ads", never
        // "unlimited". The remote config parser only emits 0..N, but the
        // policy also defends against a misbehaving caller.
        if (options.MaxPerHour <= 0)
        {
            return AdDecision.Deny(AdDecisionReason.HourlyCap);
        }

        if (options.MaxPerDay <= 0)
        {
            return AdDecision.Deny(AdDecisionReason.DailyCap);
        }

        if (impressions.Length > 0 &&
            context.Now - impressions[0] < options.Cooldown)
        {
            return AdDecision.Deny(AdDecisionReason.Cooldown);
        }

        if (impressions.Count(timestamp => context.Now - timestamp < TimeSpan.FromHours(1)) >= options.MaxPerHour)
        {
            return AdDecision.Deny(AdDecisionReason.HourlyCap);
        }

        if (impressions.Count(timestamp => context.Now - timestamp < TimeSpan.FromHours(24)) >= options.MaxPerDay)
        {
            return AdDecision.Deny(AdDecisionReason.DailyCap);
        }

        return AdDecision.Allow();
    }
}
