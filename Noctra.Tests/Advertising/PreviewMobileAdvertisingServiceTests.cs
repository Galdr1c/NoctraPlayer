using System;
using Moq;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;
using Noctra.Services;

namespace Noctra.Tests.Advertising;

public sealed class PreviewMobileAdvertisingServiceTests
{
    [Fact]
    public void RemoteConfigChange_IsForwardedAsEligibilityChange()
    {
        var remoteConfig = new Mock<IRemoteAdvertisingConfigService>();
        var service = new PreviewMobileAdvertisingService(
            Mock.Of<ILicenseService>(),
            remoteConfig.Object);

        var fired = false;
        service.EligibilityChanged += (_, _) => fired = true;

        remoteConfig.Raise(r => r.OptionsChanged += null, EventArgs.Empty);

        Assert.True(fired);
    }

    [Fact]
    public void Options_FollowsRemoteConfigCurrentOptions()
    {
        var remoteOptions = new AdvertisingOptions
        {
            Movies = new NativeAdPlacementOptions(true, 30, 3)
        };
        var remoteConfig = new Mock<IRemoteAdvertisingConfigService>();
        remoteConfig.SetupGet(r => r.CurrentOptions).Returns(remoteOptions);

        var service = new PreviewMobileAdvertisingService(
            Mock.Of<ILicenseService>(),
            remoteConfig.Object);

        Assert.Same(remoteOptions, service.Options);
    }

    [Fact]
    public void WithoutRemoteConfig_FallsBackToConservativeDefault()
    {
        var service = new PreviewMobileAdvertisingService(Mock.Of<ILicenseService>());

        Assert.Equal(AdvertisingOptions.ConservativeDefault, service.Options);
    }
}