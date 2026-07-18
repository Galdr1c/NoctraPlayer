using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MainViewModelStalkerFirstImportTests
{
    [Fact]
    public async Task FreshImport_AggregatesFirstCompletedSeriesCategory_BeforeProviderFinishes()
    {
        var playlist = new Playlist { Id = 77, Name = "Fresh Stalker" };
        var playlistCreated = false;
        var releaseProvider = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var providerFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var earlyAggregationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var playlistService = new Mock<IPlaylistService>();
        playlistService
            .Setup(service => service.GetAllAsync(It.IsAny<int?>()))
            .ReturnsAsync(() => playlistCreated ? [playlist] : []);
        playlistService
            .Setup(service => service.CreateEmptyPlaylistAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => playlistCreated = true)
            .ReturnsAsync(playlist);
        playlistService
            .Setup(service => service.AppendChannelsAsync(
                playlist.Id,
                It.IsAny<IReadOnlyCollection<Channel>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        playlistService
            .Setup(service => service.ReplaceDummyWithRealChannelsAsync(
                playlist.Id,
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<Channel>>(),
                true,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<ChannelType?>()))
            .Returns(Task.CompletedTask);
        playlistService
            .Setup(service => service.DeleteAllDummiesAsync(
                playlist.Id,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var stalker = new Mock<IStalkerPortalService>();
        stalker.Setup(service => service.GetEpgUrl(It.IsAny<string>())).Returns(string.Empty);
        stalker
            .Setup(service => service.GetChannelsProgressiveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                true,
                It.IsAny<Func<List<StalkerCategory>, Action<string>, Task<List<StalkerCategory>>>>(),
                It.IsAny<Func<List<Channel>, StalkerCategory, Task>>(),
                It.IsAny<IProgress<StalkerLoadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string _,
                string _,
                bool _,
                Func<List<StalkerCategory>, Action<string>, Task<List<StalkerCategory>>> onCategoriesDiscovered,
                Func<List<Channel>, StalkerCategory, Task> onCategoryLoaded,
                IProgress<StalkerLoadProgress>? _,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var category = new StalkerCategory
                    {
                        Id = "series-1",
                        Name = "First Series",
                        Type = "series"
                    };
                    await onCategoriesDiscovered([category], _ => { });
                    await onCategoryLoaded([
                        new Channel
                        {
                            PlaylistId = playlist.Id,
                            Name = "S01E01",
                            StreamUrl = "stalker://series/1",
                            GroupTitle = category.Name,
                            Type = ChannelType.Series
                        }
                    ], category);
                    await releaseProvider.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    providerFinished.TrySetResult();
                }
            });

        var mediaService = new Mock<IMediaService>();
        mediaService
            .Setup(service => service.AggregateContentAsync(
                playlist.Id,
                It.IsAny<CancellationToken>()))
            .Callback(() => earlyAggregationStarted.TrySetResult())
            .Returns(Task.CompletedTask);

        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(
                playlist.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                1,
                new List<string> { "First Series" },
                new List<string>(),
                new List<string>(),
                new List<string> { "First Series" }));
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Channel>());
        contentQuery
            .Setup(service => service.GetSeriesListAsync(
                playlist.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>());

        var viewModel = CreateViewModel(
            playlistService.Object,
            stalker.Object,
            mediaService.Object,
            contentQuery.Object);
        var profile = new Profile
        {
            Id = 9,
            Name = "Fresh Stalker",
            ProviderAccount = new ProviderAccount
            {
                Type = ProfileType.StalkerPortal,
                Url = "http://portal.test",
                Username = "00:11:22:33:44:55"
            }
        };

        await viewModel.LoadProfileAsync(profile).WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await earlyAggregationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            mediaService.Verify(
                service => service.AggregateContentAsync(
                    playlist.Id,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.False(providerFinished.Task.IsCompleted);
        }
        finally
        {
            releaseProvider.TrySetResult();
            await providerFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static MainViewModel CreateViewModel(
        IPlaylistService playlistService,
        IStalkerPortalService stalkerPortalService,
        IMediaService mediaService,
        IContentQueryService contentQueryService)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Settings).Returns(new AppSettings());
        settings.Setup(service => service.LoadProfileSettingsAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        settings.Setup(service => service.SaveAsync()).Returns(Task.CompletedTask);

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
            settings.Object,
            new Mock<IContentDownloadService>().Object,
            new Mock<IMetadataService>().Object,
            dispatcher.Object,
            new Mock<IDialogService>().Object,
            null!,
            new Mock<IChannelService>().Object,
            mediaService,
            new Mock<IEpgService>().Object,
            playlistService,
            new Mock<IWatchHistoryService>().Object,
            new Mock<IXtreamCodesService>().Object,
            stalkerPortalService,
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
            null,
            contentQueryService);
    }
}
