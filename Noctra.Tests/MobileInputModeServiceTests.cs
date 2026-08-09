using Noctra.Mobile.Services;

namespace Noctra.Tests;

public sealed class MobileInputModeServiceTests
{
    [Fact]
    public void SetMode_PublishesOnlyRealInputModeChanges()
    {
        var service = new MobileInputModeService();
        var observedModes = new List<MobileInputMode>();
        service.ModeChanged += (_, mode) => observedModes.Add(mode);

        Assert.Equal(MobileInputMode.Touch, service.CurrentMode);

        service.SetMode(MobileInputMode.Remote);
        service.SetMode(MobileInputMode.Remote);
        service.SetMode(MobileInputMode.Touch);

        Assert.Equal(MobileInputMode.Touch, service.CurrentMode);
        Assert.Equal(
            [MobileInputMode.Remote, MobileInputMode.Touch],
            observedModes);
    }
}
