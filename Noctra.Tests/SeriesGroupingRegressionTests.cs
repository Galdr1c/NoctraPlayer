using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

public class SeriesGroupingRegressionTests
{
    [Theory]
    [InlineData("%3 S01 %3", "3", 1, 1)]
    [InlineData("%3 S02 %3", "3", 2, 1)]
    [InlineData("Stranger Things (2016) S03", "stranger things", 3, 1)]
    [InlineData("DIZIAX Stranger Things", "stranger things", 1, 1)]
    [InlineData("NETFLIX %3 S01 %3", "3", 1, 1)]
    public void NormalizeKey_Regressions(string title, string expectedKey, int expectedSeason, int expectedEpisode)
    {
        var key = SeriesInfoParser.NormalizeKey(title);
        var info = SeriesInfoParser.Parse(title);

        Assert.Contains(expectedKey, key);
        Assert.Equal(expectedSeason, info.Season);
    }

    [Theory]
    [InlineData("%3 S01 %3", 1)]
    [InlineData("%3 S02 %3", 2)]
    [InlineData("Stranger Things (2016) S03", 3)]
    public void Parse_ShouldDetectSeasonFromSNumber(string title, int expectedSeason)
    {
        var result = SeriesInfoParser.Parse(title);
        Assert.Equal(expectedSeason, result.Season);
    }
}
