using System;

namespace Noctra.Mobile.Services;

/// <summary>
/// Small platform-neutral bridge for lifecycle signals that require a visual-tree repair.
/// Android raises this only after its decor view has returned to the UI queue.
/// </summary>
public static class MobileAppLifecycle
{
    public static event EventHandler? Resumed;

    public static void NotifyResumed()
        => Resumed?.Invoke(null, EventArgs.Empty);
}
