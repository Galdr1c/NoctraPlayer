namespace Noctra.ViewModels;

/// <summary>
/// Coalesces Home/History rail refreshes while a player session is active.
/// Playback progress persistence remains independent from this UI gate.
/// </summary>
internal sealed class ContinueWatchingRefreshGate
{
    private int _playbackActive;
    private int _refreshPending;

    public bool IsPlaybackActive =>
        Volatile.Read(ref _playbackActive) == 1;

    public void BeginPlayback()
    {
        Volatile.Write(ref _playbackActive, 1);
    }

    public bool RequestRefresh()
    {
        if (!IsPlaybackActive)
        {
            return true;
        }

        Volatile.Write(ref _refreshPending, 1);
        return false;
    }

    public bool EndPlayback()
    {
        var wasActive = Interlocked.Exchange(ref _playbackActive, 0) == 1;
        var hadPendingRefresh = Interlocked.Exchange(ref _refreshPending, 0) == 1;
        return wasActive || hadPendingRefresh;
    }
}
