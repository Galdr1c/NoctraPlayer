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
            .Setup(service => service.PickM3uFileAsync(
                It.IsAny<IProgress<Noctra.Core.Models.FileCopyProgress>?>(),
                It.IsAny<CancellationToken>()))
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

    // ─────────────────────────────────────────────────────────────
    //  Computed property tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsRemoteProviderSource_FalseWhenLocalM3u()
    {
        var vm = CreateViewModel();

        vm.SetM3uFileSource("/tmp/test.m3u");

        Assert.False(vm.IsRemoteProviderSource);
        Assert.True(vm.IsLocalM3uFileSource);
    }

    [Fact]
    public void IsRemoteProviderSource_TrueWhenNotLocalM3u()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsRemoteProviderSource);
        Assert.False(vm.IsLocalM3uFileSource);
    }

    [Fact]
    public void ShowConnectionAnalysis_FalseWhenLocalM3u()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");

        Assert.False(vm.ShowConnectionAnalysis);
        Assert.True(vm.ShowLocalM3uFileActions);
    }

    [Fact]
    public void ShowConnectionAnalysis_TrueWhenRemoteProvider()
    {
        var vm = CreateViewModel();

        Assert.True(vm.ShowConnectionAnalysis);
        Assert.False(vm.ShowLocalM3uFileActions);
    }

    [Fact]
    public void CanSwitchProviderType_FalseWhenLocalM3u()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");

        Assert.False(vm.CanSwitchProviderType);
    }

    [Fact]
    public void CanSwitchProviderType_TrueWhenRemoteAndNotBusy()
    {
        var vm = CreateViewModel();

        Assert.True(vm.CanSwitchProviderType);
    }

    [Fact]
    public void CanSwitchProviderType_FalseWhenSaving()
    {
        var vm = CreateViewModel();
        vm.IsSaving = true;

        Assert.False(vm.CanSwitchProviderType);
    }

    [Fact]
    public void CanEditProviderUrl_FalseWhenLocalM3u()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");

        Assert.False(vm.CanEditProviderUrl);
    }

    [Fact]
    public void CanEditProviderUrl_TrueWhenRemoteAndNotBusy()
    {
        var vm = CreateViewModel();

        Assert.True(vm.CanEditProviderUrl);
    }

    [Fact]
    public void CanEditProviderUrl_FalseWhenAnalyzing()
    {
        var vm = CreateViewModel();
        vm.IsAnalyzingConnection = true;

        Assert.False(vm.CanEditProviderUrl);
    }

    // ─────────────────────────────────────────────────────────────
    //  SetLocalM3uFileSourceState tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetM3uFileSource_TriggersSourceModeNotifications()
    {
        var vm = CreateViewModel();
        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        vm.SetM3uFileSource("/tmp/test.m3u");

        Assert.Contains(nameof(AddProfileViewModel.IsLocalM3uFileSource), notified);
        Assert.Contains(nameof(AddProfileViewModel.IsRemoteProviderSource), notified);
        Assert.Contains(nameof(AddProfileViewModel.CanSwitchProviderType), notified);
        Assert.Contains(nameof(AddProfileViewModel.CanEditProviderUrl), notified);
        Assert.Contains(nameof(AddProfileViewModel.ShowConnectionAnalysis), notified);
        Assert.Contains(nameof(AddProfileViewModel.ShowLocalM3uFileActions), notified);
    }

    [Fact]
    public void SetM3uFileSource_SameValueStillNotifies()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");

        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        // Set same value again — should still fire NotifySourceModePropertiesChanged
        vm.SetM3uFileSource("/tmp/other.m3u");

        Assert.Contains(nameof(AddProfileViewModel.CanSwitchProviderType), notified);
    }

    // ─────────────────────────────────────────────────────────────
    //  ValidateLocalM3uFileAsync tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateLocalM3uFile_NoFileSelected_SetsError()
    {
        var vm = CreateViewModel();

        await vm.ValidateLocalM3uFileCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Contains("Profiles.Account.FileNotSelected", vm.StatusMessage);
    }

    [Fact]
    public async Task ValidateLocalM3uFile_EmptyUrl_SetsError()
    {
        var vm = CreateViewModel();
        // Simulate local mode with empty URL
        vm.SetM3uFileSource("/tmp/test.m3u");
        // Clear URL to simulate edge case
        vm.Url = string.Empty;

        await vm.ValidateLocalM3uFileCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task ValidateLocalM3uFile_ValidFile_CallsParser()
    {
        var localPath = Path.Combine(
            Path.GetTempPath(),
            "Noctra.Tests",
            Guid.NewGuid().ToString("N"),
            "validate-test.m3u");
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, "#EXTM3U");

        var parser = new Mock<IM3UParser>();
        parser
            .Setup(service => service.ParseFromFileAsync(localPath))
            .ReturnsAsync(new List<Channel>
            {
                new() { Name = "Test", StreamUrl = "https://example.test/stream.m3u8" }
            });

        var vm = CreateViewModel(parser: parser.Object);
        vm.SetM3uFileSource(localPath);

        await vm.ValidateLocalM3uFileCommand.ExecuteAsync(null);

        parser.Verify(service => service.ParseFromFileAsync(localPath), Times.Once);
        Assert.False(vm.HasError);
        Assert.Equal(ConnectionHealth.Good, vm.ConnectionHealth);

        Directory.Delete(Path.GetDirectoryName(localPath)!, recursive: true);
    }

    // ─────────────────────────────────────────────────────────────
    //  ChangeLocalM3uSourceTypeAsync tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeLocalM3uSourceType_Confirmed_ResetsFields()
    {
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(service => service.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var vm = CreateViewModel(dialogService: dialogService.Object);
        vm.SetM3uFileSource("/tmp/test.m3u");
        vm.Username = "testuser";
        vm.Password = "testpass";

        await vm.ChangeLocalM3uSourceTypeCommand.ExecuteAsync(null);

        Assert.False(vm.IsLocalM3uFileSource);
        Assert.True(vm.IsRemoteProviderSource);
        Assert.True(vm.IsM3U);
        Assert.True(string.IsNullOrEmpty(vm.Username));
        Assert.True(string.IsNullOrEmpty(vm.Password));
        dialogService.Verify(
            service => service.ShowConfirmationAsync(
                "Profiles.Account.ChangeSourceType.Title",
                "Profiles.Account.ChangeSourceType.Message"),
            Times.Once);
    }

    [Fact]
    public async Task ChangeLocalM3uSourceType_Cancelled_NoChange()
    {
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(service => service.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var vm = CreateViewModel(dialogService: dialogService.Object);
        vm.SetM3uFileSource("/tmp/test.m3u");

        await vm.ChangeLocalM3uSourceTypeCommand.ExecuteAsync(null);

        Assert.True(vm.IsLocalM3uFileSource);
        Assert.False(vm.IsRemoteProviderSource);
    }

    [Fact]
    public async Task ChangeLocalM3uSourceType_NotLocalM3u_DoesNothing()
    {
        var dialogService = new Mock<IDialogService>();
        var vm = CreateViewModel(dialogService: dialogService.Object);
        // Not in local M3U mode
        Assert.False(vm.IsLocalM3uFileSource);

        await vm.ChangeLocalM3uSourceTypeCommand.ExecuteAsync(null);

        // Dialog should not have been shown
        dialogService.Verify(
            service => service.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────
    //  Local M3U guard tests (provider type switching)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnIsXtreamChanged_LocalM3u_RevertsToM3U()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");
        Assert.True(vm.IsM3U);

        // Try to switch to Xtream — should be blocked
        vm.IsXtream = true;

        Assert.False(vm.IsXtream);
        Assert.True(vm.IsM3U);
        Assert.False(vm.IsStalker);
    }

    [Fact]
    public void OnIsStalkerChanged_LocalM3u_RevertsToM3U()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");
        Assert.True(vm.IsM3U);

        // Try to switch to Stalker — should be blocked
        vm.IsStalker = true;

        Assert.False(vm.IsStalker);
        Assert.True(vm.IsM3U);
        Assert.False(vm.IsXtream);
    }

    [Fact]
    public void OnIsM3UChanged_TurningOffLocalM3u_Reverts()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");
        Assert.True(vm.IsM3U);

        // Try to turn off M3U — should be blocked because local mode
        vm.IsM3U = false;

        Assert.True(vm.IsM3U);
        Assert.False(vm.IsXtream);
        Assert.False(vm.IsStalker);
    }

    [Fact]
    public void ProviderTypeSwitching_WorksWhenNotLocalM3u()
    {
        var vm = CreateViewModel();
        Assert.True(vm.IsRemoteProviderSource);

        // Should be able to switch freely
        vm.IsStalker = true;
        Assert.True(vm.IsStalker);

        vm.IsXtream = true;
        Assert.True(vm.IsXtream);

        vm.IsM3U = true;
        Assert.True(vm.IsM3U);
    }

    // ─────────────────────────────────────────────────────────────
    //  OnIsSavingChanged / OnIsAnalyzingConnectionChanged tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsSavingChanged_UpdatesCanSwitchProviderType()
    {
        var vm = CreateViewModel();
        Assert.True(vm.CanSwitchProviderType);

        vm.IsSaving = true;
        Assert.False(vm.CanSwitchProviderType);

        vm.IsSaving = false;
        Assert.True(vm.CanSwitchProviderType);
    }

    [Fact]
    public void IsAnalyzingConnectionChanged_UpdatesCanEditProviderUrl()
    {
        var vm = CreateViewModel();
        Assert.True(vm.CanEditProviderUrl);

        vm.IsAnalyzingConnection = true;
        Assert.False(vm.CanEditProviderUrl);

        vm.IsAnalyzingConnection = false;
        Assert.True(vm.CanEditProviderUrl);
    }

    // ─────────────────────────────────────────────────────────────
    //  Url change resets local M3U mode
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void UrlChange_RemoteUrl_ResetsLocalM3uMode()
    {
        var vm = CreateViewModel();
        vm.SetM3uFileSource("/tmp/test.m3u");
        Assert.True(vm.IsLocalM3uFileSource);

        // Simulate typing a remote URL
        vm.Url = "http://example.com/playlist.m3u";

        Assert.False(vm.IsLocalM3uFileSource);
        Assert.True(vm.IsRemoteProviderSource);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static AddProfileViewModel CreateViewModel(
        IM3UParser? parser = null,
        IDialogService? dialogService = null)
    {
        var avatarService = new Mock<IAvatarService>();
        avatarService
            .Setup(service => service.GetAvatarsByCategory())
            .Returns(new Dictionary<string, List<string>>
            {
                ["Default"] = new() { "default" }
            });
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.IsPremium).Returns(true);

        return new AddProfileViewModel(
            new Mock<IProfileService>().Object,
            new Mock<IDispatcherService>().Object,
            avatarService.Object,
            dialogService ?? new Mock<IDialogService>().Object,
            licenseService.Object,
            parser ?? new Mock<IM3UParser>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            new DesktopSecurityService(),
            CreateLocalizationService());
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
