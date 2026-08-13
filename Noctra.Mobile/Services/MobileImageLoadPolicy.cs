using System.Threading;

namespace Noctra.Mobile.Services;

internal static class MobileImageLoadPolicy
{
    public static bool CanStart(
        bool isForeground,
        bool isSurfaceActive,
        bool isAttached,
        bool isVisible)
        => isForeground && isSurfaceActive && isAttached && isVisible;
}

internal sealed class MobileImageSourceMutationState
{
    private long _generation;

    public long BeginMutation()
        => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation)
        => Volatile.Read(ref _generation) == generation;
}
