using Noctra.Mobile.Services;
using Avalonia.Controls;
using Noctra.Mobile.Controls;

namespace Noctra.Tests;

public sealed class MobileImageLoadPolicyTests
{
    [Fact]
    public void InactiveSurface_IsInheritedByImageCreatedAfterDeactivation()
    {
        var surface = new Grid();
        RemoteImage.SetDescendantLoadsActive(surface, false);

        var image = new RemoteImage();
        surface.Children.Add(image);

        Assert.False(image.GetValue(RemoteImage.SurfaceLoadsActiveProperty));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void CanStart_RequiresEveryPersistentAndVisualCondition(
        bool isForeground,
        bool isSurfaceActive,
        bool isAttached,
        bool isVisible,
        bool expected)
    {
        Assert.Equal(
            expected,
            MobileImageLoadPolicy.CanStart(
                isForeground,
                isSurfaceActive,
                isAttached,
                isVisible));
    }
}
