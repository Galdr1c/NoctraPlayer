using System.Threading;
using System.Threading.Tasks;
using Moq;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;

namespace Noctra.Tests.Advertising;

public sealed class MobileAdvertisingBootstrapperTests
{
    [Fact]
    public async Task StartAsync_InitializesProvider_WhenEligible()
    {
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.IsAdsEligible).Returns(true);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object);

        await bootstrapper.StartAsync();

        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_InitializesEvenWhenConsentNotYetGranted()
    {
        // Regression pin for the UMP deadlock: CanServeAds implies UMP consent +
        // SDK initialization, and CanRequestAds stays false until the consent
        // flow runs. Gating initialization on CanServeAds would deadlock the
        // consent flow, so initialization must be gated on IsAdsEligible only.
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.IsAdsEligible).Returns(true);
        provider.Setup(p => p.CanServeAds).Returns(false);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object);

        await bootstrapper.StartAsync();

        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_SkipsProviderInit_WhenNotEligible()
    {
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.IsAdsEligible).Returns(false);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object);

        await bootstrapper.StartAsync();

        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ProviderFailure_DoesNotThrow()
    {
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.IsAdsEligible).Returns(true);
        provider.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("unreachable"));

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object);

        await bootstrapper.StartAsync();
    }

    [Fact]
    public void NoOpProvider_NeverServesAds_AndInitializesWithoutWork()
    {
        var provider = new NoOpMobileAdvertisingService();

        Assert.False(provider.IsAdsEligible);
        Assert.False(provider.CanServeAds);
        Assert.Equal(AdvertisingOptions.ConservativeDefault, provider.Options);
        Assert.Equal(Task.CompletedTask, provider.InitializeAsync());
    }
}