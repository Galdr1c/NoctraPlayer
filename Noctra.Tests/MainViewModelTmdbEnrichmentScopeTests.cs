using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MainViewModelTmdbEnrichmentScopeTests
{
    [Fact]
    public async Task NavigatingAway_CancelsStartedMovieEnrichmentBeforeCommit()
    {
        await using var scheduler = CreateScheduler();
        var metadata = CreateBlockingMovieMetadata(out var started, out var cancelled);
        var viewModel = CreateViewModel(metadata.Object, scheduler);
        viewModel.CurrentProfile = CreateM3uProfile();
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Movies" };
        viewModel.ActiveView = AppView.Movies;
        var channel = CreateMovie(701, 7);

        QueueMovieEnrichment(viewModel, channel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.ActiveView = AppView.Series;

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(channel.LogoUrl);
        Assert.Null(channel.TmdbId);
        Assert.Null(channel.LastTmdbSync);
    }

    [Fact]
    public async Task SwitchingPlaylist_CancelsStartedMovieEnrichmentFromOldPlaylist()
    {
        await using var scheduler = CreateScheduler();
        var metadata = CreateBlockingMovieMetadata(out var started, out var cancelled);
        var viewModel = CreateViewModel(metadata.Object, scheduler);
        viewModel.CurrentProfile = CreateM3uProfile();
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "First" };
        viewModel.ActiveView = AppView.Movies;
        var channel = CreateMovie(702, 7);

        QueueMovieEnrichment(viewModel, channel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.SelectedPlaylist = new Playlist { Id = 8, Name = "Second" };

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(channel.LogoUrl);
        Assert.Null(channel.TmdbId);
        Assert.Null(channel.LastTmdbSync);
    }

    [Fact]
    public async Task DelayedOldPlaylistPage_IsRejectedBeforeSchedulingMetadata()
    {
        await using var scheduler = CreateScheduler();
        var metadata = new Mock<IMetadataService>();
        metadata
            .Setup(service => service.FetchMetadataAsync(
                It.IsAny<string>(),
                ChannelType.VOD,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMetadata
            {
                TmdbId = 101,
                PosterUrl = "https://image.test/stale-poster.jpg"
            });
        var viewModel = CreateViewModel(metadata.Object, scheduler);
        viewModel.CurrentProfile = CreateM3uProfile();
        viewModel.SelectedPlaylist = new Playlist { Id = 8, Name = "Current" };
        viewModel.ActiveView = AppView.Movies;

        QueueMovieEnrichment(viewModel, CreateMovie(703, playlistId: 7));
        await Task.Delay(100);

        metadata.Verify(service => service.FetchMetadataAsync(
            It.IsAny<string>(),
            ChannelType.VOD,
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static TmdbEnrichmentScheduler CreateScheduler()
        => new(new TmdbEnrichmentSchedulerOptions(
            PendingCapacity: 8,
            MaxConcurrency: 1,
            InterRequestDelay: TimeSpan.Zero));

    private static Mock<IMetadataService> CreateBlockingMovieMetadata(
        out TaskCompletionSource started,
        out TaskCompletionSource cancelled)
    {
        started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedSignal = started;
        var cancelledSignal = cancelled;
        var metadata = new Mock<IMetadataService>();
        metadata
            .Setup(service => service.FetchMetadataAsync(
                It.IsAny<string>(),
                ChannelType.VOD,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, ChannelType? _, string? _, CancellationToken token) =>
            {
                using var registration = token.Register(() => cancelledSignal.TrySetResult());
                startedSignal.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ChannelMetadata
                {
                    TmdbId = 99,
                    PosterUrl = "https://image.test/poster.jpg"
                };
            });
        return metadata;
    }

    private static void QueueMovieEnrichment(MainViewModel viewModel, Channel channel)
    {
        var method = typeof(MainViewModel).GetMethod(
            "QueueVisibleChannelVisualEnrichment",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, [new List<Channel> { channel }]);
    }

    private static Channel CreateMovie(int id, int playlistId)
        => new()
        {
            Id = id,
            PlaylistId = playlistId,
            Name = $"Movie {id}",
            StreamUrl = $"https://stream.test/{id}",
            Type = ChannelType.VOD
        };

    private static Profile CreateM3uProfile()
        => new()
        {
            Id = 1,
            Name = "M3U profile",
            ProviderAccount = new ProviderAccount
            {
                Id = 1,
                Name = "M3U account",
                Type = ProfileType.M3U,
                Url = "https://playlist.test/list.m3u"
            }
        };

    private static MainViewModel CreateViewModel(
        IMetadataService metadataService,
        ITmdbEnrichmentScheduler scheduler)
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

        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Channel>());
        contentQuery
            .Setup(service => service.GetSeriesListAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>());
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, new List<string>(), new List<string>(), new List<string>(), new List<string>()));

        return new MainViewModel(
            settings.Object,
            new Mock<IContentDownloadService>().Object,
            metadataService,
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
            new Mock<IAppVersionService>().Object,
            localization.Object,
            new Mock<ILogger<MainViewModel>>().Object,
            contentQueryService: contentQuery.Object,
            tmdbEnrichmentScheduler: scheduler);
    }
}
