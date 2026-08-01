using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MainViewModelPersonalStateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public MainViewModelPersonalStateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task ToggleFavoriteAsync_SerializesRapidClicksAndPersistsEachCapturedState()
    {
        var firstCallStarted = NewSignal();
        var releaseFirstCall = NewSignal();
        var observedStates = new ConcurrentQueue<bool>();
        var updateCount = 0;
        var channelService = new Mock<IChannelService>();
        channelService
            .Setup(service => service.UpdateChannelAsync(
                It.IsAny<Channel>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Channel channel, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref updateCount) == 1)
                {
                    firstCallStarted.TrySetResult(true);
                    await releaseFirstCall.Task;
                }

                observedStates.Enqueue(channel.IsFavorite);
            });

        var viewModel = CreateViewModel(channelService: channelService);
        var channel = new Channel
        {
            Id = 11,
            PlaylistId = 7,
            Name = "Movie",
            StreamUrl = "https://stream.test/movie",
            Type = ChannelType.VOD,
            IsFavorite = false
        };

        var first = viewModel.ToggleFavoriteCommand.ExecuteAsync(channel);
        await firstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = viewModel.ToggleFavoriteCommand.ExecuteAsync(channel);
        await Task.Delay(50);

        // The second optimistic flip must wait for the first persistence operation.
        Assert.True(channel.IsFavorite);

        releaseFirstCall.TrySetResult(true);
        await Task.WhenAll(first, second);

        Assert.Equal(new[] { true, false }, observedStates);
        Assert.False(channel.IsFavorite);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_RollsBackOptimisticStateWhenPersistenceFails()
    {
        var channelService = new Mock<IChannelService>();
        channelService
            .Setup(service => service.UpdateChannelAsync(
                It.IsAny<Channel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write failed"));

        var viewModel = CreateViewModel(channelService: channelService);
        var channel = new Channel
        {
            Id = 12,
            PlaylistId = 7,
            Name = "Movie",
            StreamUrl = "https://stream.test/movie-2",
            Type = ChannelType.VOD,
            IsFavorite = false
        };

        await viewModel.ToggleFavoriteCommand.ExecuteAsync(channel);

        Assert.False(channel.IsFavorite);
    }

    [Fact]
    public async Task AddToMyListAsync_SerializesRapidClicksAndPersistsEachCapturedState()
    {
        var firstCallStarted = NewSignal();
        var releaseFirstCall = NewSignal();
        var observedStates = new ConcurrentQueue<bool>();
        var updateCount = 0;
        var channelService = new Mock<IChannelService>();
        channelService
            .Setup(service => service.UpdateChannelAsync(
                It.IsAny<Channel>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Channel channel, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref updateCount) == 1)
                {
                    firstCallStarted.TrySetResult(true);
                    await releaseFirstCall.Task;
                }

                observedStates.Enqueue(channel.IsInMyList);
            });

        var viewModel = CreateViewModel(channelService: channelService);
        var channel = new Channel
        {
            Id = 21,
            PlaylistId = 7,
            Name = "Movie",
            StreamUrl = "https://stream.test/movie-3",
            Type = ChannelType.VOD,
            IsInMyList = false
        };

        var first = viewModel.AddToMyListCommand.ExecuteAsync(channel);
        await firstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = viewModel.AddToMyListCommand.ExecuteAsync(channel);
        await Task.Delay(50);

        Assert.True(channel.IsInMyList);

        releaseFirstCall.TrySetResult(true);
        await Task.WhenAll(first, second);

        Assert.Equal(new[] { true, false }, observedStates);
        Assert.False(channel.IsInMyList);
    }

    [Fact]
    public async Task AddToMyListAsync_RollsBackOptimisticStateWhenPersistenceFails()
    {
        var channelService = new Mock<IChannelService>();
        channelService
            .Setup(service => service.UpdateChannelAsync(
                It.IsAny<Channel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write failed"));

        var viewModel = CreateViewModel(channelService: channelService);
        var channel = new Channel
        {
            Id = 22,
            PlaylistId = 7,
            Name = "Movie",
            StreamUrl = "https://stream.test/movie-4",
            Type = ChannelType.VOD,
            IsInMyList = false
        };

        await viewModel.AddToMyListCommand.ExecuteAsync(channel);

        Assert.False(channel.IsInMyList);
    }

    private MainViewModel CreateViewModel(Mock<IChannelService>? channelService = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Settings).Returns(new AppSettings());

        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(service => service.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        dispatcher.Setup(service => service.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        dispatcher.Setup(service => service.InvokeAsync(It.IsAny<Func<Task>>()))
            .Returns(Task.CompletedTask);

        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service.GetString(It.IsAny<string>()))
            .Returns("Test");

        var contentQuery = new Mock<IContentQueryService>();
        var contextFactory = new Mock<IDbContextFactory<AppDbContext>>();
        contextFactory
            .Setup(factory => factory.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_options));

        var viewModel = new MainViewModel(
            settings.Object,
            new Mock<IContentDownloadService>().Object,
            new Mock<IMetadataService>().Object,
            dispatcher.Object,
            new Mock<IDialogService>().Object,
            null!,
            (channelService ?? new Mock<IChannelService>()).Object,
            new Mock<IMediaService>().Object,
            new Mock<IEpgService>().Object,
            new Mock<IPlaylistService>().Object,
            new Mock<IWatchHistoryService>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            null!,
            null!,
            contextFactory.Object,
            new Mock<ISecurityService>().Object,
            new Mock<ITmdbSyncService>().Object,
            new Mock<ILicenseService>().Object,
            new Mock<IAppVersionService>().Object,
            localization.Object,
            new Mock<ILogger<MainViewModel>>().Object,
            null,
            null,
            null,
            contentQuery.Object);

        typeof(MainViewModel)
            .GetProperty(nameof(MainViewModel.CurrentProfileId))!
            .SetValue(viewModel, 1);
        viewModel.SelectedPlaylist = new Playlist
        {
            Id = 7,
            ProfileId = 1,
            Name = "Test playlist"
        };
        return viewModel;
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
