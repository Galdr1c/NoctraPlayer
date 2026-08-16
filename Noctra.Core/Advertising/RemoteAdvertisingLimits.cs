namespace Noctra.Core.Advertising;

/// <summary>
/// Safe envelopes for remote configuration values. The server can never dictate
/// unbounded values to the app: any value outside these ranges is rejected and
/// the in-app default for that field is kept (fail-closed). Every
/// <see cref="AdvertisingOptions.ConservativeDefault"/> value falls inside its
/// envelope, so re-parsing defaults always succeeds.
///
/// Native:
/// - spacing: 6..100 content items between ads (default 14)
/// - max slots: 0..8 per section (default 2) — bounds the anchor array
///   allocation in <see cref="AdPlacementPlanner"/>
///
/// Interstitial (playback exit):
/// - min session age: 1..1440 minutes (default 5)
/// - min playback: 5..120 minutes (default 10)
/// - cooldown: 10..1440 minutes (default 18)
/// - max per hour: 0..5 (default 2; 0 = no interstitials, never "unlimited")
/// - max per day: 0..12 (default 4; 0 = no interstitials, never "unlimited")
/// </summary>
public static class RemoteAdvertisingLimits
{
    public const int NativeMinSpacing = 6;
    public const int NativeMaxSpacing = 100;
    public const int NativeMaxSlots = 8;

    public const int InterstitialMinSessionAgeMinutes = 1;
    public const int InterstitialMaxSessionAgeMinutes = 1440;
    public const int InterstitialMinPlaybackMinutes = 5;
    public const int InterstitialMaxPlaybackMinutes = 120;
    public const int InterstitialMinCooldownMinutes = 10;
    public const int InterstitialMaxCooldownMinutes = 1440;
    public const int InterstitialMaxPerHour = 5;
    public const int InterstitialMaxPerDay = 12;
}