using Noctra.Core.Advertising;

namespace Noctra.Tests.Advertising;

public sealed class AdPlacementPlannerTests
{
    [Theory]
    [InlineData(1, 14, 28)]
    [InlineData(2, 14, 28)]
    [InlineData(3, 15, 30)]
    [InlineData(4, 16, 32)]
    [InlineData(5, 15, 30)]
    [InlineData(6, 18, 36)]
    public void MoviesPolicy_SnapsToCompleteRows(
        int columns,
        int firstAnchor,
        int secondAnchor)
    {
        var anchors = AdPlacementPlanner.GetContentAnchors(
            columns,
            AdvertisingOptions.ConservativeDefault.Movies);

        Assert.Equal(new[] { firstAnchor, secondAnchor }, anchors);
    }

    [Fact]
    public void DisabledPolicy_ReturnsNoAnchors()
    {
        var anchors = AdPlacementPlanner.GetContentAnchors(
            3,
            new NativeAdPlacementOptions(false, 14, 2));

        Assert.Empty(anchors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidColumnCount_ReturnsNoAnchors(int columns)
    {
        var anchors = AdPlacementPlanner.GetContentAnchors(
            columns,
            AdvertisingOptions.ConservativeDefault.Movies);

        Assert.Empty(anchors);
    }

    [Fact]
    public void LiveDefault_IsMoreConservativeThanMovies()
    {
        var options = AdvertisingOptions.ConservativeDefault;

        Assert.Equal(14, options.Movies.MinContentSpacing);
        Assert.Equal(2, options.Movies.MaxSlots);
        Assert.Equal(20, options.Live.MinContentSpacing);
        Assert.Equal(1, options.Live.MaxSlots);
    }

    [Fact]
    public void HomeDefault_IsDisabled()
        => Assert.False(AdvertisingOptions.ConservativeDefault.Home.Enabled);


    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void SearchEligibility_UsesRealExactResultCount(int count, bool expected)
    {
        var eligible = AdPlacementPlanner.IsEligibleContentCount(
            count,
            AdvertisingOptions.ConservativeDefault.Search);

        Assert.Equal(expected, eligible);
    }

    [Fact]
    public void SearchDefault_RequiresTenExactResults_AndOneSlot()
    {
        var search = AdvertisingOptions.ConservativeDefault.Search;

        Assert.True(search.Enabled);
        Assert.Equal(10, search.MinContentSpacing);
        Assert.Equal(1, search.MaxSlots);
    }
}
