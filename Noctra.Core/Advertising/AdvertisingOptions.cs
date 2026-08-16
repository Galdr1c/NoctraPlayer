namespace Noctra.Core.Advertising;

public enum AdPlacement
{
    None = 0,
    HomeFeed,
    MoviesFeed,
    SeriesFeed,
    LiveFeed,
    SearchFeed,
    PlaybackExit
}

public sealed record NativeAdPlacementOptions(
    bool Enabled,
    int MinContentSpacing,
    int MaxSlots);

public sealed record InterstitialAdOptions(
    bool Enabled,
    TimeSpan MinSessionAge,
    TimeSpan MinPlaybackDuration,
    TimeSpan Cooldown,
    int MaxPerHour,
    int MaxPerDay,
    bool AllowLiveContent = false);

/// <summary>
/// Conservative defaults for Noctra's first advertising rollout.
/// Keep these values remotely configurable in the production provider;
/// the UI only consumes the normalized policy exposed here.
/// </summary>
public sealed class AdvertisingOptions
{
    public static AdvertisingOptions ConservativeDefault { get; } = new();

    public NativeAdPlacementOptions Home { get; init; } = new(
        Enabled: false,
        MinContentSpacing: 6,
        MaxSlots: 1);

    public NativeAdPlacementOptions Movies { get; init; } = new(
        Enabled: true,
        MinContentSpacing: 14,
        MaxSlots: 2);

    public NativeAdPlacementOptions Series { get; init; } = new(
        Enabled: true,
        MinContentSpacing: 14,
        MaxSlots: 2);

    public NativeAdPlacementOptions Live { get; init; } = new(
        Enabled: true,
        MinContentSpacing: 20,
        MaxSlots: 1);

    public NativeAdPlacementOptions Search { get; init; } = new(
        Enabled: true,
        MinContentSpacing: 10,
        MaxSlots: 1);

    public InterstitialAdOptions PlaybackExit { get; init; } = new(
        Enabled: true,
        MinSessionAge: TimeSpan.FromMinutes(5),
        MinPlaybackDuration: TimeSpan.FromMinutes(10),
        Cooldown: TimeSpan.FromMinutes(18),
        MaxPerHour: 2,
        MaxPerDay: 4,
        AllowLiveContent: false);

    public NativeAdPlacementOptions GetNative(AdPlacement placement) => placement switch
    {
        AdPlacement.HomeFeed => Home,
        AdPlacement.MoviesFeed => Movies,
        AdPlacement.SeriesFeed => Series,
        AdPlacement.LiveFeed => Live,
        AdPlacement.SearchFeed => Search,
        _ => new NativeAdPlacementOptions(false, int.MaxValue, 0)
    };
}
