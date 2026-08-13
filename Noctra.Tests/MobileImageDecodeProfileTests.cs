using Noctra.Mobile.Services;

namespace Noctra.Tests;

public sealed class MobileImageDecodeProfileTests
{
    [Theory]
    [InlineData(0, 64)]
    [InlineData(384, 384)]
    [InlineData(4096, 2048)]
    public void DefaultProfile_PreservesAndClampsExplicitDecodeWidth(int requested, int expected)
        => Assert.Equal(expected, Resolve("Default", logicalWidth: 0, renderScaling: 0, requested));

    [Theory]
    [InlineData(36, 1.0, 64)]
    [InlineData(36, 2.0, 96)]
    [InlineData(36, 2.5, 96)]
    [InlineData(100, 4.0, 96)]
    public void LiveLogoProfile_IsDensityAwareAndBounded(
        double logicalWidth,
        double renderScaling,
        int expected)
        => Assert.Equal(expected, Resolve("LiveLogo", logicalWidth, renderScaling));

    [Theory]
    [InlineData(180, 1.0, 192)]
    [InlineData(180, 1.5, 320)]
    [InlineData(160, 2.0, 320)]
    [InlineData(180, 2.5, 384)]
    [InlineData(400, 3.0, 384)]
    public void PosterSmallProfile_AdaptsWithoutExceedingCardCap(
        double logicalWidth,
        double renderScaling,
        int expected)
        => Assert.Equal(expected, Resolve("PosterSmall", logicalWidth, renderScaling));

    [Theory]
    [InlineData(220, 1.0, 256)]
    [InlineData(300, 2.0, 640)]
    [InlineData(410, 2.5, 768)]
    public void BackdropProfile_UsesLargerButBoundedBuckets(
        double logicalWidth,
        double renderScaling,
        int expected)
        => Assert.Equal(expected, Resolve("Backdrop", logicalWidth, renderScaling));

    [Theory]
    [InlineData("LiveLogo", 0, 2.5)]
    [InlineData("LiveLogo", -1, 2.5)]
    [InlineData("PosterSmall", double.NaN, 2.5)]
    [InlineData("PosterSmall", double.PositiveInfinity, 2.5)]
    [InlineData("Backdrop", 180, 0)]
    [InlineData("Backdrop", 180, -1)]
    [InlineData("Backdrop", 180, double.PositiveInfinity)]
    public void ProfiledImage_DefersUntilLogicalSizeAndScaleAreUsable(
        string profile,
        double logicalWidth,
        double renderScaling)
        => Assert.Equal(0, Resolve(profile, logicalWidth, renderScaling));

    [Fact]
    public void NearbyPosterWidths_ResolveToSameBucket()
    {
        var first = Resolve("PosterSmall", logicalWidth: 168, renderScaling: 1.0);
        var second = Resolve("PosterSmall", logicalWidth: 176, renderScaling: 1.0);

        Assert.Equal(192, first);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(0, 180, 180)]
    [InlineData(double.NaN, 180, 180)]
    [InlineData(160, 180, 160)]
    [InlineData(double.PositiveInfinity, -1, 0)]
    public void LayoutWidth_FallsBackToFirstUsableArrangedAncestor(
        double preferred,
        double fallback,
        double expected)
        => Assert.Equal(expected, MobileImageDecodePolicy.FirstUsableWidth(preferred, fallback));

    [Fact]
    public void RequestState_NewUrlAtSameBucket_IsNotCurrentAfterDeferredInvalidation()
    {
        var state = new MobileImageDecodeRequestState();
        state.SetCurrent("https://example.test/a.png", 192);

        Assert.True(state.IsCurrent("https://example.test/a.png", 192));
        Assert.False(state.IsCurrent("https://example.test/b.png", 192));

        state.Invalidate();

        Assert.False(state.IsCurrent("https://example.test/b.png", 192));
    }

    [Fact]
    public void RequestState_BucketChange_IsNotCurrentButUrlComparisonIsCaseInsensitive()
    {
        var state = new MobileImageDecodeRequestState();
        state.SetCurrent("HTTPS://EXAMPLE.TEST/poster.jpg", 192);

        Assert.True(state.IsCurrent("https://example.test/poster.jpg", 192));
        Assert.False(state.IsCurrent("https://example.test/poster.jpg", 320));
    }

    [Fact]
    public void RequestState_TransientIneligibilityOrInvalidLayout_DoesNotRequestDestructiveRestart()
    {
        var state = new MobileImageDecodeRequestState();
        state.SetCurrent("https://example.test/a.jpg", 192);

        Assert.False(state.ShouldRestart("https://example.test/b.jpg", 192, isEligible: false));
        Assert.False(state.ShouldRestart("https://example.test/b.jpg", 0, isEligible: true));
        Assert.False(state.ShouldRestart("https://example.test/a.jpg", 192, isEligible: true));
        Assert.True(state.ShouldRestart("https://example.test/b.jpg", 192, isEligible: true));
    }

    [Theory]
    [InlineData("Noctra.Mobile/Controls/MobileLiveTvCard.axaml", "DecodeProfile=\"LiveLogo\"")]
    [InlineData("Noctra.Mobile/Controls/MobileVodCard.axaml", "DecodeProfile=\"PosterSmall\"")]
    [InlineData("Noctra.Mobile/Controls/MobileSeriesCard.axaml", "DecodeProfile=\"PosterSmall\"")]
    [InlineData("Noctra.Mobile/Controls/MobileContinueWatchingCard.axaml", "DecodeProfile=\"Backdrop\"")]
    public void PrimaryMobileCards_DeclareTheirDecodeProfile(string relativePath, string contract)
    {
        var source = File.ReadAllText(ProjectFile(relativePath.Split('/')));

        Assert.Contains(contract, source, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteImage_ReevaluatesProfileWhenTopLevelScalingChanges()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs"));

        Assert.Contains("topLevel.ScalingChanged += OnTopLevelScalingChanged;", source, StringComparison.Ordinal);
        Assert.Contains("_subscribedTopLevel.ScalingChanged -= OnTopLevelScalingChanged;", source, StringComparison.Ordinal);
        Assert.Contains("private void OnTopLevelScalingChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteImage_DecodeSettingChangesUseEffectiveRequestIdentity()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs"));

        Assert.Contains(
            "DecodePixelWidthProperty.Changed.AddClassHandler<RemoteImage>((control, _) => control.ReevaluateDecodeRequest());",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DecodeProfileProperty.Changed.AddClassHandler<RemoteImage>((control, _) => control.ReevaluateDecodeRequest());",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteImage_RetriesDeferredProfileAfterAncestorLayoutCompletes()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs"));

        Assert.Contains("LayoutUpdated += OnImageLayoutUpdated;", source, StringComparison.Ordinal);
        Assert.Contains("private void OnImageLayoutUpdated", source, StringComparison.Ordinal);
        Assert.Contains("!_profileRequestState.HasCurrent", source, StringComparison.Ordinal);
    }

    private static int Resolve(
        string profileName,
        double logicalWidth,
        double renderScaling,
        int requestedDecodeWidth = 384)
    {
        var profile = Enum.Parse<MobileImageDecodeProfile>(profileName);
        return MobileImageDecodePolicy.ResolvePixelWidth(
            profile,
            logicalWidth,
            renderScaling,
            requestedDecodeWidth);
    }

    private static string ProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. parts]);
    }
}
