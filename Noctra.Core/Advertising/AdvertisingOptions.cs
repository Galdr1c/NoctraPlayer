namespace Noctra.Core.Advertising;

public enum AdPlacement
{
    None = 0,
    PlaybackExit
}

public sealed record InterstitialAdOptions(
    bool Enabled,
    TimeSpan MinSessionAge,
    TimeSpan MinPlaybackDuration,
    TimeSpan Cooldown,
    int MaxPerHour,
    int MaxPerDay,
    bool AllowLiveContent = false);

/// <summary>
/// Conservative defaults for Noctra's advertising rollout.
/// Keep these values remotely configurable in the production provider;
/// the UI only consumes the normalized policy exposed here.
/// </summary>
public sealed class AdvertisingOptions
{
    public static AdvertisingOptions ConservativeDefault { get; } = new();

    public InterstitialAdOptions PlaybackExit { get; init; } = new(
        Enabled: true,
        MinSessionAge: TimeSpan.FromMinutes(5),
        MinPlaybackDuration: TimeSpan.FromMinutes(10),
        Cooldown: TimeSpan.FromMinutes(18),
        MaxPerHour: 2,
        MaxPerDay: 4,
        AllowLiveContent: false);
}
