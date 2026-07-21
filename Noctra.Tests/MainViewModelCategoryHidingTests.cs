using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public class MainViewModelCategoryHidingTests
{
    private readonly AppSettings _settings = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<ILicenseService> _licenseService = new();

    public MainViewModelCategoryHidingTests()
    {
        _settingsService.SetupGet(service => service.Settings).Returns(_settings);
        _settingsService.Setup(service => service.SaveAsync()).Returns(Task.CompletedTask);
        _dialogService.Setup(service => service.ShowUpsellAsync()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HideGroupCommand_FreeUser_ShowsUpsellWithoutChangingCategoryState()
    {
        const string groupName = "News";
        _licenseService.SetupGet(service => service.IsPremium).Returns(false);
        var viewModel = CreateViewModel();
        viewModel.SelectedChannelType = ChannelType.Live;
        viewModel.Groups.Add(groupName);
        viewModel.SelectedGroup = groupName;
        var originalStatus = viewModel.StatusMessage;

        viewModel.HideGroupCommand.Execute(groupName);
        await WaitForAsync(() => _dialogService.Invocations.Count > 0);

        _dialogService.Verify(service => service.ShowUpsellAsync(), Times.Once);
        Assert.Empty(_settings.HiddenLiveGroups);
        Assert.Contains(groupName, viewModel.Groups);
        Assert.Equal(groupName, viewModel.SelectedGroup);
        Assert.Equal(originalStatus, viewModel.StatusMessage);
        _settingsService.Verify(service => service.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task HideGroupCommand_PremiumUser_HidesCategoryAndPersistsSettings()
    {
        const string groupName = "Movies";
        _licenseService.SetupGet(service => service.IsPremium).Returns(true);
        var viewModel = CreateViewModel();
        viewModel.SelectedChannelType = ChannelType.VOD;
        viewModel.Groups.Add(groupName);

        viewModel.HideGroupCommand.Execute(groupName);
        await WaitForAsync(() => _settingsService.Invocations.Any(
            invocation => invocation.Method.Name == nameof(ISettingsService.SaveAsync)));

        Assert.Contains(groupName, _settings.HiddenMovieGroups);
        Assert.DoesNotContain(groupName, viewModel.Groups);
        _dialogService.Verify(service => service.ShowUpsellAsync(), Times.Never);
        _settingsService.Verify(service => service.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task HideGroupCommand_BlankGroup_DoesNothingWithoutShowingUpsell()
    {
        _licenseService.SetupGet(service => service.IsPremium).Returns(false);
        var viewModel = CreateViewModel();

        viewModel.HideGroupCommand.Execute(" ");
        await Task.Delay(50);

        _dialogService.Verify(service => service.ShowUpsellAsync(), Times.Never);
        Assert.Empty(_settings.HiddenLiveGroups);
        Assert.Empty(_settings.HiddenMovieGroups);
        Assert.Empty(_settings.HiddenSeriesGroups);
        _settingsService.Verify(service => service.SaveAsync(), Times.Never);
    }

    private MainViewModel CreateViewModel()
    {
        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(service => service.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        dispatcher.Setup(service => service.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        dispatcher.Setup(service => service.InvokeAsync(It.IsAny<Func<Task>>()))
            .Returns((Func<Task> action) => action());

        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service.GetString(It.IsAny<string>())).Returns("Test");

        return new MainViewModel(
            _settingsService.Object,
            new Mock<IContentDownloadService>().Object,
            new Mock<IMetadataService>().Object,
            dispatcher.Object,
            _dialogService.Object,
            null!,
            new Mock<IChannelService>().Object,
            new Mock<IMediaService>().Object,
            new Mock<IEpgService>().Object,
            new Mock<IPlaylistService>().Object,
            new Mock<IWatchHistoryService>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            null!,
            null!,
            new Mock<IDbContextFactory<AppDbContext>>().Object,
            new Mock<ISecurityService>().Object,
            new Mock<ITmdbSyncService>().Object,
            _licenseService.Object,
            new Mock<IAppVersionService>().Object,
            localization.Object,
            new Mock<ILogger<MainViewModel>>().Object);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Expected asynchronous operation did not complete.");
            }

            await Task.Delay(10);
        }
    }
}
