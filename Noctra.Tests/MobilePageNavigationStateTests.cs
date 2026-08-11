using Noctra.Mobile.Navigation;

namespace Noctra.Tests;

public sealed class MobilePageNavigationStateTests
{
    [Fact]
    public void BeginNavigation_AdvancesGenerationAndRejectsOlderWork()
    {
        var store = new MobilePageNavigationStateStore();

        var first = store.BeginNavigation();
        var second = store.BeginNavigation();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.False(store.IsCurrent(first));
        Assert.True(store.IsCurrent(second));
    }

    [Fact]
    public void SavedOffsets_AreIndependentPerDestination()
    {
        var store = new MobilePageNavigationStateStore();
        store.Save("Movies", new MobilePageScrollState(0, 640));
        store.Save("Series", new MobilePageScrollState(0, 1280));

        Assert.True(store.TryGet("Movies", out var movies));
        Assert.True(store.TryGet("Series", out var series));
        Assert.Equal(640, movies.VerticalOffset);
        Assert.Equal(1280, series.VerticalOffset);
        Assert.False(store.TryGet("Search", out _));
    }

    [Fact]
    public void Save_NormalizesInvalidOrNegativeOffsets()
    {
        var store = new MobilePageNavigationStateStore();

        store.Save("Movies", new MobilePageScrollState(double.NaN, -42));

        Assert.True(store.TryGet("Movies", out var state));
        Assert.Equal(MobilePageScrollState.Empty, state);
    }

    [Fact]
    public void Clamp_UsesCurrentScrollableExtent()
    {
        var state = new MobilePageScrollState(50, 900);

        var clamped = state.Clamp(10, 320);

        Assert.Equal(10, clamped.HorizontalOffset);
        Assert.Equal(320, clamped.VerticalOffset);
    }

    [Fact]
    public void Clear_RemovesProfileSpecificPositionsAndInvalidatesWork()
    {
        var store = new MobilePageNavigationStateStore();
        store.Save("Live", new MobilePageScrollState(0, 500));
        var beforeClear = store.BeginNavigation();

        store.Clear();

        Assert.False(store.TryGet("Live", out _));
        Assert.False(store.IsCurrent(beforeClear));
    }
}
