using Moq;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;
using Noctra.Services;

namespace Noctra.Tests.Advertising;

public sealed class PreviewMobileAdvertisingServiceTests
{
    [Fact]
    public void Options_FallsBackToConservativeDefault()
    {
        var service = new PreviewMobileAdvertisingService(Mock.Of<ILicenseService>());

        Assert.Equal(AdvertisingOptions.ConservativeDefault, service.Options);
    }

    [Fact]
    public void SubscriptionChange_IsForwardedAsEligibilityChange()
    {
        var license = new Mock<ILicenseService>();
        var service = new PreviewMobileAdvertisingService(license.Object);

        var fired = false;
        service.EligibilityChanged += (_, _) => fired = true;

        license.Raise(l => l.SubscriptionChanged += null);

        Assert.True(fired);
    }
}