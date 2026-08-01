using System;

namespace Noctra.Mobile.Behaviors;

internal static class MobileOverscrollPhysics
{
    public const double EdgeTolerance = 0.75;
    public const double FallbackActivationDistance = 5;
    public const double AxisLockRatio = 1.15;
    public const double MaxScaleDelta = 0.026;
    public const double FlingBounceDistance = 8;
    public static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan FlingReleaseDuration = TimeSpan.FromMilliseconds(210);

    private const double Resistance = 4.6;

    public static double GetMaximumTranslation(double viewportHeight)
        => Math.Clamp(viewportHeight * 0.035, 16, 28);

    public static double GetTranslation(double pullDistance, double viewportHeight)
    {
        if (pullDistance <= 0 || viewportHeight <= 0)
        {
            return 0;
        }

        var maximum = GetMaximumTranslation(viewportHeight);
        var normalizedPull = pullDistance / Math.Max(1, viewportHeight);
        return maximum * (1 - Math.Exp(-normalizedPull * Resistance));
    }

    public static double GetScale(double translation, double viewportHeight)
    {
        var maximum = GetMaximumTranslation(viewportHeight);
        var progress = maximum <= 0 ? 0 : Math.Clamp(translation / maximum, 0, 1);
        return 1 + (MaxScaleDelta * progress);
    }

    public static double EstimatePullDistance(double translation, double viewportHeight)
    {
        var maximum = GetMaximumTranslation(viewportHeight);
        if (translation <= 0 || maximum <= 0)
        {
            return 0;
        }

        var ratio = Math.Clamp(translation / maximum, 0, 0.985);
        return -Math.Log(1 - ratio) * Math.Max(1, viewportHeight) / Resistance;
    }

    /// <summary>
    /// Critically damped spring response normalized to 1 at t=0 and exactly
    /// 0 at the end. It returns quickly without a rubber-band oscillation.
    /// </summary>
    public static double GetSpringRemaining(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var spring = (1 + (8 * progress)) * Math.Exp(-8 * progress);
        var exactRestEnvelope = 1 - (progress * progress * (3 - (2 * progress)));
        return spring * exactRestEnvelope;
    }
}
