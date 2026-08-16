using System.Threading;
using System.Threading.Tasks;
using Moq;
using Noctra.Core.Advertising;
using Noctra.Mobile.Services;

namespace Noctra.Tests.Advertising;

public sealed class MobileAdvertisingBootstrapperTests
{
    [Fact]
    public async Task StartAsync_RefreshesConfig_AndInitializesProvider_WhenEntitled()
    {
        var remoteConfig = new Mock<IRemoteAdvertisingConfigService>();
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.CanServeAds).Returns(true);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object, remoteConfig.Object);

        await bootstrapper.StartAsync();

        remoteConfig.Verify(r => r.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_RefreshesConfig_ButSkipsProviderInit_WhenNotEntitled()
    {
        var remoteConfig = new Mock<IRemoteAdvertisingConfigService>();
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.CanServeAds).Returns(false);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object, remoteConfig.Object);

        await bootstrapper.StartAsync();

        remoteConfig.Verify(r => r.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ConfigFailure_DoesNotThrow_AndSkipsProviderInit()
    {
        var remoteConfig = new Mock<IRemoteAdvertisingConfigService>();
        remoteConfig.Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("unreachable"));
        var provider = new Mock<IMobileAdvertisingService>();
        provider.Setup(p => p.CanServeAds).Returns(false);

        var bootstrapper = new MobileAdvertisingBootstrapper(provider.Object, remoteConfig.Object);

        await bootstrapper.StartAsync();

        provider.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void NoOpProvider_NeverServesAds_AndInitializesWithoutWork()
    {
        var provider = new NoOpMobileAdvertisingService();

        Assert.False(provider.CanServeAds);
        Assert.Equal(AdvertisingOptions.ConservativeDefault, provider.Options);
        Assert.Equal(Task.CompletedTask, provider.InitializeAsync());
    }
}