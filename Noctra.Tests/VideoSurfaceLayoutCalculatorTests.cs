using Noctra.Models;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class VideoSurfaceLayoutCalculatorTests
{
    [Fact]
    public void Fit_UltrawideVideoInsidePortraitTarget_PreservesAspectAndCentersVertically()
    {
        var result = VideoSurfaceLayoutCalculator.Calculate(
            targetX: 0,
            targetY: 0,
            targetWidth: 1080,
            targetHeight: 1920,
            videoWidth: 3840,
            videoHeight: 1600,
            pixelWidthHeightRatio: 1f,
            VideoScaleMode.Fit);

        Assert.Equal(new VideoSurfaceRect(0, 735, 1080, 450), result);
    }

    [Fact]
    public void Fit_FourByThreeVideoInsideLandscapeTarget_PreservesAspectAndCentersHorizontally()
    {
        var result = VideoSurfaceLayoutCalculator.Calculate(
            0, 0, 1920, 1080,
            1440, 1080,
            1f,
            VideoScaleMode.Fit);

        Assert.Equal(new VideoSurfaceRect(240, 0, 1440, 1080), result);
    }

    [Theory]
    [InlineData(VideoScaleMode.Fill)]
    [InlineData(VideoScaleMode.Stretch)]
    public void NonFitModes_KeepTheRequestedSurfaceBounds(VideoScaleMode scaleMode)
    {
        var result = VideoSurfaceLayoutCalculator.Calculate(
            11, 23, 1080, 1920,
            3840, 1600,
            1f,
            scaleMode);

        Assert.Equal(new VideoSurfaceRect(11, 23, 1080, 1920), result);
    }

    [Fact]
    public void Fit_UsesPixelWidthHeightRatioForAnamorphicVideo()
    {
        var result = VideoSurfaceLayoutCalculator.Calculate(
            0, 0, 1920, 1080,
            720, 576,
            64f / 45f,
            VideoScaleMode.Fit);

        Assert.Equal(new VideoSurfaceRect(0, 0, 1920, 1080), result);
    }

    [Theory]
    [InlineData(VideoScaleMode.Fill)]
    [InlineData(VideoScaleMode.Stretch)]
    public void NonFitModes_WithAnamorphicVideo_KeepTheRequestedSurfaceBounds(VideoScaleMode scaleMode)
    {
        var result = VideoSurfaceLayoutCalculator.Calculate(
            0, 0, 1920, 1080,
            720, 576,
            64f / 45f,
            scaleMode);

        Assert.Equal(new VideoSurfaceRect(0, 0, 1920, 1080), result);
    }
}
