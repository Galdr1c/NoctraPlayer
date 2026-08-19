using System.Threading;
using System.Threading.Tasks;
using Moq;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;

namespace Noctra.Tests.Advertising;

public sealed class MobileAdvertisingBootstrapperTests
{
    [Fact]
    public async Task StartAsync_InitializesProvider()
    {
        var provider = new Mock<IMobileAdvertisingService>();

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object);

        await bootstrapper.StartAsync();

        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_InitializesEvenWhenNotEligible()
    {
        // Regression pin for the entitlement decoupling: UMP consent info must
        // still be refreshed on every launch for premium / ad-less
        // configurations so the privacy-options entry point stays accurate;
        // ad creation is the provider's internal decision.
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.IsAdsEligible).Returns(false);

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
        // consent flow, so initialization must never depend on it.
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.IsAdsEligible).Returns(true);
        provider.Setup(p => p.CanServeAds).Returns(false);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object);

        await bootstrapper.StartAsync();

        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ProviderFailure_DoesNotThrow()
    {
        var provider = new Mock<IMobileAdvertisingService>();
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