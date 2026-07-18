using Noctra.Services;

namespace Noctra.Tests;

public sealed class StalkerEarlySeriesAggregationGateTests
{
    [Fact]
    public void TryClaim_ClaimsOnlyFirstCompletedNonEmptySeriesBatch()
    {
        var gateType = typeof(StalkerPortalService).Assembly.GetType(
            "Noctra.Services.StalkerEarlySeriesAggregationGate");
        Assert.NotNull(gateType);

        var gate = Activator.CreateInstance(gateType);
        Assert.NotNull(gate);
        var tryClaim = gateType.GetMethod("TryClaim");
        Assert.NotNull(tryClaim);

        bool Claim(string categoryType, int channelCount, bool completed)
            => Assert.IsType<bool>(tryClaim.Invoke(
                gate,
                [categoryType, channelCount, completed]));

        Assert.False(Claim("itv", 10, completed: true));
        Assert.False(Claim("vod", 10, completed: true));
        Assert.False(Claim("series", 0, completed: true));
        Assert.False(Claim("series", 10, completed: false));
        Assert.True(Claim("series", 10, completed: true));
        Assert.False(Claim("series", 10, completed: true));
    }
}
