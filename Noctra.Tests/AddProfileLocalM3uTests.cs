using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class AddProfileLocalM3uTests
{
    [Fact]
    public async Task SaveCommand_LocalM3uFile_PreservesLocalPath()
    {
        var localPath = Path.Combine(
            Path.GetTempPath(),
            "Noctra.Tests",
            Guid.NewGuid().ToString("N"),
            "selected-playlist.m3u");
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, "#EXTM3U");
        ProfileSaveRequest? capturedRequest = null;
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(service => service.SaveProfileAsync(It.IsAny<ProfileSaveRequest>()))
            .Callback<ProfileSaveRequest>(request => capturedRequest = request)
            .ReturnsAsync(new Profile { Name = "Local playlist" });
        var avatarService = new Mock<IAvatarService>();
        avatarService
            .Setup(service => service.GetAvatarsByCategory())
            .Returns(new Dictionary<string, List<string>>
            {
                ["Default"] = new() { "default" }
            });
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.IsPremium).Returns(true);
        licenseService
            .Setup(service => service.IsWithinLimit(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(true);

        var viewModel = new AddProfileViewModel(
            profileService.Object,
            new Mock<IDispatcherService>().Object,
            avatarService.Object,
            new Mock<IDialogService>().Object,
            licenseService.Object,
            new Mock<IM3UParser>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            new DesktopSecurityService(),
            CreateLocalizationService());
        viewModel.ProfileName = "Local playlist";

        try
        {
            viewModel.SetM3uFileSource(localPath);
            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.NotNull(capturedRequest);
            Assert.Equal(ProfileType.M3U, capturedRequest.AccountType);
            Assert.Equal(localPath, capturedRequest.Url);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(localPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeConnection_LocalM3uFile_UsesFileParser()
    {
        var localPath = Path.Combine(
            Path.GetTempPath(),
            "Noctra.Tests",
            Guid.NewGuid().ToString("N"),
            "selected-playlist.m3u");
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, "#EXTM3U");
        var parser = new Mock<IM3UParser>();
        parser
            .Setup(service => service.ParseFromFileAsync(localPath))
            .ReturnsAsync(new List<Channel>
            {
                new() { Name = "Test channel", StreamUrl = "https://example.test/live.m3u8" }
            });
        var avatarService = new Mock<IAvatarService>();
        avatarService
            .Setup(service => service.GetAvatarsByCategory())
            .Returns(new Dictionary<string, List<string>>
            {
                ["Default"] = new() { "default" }
            });
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.IsPremium).Returns(true);

        var viewModel = new AddProfileViewModel(
            new Mock<IProfileService>().Object,
            new Mock<IDispatcherService>().Object,
            avatarService.Object,
            new Mock<IDialogService>().Object,
            licenseService.Object,
            parser.Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            new DesktopSecurityService(),
            CreateLocalizationService());

        try
        {
            viewModel.SetM3uFileSource(localPath);
            await viewModel.AnalyzeConnectionCommand.ExecuteAsync(null);

            parser.Verify(
                service => service.ParseFromFileAsync(localPath),
                Times.Once);
            Assert.False(viewModel.HasError);
            Assert.Equal(ConnectionHealth.Good, viewModel.ConnectionHealth);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(localPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task PickM3uFileCommand_SelectedFile_ConfiguresLocalM3uSource()
    {
        var selectedPath = Path.Combine(
            Path.GetTempPath(),
            "Noctra.Tests",
            "picked-playlist.m3u");
        var filePicker = new Mock<IPlaylistFilePickerService>();
        filePicker
            .Setup(service => service.PickM3uFileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(selectedPath);
        var avatarService = new Mock<IAvatarService>();
        avatarService
            .Setup(service => service.GetAvatarsByCategory())
            .Returns(new Dictionary<string, List<string>>
            {
                ["Default"] = new() { "default" }
            });
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.IsPremium).Returns(true);

        var viewModel = new AddProfileViewModel(
            new Mock<IProfileService>().Object,
            new Mock<IDispatcherService>().Object,
            avatarService.Object,
            new Mock<IDialogService>().Object,
            licenseService.Object,
            new Mock<IM3UParser>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            new DesktopSecurityService(),
            CreateLocalizationService(),
            filePicker.Object);

        await viewModel.PickM3uFileCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsM3U);
        Assert.True(viewModel.IsLocalM3uFileSource);
        Assert.Equal(selectedPath, viewModel.Url);
    }

    [Fact]
    public void InitializeForEdit_LocalM3uFile_RestoresLocalSourceMode()
    {
        var localPath = Path.Combine(
            Path.GetTempPath(),
            "Noctra.Tests",
            "existing-playlist.m3u");
        var avatarService = new Mock<IAvatarService>();
        avatarService
            .Setup(service => service.GetAvatarsByCategory())
            .Returns(new Dictionary<string, List<string>>
            {
                ["Default"] = new() { "default" }
            });
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.IsPremium).Returns(true);
        var viewModel = new AddProfileViewModel(
            new Mock<IProfileService>().Object,
            new Mock<IDispatcherService>().Object,
            avatarService.Object,
            new Mock<IDialogService>().Object,
            licenseService.Object,
            new Mock<IM3UParser>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            new DesktopSecurityService(),
            CreateLocalizationService());
        var profile = new Profile
        {
            Name = "Existing local playlist",
            ProviderAccount = new ProviderAccount
            {
                Type = ProfileType.M3U,
                Url = localPath
            }
        };

        viewModel.InitializeForEdit(profile);

        Assert.True(viewModel.IsM3U);
        Assert.True(viewModel.IsLocalM3uFileSource);
        Assert.Equal(localPath, viewModel.Url);
    }

    private static ILocalizationService CreateLocalizationService()
    {
        var localization = new Mock<ILocalizationService>();
        localization
            .Setup(service => service.GetString(It.IsAny<string>()))
            .Returns((string key) => key);
        return localization.Object;
    }
}
