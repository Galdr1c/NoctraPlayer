using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MainViewModelIncrementalCancellationTests
{
    [Fact]
    public async Task DelayedReload_ForOldPlaylist_DoesNotCancelNewPlaylistLoad()
    {
        var newMetadataStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var newMetadata = new TaskCompletionSource<(int, List<string>, List<string>, List<string>, List<string>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                0,
                new List<string> { "Old group" },
                new List<string> { "Old group" },
                new List<string>(),
                new List<string>()));
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(2, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken token) =>
            {
                newMetadataStarted.TrySetResult(token);
                token.Register(() => newMetadata.TrySetCanceled(token));
                return newMetadata.Task;
            });
        contentQuery
            .Setup(service => service.GetSeriesListAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>());
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Channel>());

        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Live;
        viewModel.SelectedChannelType = ChannelType.Live;
        viewModel.SelectedPlaylist = new Playlist { Id = 1, Name = "Old" };
        await WaitForAsync(() => viewModel.Groups.Contains("Old group"));
        await Task.Delay(600); // Let the initial selection/filter coalescing settle.

        var throttledReload = (Task)typeof(MainViewModel)
            .GetMethod("ThrottledLoadChannelsAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(viewModel, new object[] { 1 })!;
        viewModel.SelectedPlaylist = new Playlist { Id = 2, Name = "New" };
        var newToken = await newMetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await throttledReload;

        Assert.False(newToken.IsCancellationRequested);
        newMetadata.SetResult((
            0,
            new List<string> { "New group" },
            new List<string> { "New group" },
            new List<string>(),
            new List<string>()));
        await WaitForAsync(() => viewModel.Groups.Contains("New group"));
    }

    [Fact]
    public async Task ChangingPlaylist_CancelsOldMetadataAndOnlyAppliesNewPage()
    {
        var oldMetadataStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var oldMetadata = new TaskCompletionSource<(int, List<string>, List<string>, List<string>, List<string>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(1, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken token) =>
            {
                oldMetadataStarted.TrySetResult(token);
                token.Register(() => oldMetadata.TrySetCanceled(token));
                return oldMetadata.Task;
            });
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                1,
                new List<string> { "New group" },
                new List<string> { "New group" },
                new List<string>(),
                new List<string>()));
        contentQuery
            .Setup(service => service.GetSeriesListAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>());
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((ContentPageRequest request, CancellationToken _) => Task.FromResult(
                request.PlaylistId == 2 && request.Type != ChannelType.Series
                    ? new List<Channel>
                    {
                        new()
                        {
                            Name = "New playlist channel",
                            PlaylistId = 2,
                            StreamUrl = "https://stream.test/new",
                            Type = ChannelType.Live
                        }
                    }
                    : new List<Channel>()));

        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Live;
        viewModel.SelectedChannelType = ChannelType.Live;
        viewModel.SelectedPlaylist = new Playlist { Id = 1, Name = "Old" };
        var oldToken = await oldMetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.SelectedPlaylist = new Playlist { Id = 2, Name = "New" };
        await WaitForAsync(() => viewModel.FilteredChannels.Any(channel => channel.PlaylistId == 2));

        Assert.True(oldToken.IsCancellationRequested);
        Assert.All(viewModel.FilteredChannels, channel => Assert.Equal(2, channel.PlaylistId));
        Assert.Contains("New group", viewModel.Groups);
    }

    private static MainViewModel CreateViewModel(IContentQueryService contentQueryService)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Settings).Returns(new AppSettings());
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
            null,
            contentQueryService);
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
