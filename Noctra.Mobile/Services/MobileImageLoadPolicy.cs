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
