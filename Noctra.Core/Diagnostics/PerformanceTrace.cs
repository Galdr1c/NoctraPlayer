using System.Threading;

namespace Noctra.Diagnostics;

public static class PerformanceTrace
{
    private static IPerformanceProbe _probe = NullPerformanceProbe.Instance;

    public static IPerformanceProbe Probe
    {
        get => Volatile.Read(ref _probe);
        set => Volatile.Write(ref _probe, value ?? NullPerformanceProbe.Instance);
    }

    public static void Mark(string name, long value = 0, string? scope = null)
    {
        var probe = Probe;
        if (probe.IsEnabled)
        {
            probe.Mark(name, value, scope);
        }
    }
}
