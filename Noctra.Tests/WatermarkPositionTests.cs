using System.Reflection;
using Moq;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class WatermarkPositionTests
{
    [Fact]
    public void ShiftPosition_NeverMovesBottomRightWatermarkOutsidePlayerBounds()
    {
        var licenseService = new Mock<ILicenseService>();
        licenseService
            .Setup(service => service.IsFeatureAvailable(LicenseService.Features.AdFree))
            .Returns(false);

        var dispatcherService = new Mock<IDispatcherService>();
        dispatcherService
            .Setup(service => service.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        using var viewModel = new WatermarkViewModel(
            licenseService.Object,
            dispatcherService.Object);

        var shiftPosition = typeof(WatermarkViewModel).GetMethod(
            "ShiftPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(shiftPosition);

        for (var iteration = 0; iteration < 200; iteration++)
        {
            shiftPosition.Invoke(viewModel, null);

            Assert.InRange(viewModel.TranslateX, -20, 0);
            Assert.InRange(viewModel.TranslateY, -20, 0);
        }
    }
}
