namespace Noctra.Mobile.Services;

/// <summary>
/// Lifecycle state reported by a platform banner provider.
/// </summary>
public enum BannerAdLoadState
{
    Idle,
    Loading,
    Loaded,
    Failed
}

/// <summary>
/// Platform-neutral state machine for the persistent banner control.
/// A generation makes callbacks from a disposed/replaced ad unable to revive
/// a newer banner request.
/// </summary>
public sealed class BannerAdPresentationState
{
    public BannerAdLoadState LoadState { get; private set; }

    public bool HasHandle { get; private set; }

    public bool IsSuppressed { get; private set; }

    public long Generation { get; private set; }

    public bool IsVisible => HasHandle &&
        LoadState == BannerAdLoadState.Loaded &&
        !IsSuppressed;

    public long BeginLoad()
    {
        Generation++;
        HasHandle = true;
        LoadState = BannerAdLoadState.Loading;
        return Generation;
    }

    public bool ApplyLoadState(long generation, BannerAdLoadState state)
    {
        if (generation != Generation)
        {
            return false;
        }

        LoadState = state;
        return true;
    }

    public void SetSuppressed(bool suppressed)
        => IsSuppressed = suppressed;

    public void Clear()
    {
        Generation++;
        HasHandle = false;
        LoadState = BannerAdLoadState.Idle;
    }
}
