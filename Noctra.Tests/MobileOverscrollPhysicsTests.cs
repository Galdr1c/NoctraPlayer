using Noctra.Mobile.Behaviors;
using System.Reflection;

namespace Noctra.Tests;

public sealed class MobileOverscrollPhysicsTests
{
    [Theory]
    [InlineData(200, 16)]
    [InlineData(600, 21)]
    [InlineData(1200, 28)]
    public void MaximumTranslation_IsViewportAwareAndClamped(
        double viewportHeight,
        double expected)
    {
        Assert.Equal(
            expected,
            MobileOverscrollPhysics.GetMaximumTranslation(viewportHeight),
            precision: 6);
    }

    [Fact]
    public void Translation_HasNonLinearResistanceAndNeverExceedsMaximum()
    {
        const double viewport = 800;
        var smallPull = MobileOverscrollPhysics.GetTranslation(20, viewport);
        var mediumPull = MobileOverscrollPhysics.GetTranslation(120, viewport);
        var hugePull = MobileOverscrollPhysics.GetTranslation(100_000, viewport);
        var maximum = MobileOverscrollPhysics.GetMaximumTranslation(viewport);

        Assert.InRange(smallPull, 0.001, mediumPull);
        Assert.InRange(mediumPull, smallPull, maximum);
        Assert.InRange(hugePull, maximum - 0.001, maximum);
    }

    [Fact]
    public void EstimatePullDistance_RoundTripsVisibleTranslation()
    {
        const double viewport = 720;
        const double originalPull = 135;
        var translation = MobileOverscrollPhysics.GetTranslation(originalPull, viewport);
        var estimatedPull = MobileOverscrollPhysics.EstimatePullDistance(
            translation,
            viewport);

        Assert.Equal(originalPull, estimatedPull, precision: 6);
    }

    [Fact]
    public void Scale_IsBoundedAndStartsAtIdentity()
    {
        const double viewport = 800;
        var maximum = MobileOverscrollPhysics.GetMaximumTranslation(viewport);

        Assert.Equal(1, MobileOverscrollPhysics.GetScale(0, viewport));
        Assert.Equal(
            1 + MobileOverscrollPhysics.MaxScaleDelta,
            MobileOverscrollPhysics.GetScale(maximum * 10, viewport),
            precision: 6);
    }

    [Fact]
    public void SpringResponse_IsMonotonicAndEndsAtExactRest()
    {
        var previous = MobileOverscrollPhysics.GetSpringRemaining(0);
        Assert.Equal(1, previous, precision: 6);

        for (var i = 1; i <= 100; i++)
        {
            var current = MobileOverscrollPhysics.GetSpringRemaining(i / 100d);
            Assert.InRange(current, 0, previous);
            previous = current;
        }

        Assert.Equal(0, previous, precision: 6);
    }

    [Fact]
    public void GlowOpacity_IsMonotonicClampedAndZeroAtRest()
    {
        var method = typeof(MobileOverscrollPhysics).GetMethod(
            "GetGlowOpacity",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        static double Invoke(MethodInfo methodInfo, double translation) =>
            (double)methodInfo.Invoke(null, new object[] { translation, 800d })!;

        var atRest = Invoke(method!, 0);
        var small = Invoke(method!, 4);
        var medium = Invoke(method!, 14);
        var saturated = Invoke(method!, 100_000);

        Assert.Equal(0, atRest, precision: 6);
        Assert.InRange(small, 0.001, medium);
        Assert.InRange(medium, small, 0.12);
        Assert.Equal(0.12, saturated, precision: 6);
    }

    [Fact]
    public void GlowDepth_IsMonotonicClampedAndZeroAtRest()
    {
        var method = typeof(MobileOverscrollPhysics).GetMethod(
            "GetGlowDepth",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        static double Invoke(MethodInfo methodInfo, double translation) =>
            (double)methodInfo.Invoke(null, new object[] { translation, 800d })!;

        var atRest = Invoke(method!, 0);
        var small = Invoke(method!, 4);
        var medium = Invoke(method!, 14);
        var saturated = Invoke(method!, 100_000);

        Assert.Equal(0, atRest, precision: 6);
        Assert.InRange(small, 0.001, medium);
        Assert.InRange(medium, small, 24);
        Assert.Equal(24, saturated, precision: 6);
    }
}
