using Noctra.Models;
using Xunit;

namespace Noctra.Tests;

public sealed class SubtitleAppearanceDefaultsTests
{
    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    [InlineData(100, 100)]
    [InlineData(128, 50)]
    [InlineData(255, 100)]
    public void NormalizeOpacityPercent_MigratesPercentageAndLegacyAlphaValues(int input, int expected)
    {
        Assert.Equal(expected, SubtitleAppearanceDefaults.NormalizeOpacityPercent(input));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 16)]
    [InlineData(100, 39)]
    [InlineData(128, 50)]
    [InlineData(255, 100)]
    public void LegacyAlphaToOpacityPercent_ConvertsPersistedAlphaValues(int input, int expected)
    {
        Assert.Equal(expected, SubtitleAppearanceDefaults.LegacyAlphaToOpacityPercent(input));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 76)]
    [InlineData(60, 153)]
    [InlineData(85, 217)]
    [InlineData(100, 255)]
    public void OpacityPercentToAlpha_UsesExpectedByteRange(int input, int expected)
    {
        Assert.Equal(expected, SubtitleAppearanceDefaults.OpacityPercentToAlpha(input));
    }

    [Theory]
    [InlineData(40, SubtitleVerticalPosition.Bottom)]
    [InlineData(130, SubtitleVerticalPosition.Bottom)]
    [InlineData(131, SubtitleVerticalPosition.LowerMiddle)]
    [InlineData(410, SubtitleVerticalPosition.LowerMiddle)]
    [InlineData(411, SubtitleVerticalPosition.UpperMiddle)]
    [InlineData(750, SubtitleVerticalPosition.UpperMiddle)]
    [InlineData(751, SubtitleVerticalPosition.Top)]
    [InlineData(900, SubtitleVerticalPosition.Top)]
    public void ResolveLegacyPosition_MapsExistingMargins(
        int legacyMargin,
        SubtitleVerticalPosition expected)
    {
        Assert.Equal(expected, SubtitleAppearanceDefaults.ResolveLegacyPosition(legacyMargin));
    }

    [Theory]
    [InlineData(28, SubtitleTextSize.Small)]
    [InlineData(40, SubtitleTextSize.Medium)]
    [InlineData(60, SubtitleTextSize.Large)]
    [InlineData(72, SubtitleTextSize.ExtraLarge)]
    public void ResolveTextSize_PreservesLegacyDesktopChoices(int legacySize, SubtitleTextSize expected)
    {
        Assert.Equal(expected, SubtitleAppearanceDefaults.ResolveTextSize(legacySize));
    }

    [Theory]
    [InlineData(SubtitleVerticalPosition.Bottom, 40)]
    [InlineData(SubtitleVerticalPosition.LowerMiddle, 40)]
    [InlineData(SubtitleVerticalPosition.UpperMiddle, 900)]
    [InlineData(SubtitleVerticalPosition.Top, 900)]
    public void ToLegacyMargin_DesktopCollapsesMiddlePositionsToSafeEnds(
        SubtitleVerticalPosition position,
        int expected)
    {
        // Masaüstünde yalnızca Bottom ve Top güvenli: ara konumlar (220/600)
        // mutlak margin olduğundan çözünürlüğe bağlı; en yakın güvenli uca eşlenir.
        // Top (>=900) VideoPlayerService'te video yüksekliğine göre dinamik hesaplanır.
        Assert.Equal(expected, SubtitleAppearanceDefaults.ToLegacyMargin(position));
    }

    [Fact]
    public void DefaultTextSize_PreservesDesktopAndMobileScale()
    {
        var settings = new AppSettings();

        Assert.Equal(SubtitleTextSize.Medium, settings.SubtitleTextSize);
        Assert.Equal(24, SubtitleAppearanceDefaults.ResolveMobileFontSize(settings.SubtitleTextSize));
#pragma warning disable CS0618
        Assert.Equal(40, settings.SubtitleFontSize);
#pragma warning restore CS0618
        Assert.Equal(SubtitleVerticalPosition.Bottom, settings.SubtitlePosition);
    }
}
