using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public class MainViewModelImportJobStatusTests
{
    private readonly Mock<IImportJobService> _importJobService = new();

    [Fact]
    public async Task RefreshActiveImportJobStatusAsync_ActiveJob_UpdatesObservableState()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProfileId = 42;

        _importJobService
            .Setup(service => service.GetActiveForProfileAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportJob
            {
                Id = 7,
                ProfileId = 42,
                Kind = ImportJobKind.M3U,
                Status = ImportJobStatus.Running,
                SourceName = "M3U",
                Stage = "Parsing playlist",
                LiveCount = 120,
                VodCount = 34,
                SeriesCount = 5,
                FailedCategoryCount = 2
            });

        await viewModel.RefreshActiveImportJobStatusAsync();

        Assert.True(viewModel.HasActiveImportJob);
        Assert.Equal("Parsing playlist", viewModel.ActiveImportJobStage);
        Assert.Equal(120, viewModel.ActiveImportJobLiveCount);
        Assert.Equal(34, viewModel.ActiveImportJobVodCount);
        Assert.Equal(5, viewModel.ActiveImportJobSeriesCount);
        Assert.Equal(2, viewModel.ActiveImportJobFailedCategoryCount);
    }

    [Fact]
    public async Task RefreshActiveImportJobStatusAsync_NoActiveJob_ClearsObservableState()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProfileId = 42;
        viewModel.HasActiveImportJob = true;
        viewModel.ActiveImportJobStage = "Importing";
        viewModel.ActiveImportJobLiveCount = 1;
        viewModel.ActiveImportJobVodCount = 2;
        viewModel.ActiveImportJobSeriesCount = 3;
        viewModel.ActiveImportJobFailedCategoryCount = 4;

        _importJobService
            .Setup(service => service.GetActiveForProfileAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportJob?)null);

        await viewModel.RefreshActiveImportJobStatusAsync();

        Assert.False(viewModel.HasActiveImportJob);
        Assert.Equal(string.Empty, viewModel.ActiveImportJobStage);
        Assert.Equal(0, viewModel.ActiveImportJobLiveCount);
        Assert.Equal(0, viewModel.ActiveImportJobVodCount);
        Assert.Equal(0, viewModel.ActiveImportJobSeriesCount);
        Assert.Equal(0, viewModel.ActiveImportJobFailedCategoryCount);
    }

    [Fact]
    public async Task RefreshActiveImportJobStatusAsync_NoCurrentProfile_DoesNotQueryService()
    {
        var viewModel = CreateViewModel();

        await viewModel.RefreshActiveImportJobStatusAsync();

        Assert.False(viewModel.HasActiveImportJob);
        _importJobService.Verify(
            service => service.GetActiveForProfileAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private MainViewModel CreateViewModel()
    {
        var settings = new AppSettings();
        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(service => service.Settings).Returns(settings);

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
            settingsService.Object,
            new Mock<IContentDownloadService>().Object,
            new Mock<IMetadataService>().Object,
            dispatcher.Object,
            new Mock<IDialogService>().Object,
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
            new Mock<ILicenseService>().Object,
            new Mock<IUpdateService>().Object,
            localization.Object,
            new Mock<ILogger<MainViewModel>>().Object,
            null,
            null,
            _importJobService.Object);
    }
}
