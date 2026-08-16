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

    [Fact]
    public void HugeSpacing_DoesNotOverflow()
    {
        var policy = new NativeAdPlacementOptions(true, int.MaxValue, 1);

        var anchors = AdPlacementPlanner.GetContentAnchors(6, policy);

        Assert.Single(anchors);
        Assert.Equal(int.MaxValue, anchors[0]);
    }

    [Fact]
    public void HugeMaxSlots_IsCapped_NotAllocated()
    {
        var policy = new NativeAdPlacementOptions(true, 14, int.MaxValue);

        var anchors = AdPlacementPlanner.GetContentAnchors(6, policy);

        Assert.True(anchors.Count <= 8);
        Assert.Equal(new[] { 18, 36 }, anchors.Take(2));
    }

    [Fact]
    public void MaxSlotsZero_ReturnsNoAnchors()
    {
        var policy = new NativeAdPlacementOptions(true, 14, 0);

        var anchors = AdPlacementPlanner.GetContentAnchors(6, policy);

        Assert.Empty(anchors);
    }

    [Theory]
    [InlineData(new[] { 24, 48 }, 0, 0)]
    [InlineData(new[] { 24, 48 }, 1, 0)]
    [InlineData(new[] { 24, 48 }, 13, 0)]
    [InlineData(new[] { 24, 48 }, 23, 0)]
    [InlineData(new[] { 24, 48 }, 24, 1)]
    [InlineData(new[] { 24, 48 }, 30, 1)]
    [InlineData(new[] { 24, 48 }, 47, 1)]
    [InlineData(new[] { 24, 48 }, 48, 2)]
    [InlineData(new[] { 24, 48 }, 500, 2)]
    [InlineData(new int[0], 100, 0)]
    public void GetReachableSlotCount_OnlyCountsReachableAnchors(
        int[] anchors,
        int realContentCount,
        int expected)
    {
        Assert.Equal(
            expected,
            AdPlacementPlanner.GetReachableSlotCount(anchors, realContentCount));
    }

    [Fact]
    public void GetReachableSlotCount_SingleMovie_PrimesNothing()
    {
        var policy = new NativeAdPlacementOptions(true, 14, 2);
        var anchors = AdPlacementPlanner.GetContentAnchors(6, policy);

        Assert.Equal(0, AdPlacementPlanner.GetReachableSlotCount(anchors, 1));
    }

    [Fact]
    public void GetReachableSlotCount_FifteenItemsThreeColumns_PrimesFirstSlotOnly()
    {
        var policy = new NativeAdPlacementOptions(true, 14, 2);
        var anchors = AdPlacementPlanner.GetContentAnchors(3, policy);

        Assert.Equal(new[] { 15, 30 }, anchors);
        Assert.Equal(1, AdPlacementPlanner.GetReachableSlotCount(anchors, 15));
        Assert.Equal(2, AdPlacementPlanner.GetReachableSlotCount(anchors, 30));
    }
}
