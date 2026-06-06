using System.Reflection;
using Moq;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class WatermarkPositionTests
{
    [Fact]
    public void WatermarkView_TranslatesTheControlInsteadOfItsClippedInnerBorder()
    {
        var view = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Noctra.Avalonia",
            "Views",
            "WatermarkView.axaml"));

        Assert.Contains("<UserControl.RenderTransform>", view, StringComparison.Ordinal);
        Assert.Contains(
            "<TranslateTransform X=\"{Binding TranslateX}\" Y=\"{Binding TranslateY}\"/>",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<Border.RenderTransform>", view, StringComparison.Ordinal);
    }

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
