using System.Reflection;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class PlayerGesturePolicyTests
{
    [Theory]
    [InlineData(0d, 10d, "Pending")]
    [InlineData(17d, 0d, "Pending")]
    [InlineData(30d, 10d, "Rejected")]
    [InlineData(10d, 30d, "Vertical")]
    [InlineData(-10d, -30d, "Vertical")]
    public void Classify_UsesActivationThresholdAndVerticalIntent(
        double deltaX,
        double deltaY,
        string expected)
    {
        var policyType = typeof(PlayerViewModel).Assembly.GetType(
            "Noctra.ViewModels.PlayerGesturePolicy");

        Assert.NotNull(policyType);

        var classify = policyType!.GetMethod(
            "Classify",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(classify);

        var result = classify!.Invoke(
            null,
            [deltaX, deltaY, 18d, 1.35d]);

        Assert.Equal(expected, result?.ToString());
    }
}
