using System;
using System.Threading;

namespace Noctra.Mobile.Services;

/// <summary>
/// Small platform-neutral bridge for lifecycle signals that require a visual-tree repair.
/// Android raises this only after its decor view has returned to the UI queue.
/// </summary>
public static class MobileAppLifecycle
{
    private static readonly object Sync = new();
    private static int _isForeground = 1;
    private static long _generation;

    public static bool IsForeground => Volatile.Read(ref _isForeground) == 1;

    public static bool IsGenerationCurrent(long generation)
    {
        lock (Sync)
        {
            return generation == _generation &&
                   Volatile.Read(ref _isForeground) == 1;
        }
    }

    public static event EventHandler? Resumed;
    public static event EventHandler? Paused;

    public static long BeginResume()
    {
        lock (Sync)
        {
            return ++_generation;
        }
    }

    public static bool TryNotifyResumed(long generation)
    {
        EventHandler? handlers;
        lock (Sync)
        {
            if (generation != _generation)
            {
                return false;
            }

            Volatile.Write(ref _isForeground, 1);
            handlers = Resumed;
        }

        InvokeIsolated(handlers);
        return true;
    }

    public static void NotifyPaused()
    {
        EventHandler? handlers;
        lock (Sync)
        {
            _generation++;
            Volatile.Write(ref _isForeground, 0);
            handlers = Paused;
        }

        InvokeIsolated(handlers);
    }

    /// <summary>
    /// Raises each subscriber separately: grids, the banner control, and the
    /// native ad hosts all listen here, so one throwing handler must never
    /// leave the remaining subscribers without their resume/pause signal
    /// (that surfaced as "app came back but parts never recovered").
    /// </summary>
    private static void InvokeIsolated(EventHandler? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var candidate in handlers.GetInvocationList())
        {
            if (candidate is not EventHandler handler)
            {
                continue;
            }

            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Noctra] Lifecycle handler failed: {ex}");
            }
        }
    }
}
