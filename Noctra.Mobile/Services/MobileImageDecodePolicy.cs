using System;

namespace Noctra.Mobile.Services;

public enum MobileImageDecodeProfile
{
    Default,
    LiveLogo,
    PosterSmall,
    Backdrop
}

internal struct MobileImageDecodeRequestState
{
    private string? _url;
    private int _pixelWidth;

    public readonly bool HasCurrent => _url is not null && _pixelWidth > 0;

    public readonly bool IsCurrent(string url, int pixelWidth)
        => pixelWidth > 0 &&
           _pixelWidth == pixelWidth &&
           string.Equals(_url, url, StringComparison.OrdinalIgnoreCase);

    public readonly bool ShouldRestart(string url, int pixelWidth, bool isEligible)
        => isEligible &&
           pixelWidth > 0 &&
           !IsCurrent(url, pixelWidth);

    public void SetCurrent(string url, int pixelWidth)
    {
        _url = url;
        _pixelWidth = pixelWidth;
    }

    public void Invalidate()
    {
        _url = null;
        _pixelWidth = 0;
    }
}

internal static class MobileImageDecodePolicy
{
    private const int MinimumDecodePixelWidth = 64;
    private const int MaximumDecodePixelWidth = 2048;

    private static readonly int[] DecodeWidthBuckets =
    [
        64,
        96,
        128,
        160,
        192,
        256,
        320,
        384,
        512,
        640,
        768,
        960,
        1280,
        1536,
        2048
    ];

    public static double FirstUsableWidth(double preferredWidth, double fallbackWidth)
    {
        if (double.IsFinite(preferredWidth) && preferredWidth > 0)
        {
            return preferredWidth;
        }

        return double.IsFinite(fallbackWidth) && fallbackWidth > 0
            ? fallbackWidth
            : 0;
    }

    public static int ResolvePixelWidth(
        MobileImageDecodeProfile profile,
        double logicalWidth,
        double renderScaling,
        int requestedDecodePixelWidth)
    {
        if (profile == MobileImageDecodeProfile.Default)
        {
            return Math.Clamp(
                requestedDecodePixelWidth,
                MinimumDecodePixelWidth,
                MaximumDecodePixelWidth);
        }

        if (!double.IsFinite(logicalWidth) || logicalWidth <= 0 ||
            !double.IsFinite(renderScaling) || renderScaling <= 0)
        {
            return 0;
        }

        var (minimum, maximum) = profile switch
        {
            MobileImageDecodeProfile.LiveLogo => (64, 96),
            MobileImageDecodeProfile.PosterSmall => (160, 384),
            MobileImageDecodeProfile.Backdrop => (256, 768),
            _ => (MinimumDecodePixelWidth, MaximumDecodePixelWidth)
        };
        var physicalWidth = Math.Clamp(logicalWidth * renderScaling, minimum, maximum);

        foreach (var bucket in DecodeWidthBuckets)
        {
            if (bucket >= physicalWidth)
            {
                return Math.Clamp(bucket, minimum, maximum);
            }
        }

        return maximum;
    }
}
