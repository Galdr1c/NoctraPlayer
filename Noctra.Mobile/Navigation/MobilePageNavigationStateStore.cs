using System;
using System.Collections.Generic;
using System.Threading;

namespace Noctra.Mobile.Navigation;

public readonly record struct MobilePageScrollState(
    double HorizontalOffset,
    double VerticalOffset)
{
    public static MobilePageScrollState Empty { get; } = new(0, 0);

    public MobilePageScrollState Normalize()
        => new(
            NormalizeCoordinate(HorizontalOffset),
            NormalizeCoordinate(VerticalOffset));

    public MobilePageScrollState Clamp(double maximumHorizontalOffset, double maximumVerticalOffset)
    {
        var normalized = Normalize();
        return new MobilePageScrollState(
            Math.Min(normalized.HorizontalOffset, NormalizeCoordinate(maximumHorizontalOffset)),
            Math.Min(normalized.VerticalOffset, NormalizeCoordinate(maximumVerticalOffset)));
    }

    private static double NormalizeCoordinate(double value)
        => double.IsFinite(value) && value > 0 ? value : 0;
}

public sealed class MobilePageNavigationStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MobilePageScrollState> _states =
        new(StringComparer.Ordinal);
    private long _generation;

    public long CurrentGeneration => Volatile.Read(ref _generation);

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _states.Count;
            }
        }
    }

    public long BeginNavigation()
        => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation)
        => generation == Volatile.Read(ref _generation);

    public void Save(string destination, MobilePageScrollState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (_sync)
        {
            _states[destination] = state.Normalize();
        }
    }

    public bool TryGet(string destination, out MobilePageScrollState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (_sync)
        {
            return _states.TryGetValue(destination, out state);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _states.Clear();
        }

        Interlocked.Increment(ref _generation);
    }
}
