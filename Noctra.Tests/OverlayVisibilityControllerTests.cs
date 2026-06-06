using Noctra.Avalonia.Controls;

namespace Noctra.Tests;

public sealed class OverlayVisibilityControllerTests
{
    [Fact]
    public void Update_ShowsOverlayWhenLayoutBecomesVisible()
    {
        var harness = new OverlayVisibilityHarness();

        harness.Update(isEffectivelyVisible: true);

        Assert.True(harness.OverlayVisible);
        Assert.Equal(1, harness.ShowCallCount);
        Assert.Equal(0, harness.HideCallCount);
    }

    [Fact]
    public void Update_DoesNotShowOverlayTwice()
    {
        var harness = new OverlayVisibilityHarness();

        harness.Update(isEffectivelyVisible: true);
        harness.Update(isEffectivelyVisible: true);

        Assert.Equal(1, harness.ShowCallCount);
    }

    [Fact]
    public void Update_HidesOverlayWhenLayoutBecomesInvisible()
    {
        var harness = new OverlayVisibilityHarness();
        harness.Update(isEffectivelyVisible: true);

        harness.Update(isEffectivelyVisible: false);

        Assert.False(harness.OverlayVisible);
        Assert.Equal(1, harness.HideCallCount);
    }

    [Fact]
    public void Update_DoesNotHideOverlayTwice()
    {
        var harness = new OverlayVisibilityHarness();

        harness.Update(isEffectivelyVisible: false);
        harness.Update(isEffectivelyVisible: false);

        Assert.Equal(0, harness.HideCallCount);
    }

    [Fact]
    public void Update_MakesOverlayTopmostWhenOwnerEntersPiP()
    {
        var harness = new OverlayVisibilityHarness();

        harness.Update(isEffectivelyVisible: true, ownerIsTopmost: true);

        Assert.True(harness.OverlayTopmost);
        Assert.Equal(1, harness.TopmostChangeCount);
    }

    [Fact]
    public void Update_RemovesTopmostWhenOwnerLeavesPiP()
    {
        var harness = new OverlayVisibilityHarness();
        harness.Update(isEffectivelyVisible: true, ownerIsTopmost: true);

        harness.Update(isEffectivelyVisible: true, ownerIsTopmost: false);

        Assert.False(harness.OverlayTopmost);
        Assert.Equal(2, harness.TopmostChangeCount);
    }

    [Fact]
    public void Update_DoesNotRepeatTopmostAssignment()
    {
        var harness = new OverlayVisibilityHarness();

        harness.Update(isEffectivelyVisible: true, ownerIsTopmost: true);
        harness.Update(isEffectivelyVisible: true, ownerIsTopmost: true);

        Assert.Equal(1, harness.TopmostChangeCount);
    }

    private sealed class OverlayVisibilityHarness
    {
        public bool OverlayVisible { get; private set; }
        public bool OverlayTopmost { get; private set; }
        public int ShowCallCount { get; private set; }
        public int HideCallCount { get; private set; }
        public int TopmostChangeCount { get; private set; }

        private readonly OverlayVisibilityController _controller;

        public OverlayVisibilityHarness()
        {
            _controller = new OverlayVisibilityController(
                isOverlayCurrentlyVisible: () => OverlayVisible,
                showOverlay: () =>
                {
                    OverlayVisible = true;
                    ShowCallCount++;
                },
                hideOverlay: () =>
                {
                    OverlayVisible = false;
                    HideCallCount++;
                },
                isOverlayTopmost: () => OverlayTopmost,
                setOverlayTopmost: value =>
                {
                    OverlayTopmost = value;
                    TopmostChangeCount++;
                });
        }

        public void Update(bool isEffectivelyVisible, bool ownerIsTopmost = false)
        {
            _controller.Update(isEffectivelyVisible, ownerIsTopmost);
        }
    }
}
