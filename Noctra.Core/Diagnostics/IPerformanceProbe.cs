namespace Noctra.Diagnostics;

public interface IPerformanceProbe
{
    bool IsEnabled { get; }

    void Mark(string name, long value = 0, string? scope = null);
}

public sealed class NullPerformanceProbe : IPerformanceProbe
{
    public static NullPerformanceProbe Instance { get; } = new();

    private NullPerformanceProbe()
    {
    }

    public bool IsEnabled => false;

    public void Mark(string name, long value = 0, string? scope = null)
    {
    }
}
