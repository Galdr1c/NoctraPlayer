using System;
using Noctra.Models;

namespace Noctra.Services;

public readonly record struct VideoSurfaceRect(
    int X,
    int Y,
    int Width,
    int Height);

/// <summary>
/// Calculates the native video surface rectangle independently from decoder
/// scaling. Fit owns aspect-ratio placement; Fill/Stretch keep the host bounds
/// and let the platform renderer crop or stretch inside that rectangle.
/// </summary>
public static class VideoSurfaceLayoutCalculator
{
    public static VideoSurfaceRect Calculate(
        int targetX,
        int targetY,
        int targetWidth,
        int targetHeight,
        int videoWidth,
        int videoHeight,
        float pixelWidthHeightRatio,
        VideoScaleMode scaleMode)
    {
        var target = new VideoSurfaceRect(
            targetX,
            targetY,
            Math.Max(0, targetWidth),
            Math.Max(0, targetHeight));

        if (scaleMode != VideoScaleMode.Fit ||
            target.Width <= 0 ||
            target.Height <= 0 ||
            videoWidth <= 0 ||
            videoHeight <= 0)
        {
            return target;
        }

        var pixelRatio = pixelWidthHeightRatio > 0
            ? pixelWidthHeightRatio
            : 1f;
        var videoAspect = videoWidth * (double)pixelRatio / videoHeight;
        var targetAspect = target.Width / (double)target.Height;

        if (Math.Abs(videoAspect - targetAspect) < 0.0001d)
        {
            return target;
        }

        if (videoAspect > targetAspect)
        {
            var fittedHeight = Math.Clamp(
                (int)Math.Round(
                    target.Width / videoAspect,
                    MidpointRounding.AwayFromZero),
                1,
                target.Height);
            return new VideoSurfaceRect(
                target.X,
                target.Y + ((target.Height - fittedHeight) / 2),
                target.Width,
                fittedHeight);
        }

        var fittedWidth = Math.Clamp(
            (int)Math.Round(
                target.Height * videoAspect,
                MidpointRounding.AwayFromZero),
            1,
            target.Width);
        return new VideoSurfaceRect(
            target.X + ((target.Width - fittedWidth) / 2),
            target.Y,
            fittedWidth,
            target.Height);
    }
}
