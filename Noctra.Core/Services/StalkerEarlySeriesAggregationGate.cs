namespace Noctra.Services;

internal sealed class StalkerEarlySeriesAggregationGate
{
    private int _claimed;

    public bool TryClaim(string categoryType, int channelCount, bool completed)
    {
        if (!completed ||
            channelCount <= 0 ||
            !categoryType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
    }
}
