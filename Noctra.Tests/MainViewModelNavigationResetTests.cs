using System.Collections.Concurrent;
using System.Collections.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Diagnostics;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

[CollectionDefinition("Navigation reset performance trace", DisableParallelization = true)]
public sealed class NavigationResetTraceCollection
{
    public const string CollectionName = "Navigation reset performance trace";
}

[Collection(NavigationResetTraceCollection.CollectionName)]
public sealed class MainViewModelNavigationResetTests
{
    [Fact]
    public async Task NavigateBetweenChannelPages_KeepsCollectionsAndPublishesOneReset()
    {
        var firstMovieQueryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMoviePage = new TaskCompletionSource<List<Channel>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var contentQuery = CreateContentQuery();
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((ContentPageRequest request, CancellationToken _) =>
            {
                if (request.Type != ChannelType.VOD)
                {
                    return Task.FromResult(CreateChannelPage(request, 1));
                }

                if (request.Skip == 0)
                {
                    firstMovieQueryStarted.TrySetResult();
                    return firstMoviePage.Task;
                }

                return Task.FromResult(CreateChannelPage(request, 1));
            });
        var viewModel = CreateViewModel(contentQuery.Object);

        await LoadInitialLivePageAsync(viewModel);
        var originalChannels = viewModel.Channels;
        var originalFilteredChannels = viewModel.FilteredChannels;
        var events = ObserveCollection(originalFilteredChannels);

        viewModel.NavigateCommand.Execute(AppView.Movies);

        Assert.Same(originalChannels, viewModel.Channels);
        Assert.Same(originalFilteredChannels, viewModel.FilteredChannels);
        Assert.Empty(originalFilteredChannels);
        AssertEventSequence(events, (NotifyCollectionChangedAction.Reset, -1, 0));

        await firstMovieQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        firstMoviePage.SetResult(CreateChannelPage(
            new ContentPageRequest(7, 0, 30, Type: ChannelType.VOD),
            30));
        await WaitForAsync(() =>
            viewModel.FilteredChannels.Count == 30 && events.Count >= 2);

        Assert.Same(originalChannels, viewModel.Channels);
        Assert.Same(originalFilteredChannels, viewModel.FilteredChannels);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30));
        Assert.All(viewModel.FilteredChannels, channel => Assert.Equal(ChannelType.VOD, channel.Type));

        await viewModel.LoadMoreChannelsAsync();
        await WaitForAsync(() =>
            viewModel.FilteredChannels.Count == 31 && events.Count >= 3);

        Assert.Same(originalFilteredChannels, viewModel.FilteredChannels);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30),
            (NotifyCollectionChangedAction.Add, 30, 1));
    }

    [Fact]
    public async Task NavigateToSeries_KeepsCollectionAndPublishesOneReset()
    {
        var contentQuery = CreateContentQuery();
        var viewModel = CreateViewModel(contentQuery.Object);

        await LoadInitialLivePageAsync(viewModel);
        var originalSeriesItems = viewModel.SeriesViewItems;
        var events = ObserveCollection(originalSeriesItems);

        viewModel.NavigateCommand.Execute(AppView.Series);

        Assert.Same(originalSeriesItems, viewModel.SeriesViewItems);
        Assert.Empty(originalSeriesItems);
        AssertEventSequence(events, (NotifyCollectionChangedAction.Reset, -1, 0));

        await WaitForAsync(() =>
            viewModel.SeriesViewItems.Count == 30 && events.Count >= 2);

        Assert.Same(originalSeriesItems, viewModel.SeriesViewItems);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30));

        await viewModel.LoadMoreSeriesAsync();
        await WaitForAsync(() =>
            viewModel.SeriesViewItems.Count == 31 && events.Count >= 3);

        Assert.Same(originalSeriesItems, viewModel.SeriesViewItems);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30),
            (NotifyCollectionChangedAction.Add, 30, 1));
    }

    [Fact]
    public async Task SamePageFilter_KeepsCollectionAndPublishesOneReset()
    {
        var contentQuery = CreateContentQuery();
        var viewModel = CreateViewModel(contentQuery.Object);

        await LoadInitialLivePageAsync(viewModel);
        var originalFilteredChannels = viewModel.FilteredChannels;
        var events = ObserveCollection(originalFilteredChannels);
        var originalProbe = PerformanceTrace.Probe;
        var probe = new RecordingPerformanceProbe();
        PerformanceTrace.Probe = probe;
        try
        {
            viewModel.SelectedSortOrder = ChannelSortOrder.NameAsc;

            await WaitForAsync(() =>
                viewModel.FilteredChannels.Count == 1 &&
                viewModel.FilteredChannels[0].Name == "Sorted live channel" &&
                events.Count >= 2);
        }
        finally
        {
            PerformanceTrace.Probe = originalProbe;
        }

        Assert.Same(originalFilteredChannels, viewModel.FilteredChannels);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 1));
        Assert.Contains(probe.Events, item =>
            item.Name == "navigation.reset.ordinary_filter.count" && item.Value > 0);
    }

    [Fact]
    public async Task RapidNavigation_StaleChannelPassCannotResetOrPopulateSeriesDestination()
    {
        var vodQueryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var vodPage = new TaskCompletionSource<List<Channel>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var contentQuery = CreateContentQuery();
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((ContentPageRequest request, CancellationToken _) =>
            {
                if (request.Type == ChannelType.VOD)
                {
                    vodQueryStarted.TrySetResult();
                    return vodPage.Task;
                }

                return Task.FromResult(new List<Channel>
                {
                    new()
                    {
                        Id = 701,
                        PlaylistId = 7,
                        Name = "Live channel",
                        StreamUrl = "https://stream.test/live",
                        GroupTitle = "Live",
                        Type = ChannelType.Live
                    }
                });
            });
        var viewModel = CreateViewModel(contentQuery.Object);

        await LoadInitialLivePageAsync(viewModel);
        var originalSeriesItems = viewModel.SeriesViewItems;
        var seriesEvents = ObserveCollection(originalSeriesItems);
        var originalProbe = PerformanceTrace.Probe;
        var probe = new RecordingPerformanceProbe();
        PerformanceTrace.Probe = probe;
        try
        {
            viewModel.NavigateCommand.Execute(AppView.Movies);
            await vodQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            viewModel.NavigateCommand.Execute(AppView.Series);
            vodPage.SetResult([
                new Channel
                {
                    Id = 799,
                    PlaylistId = 7,
                    Name = "Stale movie",
                    StreamUrl = "https://stream.test/stale",
                    Type = ChannelType.VOD
                }
            ]);

            await WaitForAsync(() =>
                viewModel.ActiveView == AppView.Series &&
                viewModel.SeriesViewItems.Count == 30 &&
                !viewModel.IsContentLoading &&
                seriesEvents.Count >= 2);
        }
        finally
        {
            PerformanceTrace.Probe = originalProbe;
        }

        Assert.Same(originalSeriesItems, viewModel.SeriesViewItems);
        AssertEventSequence(
            seriesEvents,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30));
        Assert.DoesNotContain(viewModel.FilteredChannels, channel => channel.Name == "Stale movie");
        Assert.Contains(probe.Events, item => item.Name == "navigation.reset.prepared.count");
        Assert.Contains(probe.Events, item => item.Name == "navigation.reset.consumed.count");
        Assert.Contains(probe.Events, item => item.Name == "navigation.reset.stale_completion.count");
    }

    [Fact]
    public async Task DeferredSeriesRefresh_ReusesPreparedEmptySurfaceAndKeepsPagination()
    {
        var contentQuery = CreateContentQuery();
        contentQuery
            .SetupSequence(service => service.GetSeriesListAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>())
            .ReturnsAsync(new List<Series>())
            .ReturnsAsync(CreateSeriesPage(31));
        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Live;
        viewModel.SelectedChannelType = ChannelType.Live;
        viewModel.IsChannelLoading = true;
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Deferred series test" };
        await WaitForAsync(() =>
            viewModel.FilteredChannels.Any(channel => channel.Type == ChannelType.Live) &&
            !viewModel.IsLoading);

        var originalSeriesItems = viewModel.SeriesViewItems;
        var events = ObserveCollection(originalSeriesItems);

        viewModel.NavigateCommand.Execute(AppView.Series);

        Assert.Same(originalSeriesItems, viewModel.SeriesViewItems);
        Assert.Empty(originalSeriesItems);
        AssertEventSequence(events, (NotifyCollectionChangedAction.Reset, -1, 0));
        await WaitForAsync(() => !viewModel.IsLoading);

        var completeChannelRefresh = typeof(MainViewModel).GetMethod(
            "CompleteChannelRefreshProgress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(completeChannelRefresh);
        completeChannelRefresh.Invoke(viewModel, ["Test"]);

        await WaitForAsync(() =>
            viewModel.SeriesViewItems.Count == 30 && events.Count >= 2);
        Assert.Same(originalSeriesItems, viewModel.SeriesViewItems);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30));

        await viewModel.LoadMoreSeriesAsync();
        await WaitForAsync(() =>
            viewModel.SeriesViewItems.Count == 31 && events.Count >= 3);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 30),
            (NotifyCollectionChangedAction.Add, 30, 1));
    }

    [Fact]
    public async Task NavigateAwayFromPreparedContent_DoesNotLeakLoadingState()
    {
        var contentQuery = CreateContentQuery();
        var viewModel = CreateViewModel(contentQuery.Object);
        await LoadInitialLivePageAsync(viewModel);

        viewModel.NavigateCommand.Execute(AppView.Movies);
        Assert.True(viewModel.IsContentLoading);

        viewModel.NavigateCommand.Execute(AppView.Downloads);

        Assert.False(viewModel.IsContentLoading);
    }

    [Fact]
    public async Task ShortSearchCancellation_AbandonsPreparedOwnerWithoutLoadingLeak()
    {
        var contentQuery = CreateContentQuery();
        var viewModel = CreateViewModel(contentQuery.Object);
        await LoadInitialLivePageAsync(viewModel);

        viewModel.NavigateCommand.Execute(AppView.Movies);
        Assert.True(viewModel.IsContentLoading);

        viewModel.SearchText = "x";

        Assert.False(viewModel.IsContentLoading);
        Assert.Empty(viewModel.FilteredChannels);
    }

    [Fact]
    public async Task PreparedNavigation_BypassesStaleDuplicateSignatureAndCompletesOwner()
    {
        var contentQuery = CreateContentQuery();
        var viewModel = CreateViewModel(contentQuery.Object);
        await LoadInitialLivePageAsync(viewModel);
        var originalFilteredChannels = viewModel.FilteredChannels;
        var events = ObserveCollection(originalFilteredChannels);

        viewModel.NavigateCommand.Execute(AppView.Movies);

        var buildSignature = typeof(MainViewModel).GetMethod(
            "BuildFilterSignature",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var signatureField = typeof(MainViewModel).GetField(
            "_lastCompletedFilterSignature",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var completedUtcField = typeof(MainViewModel).GetField(
            "_lastCompletedFilterUtc",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(buildSignature);
        Assert.NotNull(signatureField);
        Assert.NotNull(completedUtcField);
        var signature = Assert.IsType<string>(buildSignature.Invoke(viewModel, null));
        signatureField.SetValue(viewModel, signature);
        completedUtcField.SetValue(viewModel, DateTime.UtcNow);

        await WaitForAsync(() =>
            viewModel.FilteredChannels.Count == 1 &&
            !viewModel.IsContentLoading &&
            events.Count >= 2);

        Assert.Same(originalFilteredChannels, viewModel.FilteredChannels);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 1));
    }

    [Fact]
    public async Task ConcurrentFilterScheduling_DoesNotDowngradePreparedOwnerOrResetTwice()
    {
        var movieQueryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var moviePage = new TaskCompletionSource<List<Channel>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var contentQuery = CreateContentQuery();
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.Is<ContentPageRequest>(request => request.Type == ChannelType.VOD),
                It.IsAny<CancellationToken>()))
            .Returns((ContentPageRequest _, CancellationToken _) =>
            {
                movieQueryStarted.TrySetResult();
                return moviePage.Task;
            });
        var viewModel = CreateViewModel(contentQuery.Object);
        await LoadInitialLivePageAsync(viewModel);
        var originalFilteredChannels = viewModel.FilteredChannels;
        var events = ObserveCollection(originalFilteredChannels);

        viewModel.NavigateCommand.Execute(AppView.Movies);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            viewModel.ScheduleImmediateFilter("concurrent-owner-test"))));

        await movieQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        moviePage.SetResult(CreateChannelPage(
            new ContentPageRequest(7, 0, 30, Type: ChannelType.VOD),
            1));
        await WaitForAsync(() =>
            viewModel.FilteredChannels.Count == 1 &&
            !viewModel.IsContentLoading &&
            events.Count >= 2);

        Assert.Same(originalFilteredChannels, viewModel.FilteredChannels);
        AssertEventSequence(
            events,
            (NotifyCollectionChangedAction.Reset, -1, 0),
            (NotifyCollectionChangedAction.Add, 0, 1));
    }

    private static Mock<IContentQueryService> CreateContentQuery()
    {
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                2,
                new List<string> { "Live", "Movies" },
                new List<string> { "Live" },
                new List<string> { "Movies" },
                new List<string> { "Series" }));
        contentQuery
            .Setup(service => service.GetSeriesListAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSeriesPage(31));
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((ContentPageRequest request, CancellationToken _) =>
                Task.FromResult(CreateChannelPage(request, 1)));

        return contentQuery;
    }

    private static async Task LoadInitialLivePageAsync(MainViewModel viewModel)
    {
        viewModel.ActiveView = AppView.Live;
        viewModel.SelectedChannelType = ChannelType.Live;
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Navigation test" };
        await WaitForAsync(() =>
            viewModel.FilteredChannels.Any(channel => channel.Type == ChannelType.Live) &&
            !viewModel.IsContentLoading);
    }

    private static List<Channel> CreateChannelPage(ContentPageRequest request, int count)
    {
        var type = request.Type == ChannelType.VOD ? ChannelType.VOD : ChannelType.Live;
        var baseId = type == ChannelType.VOD ? 7000 : 6000;
        var prefix = type == ChannelType.VOD ? "Movie" : "Live";
        return Enumerable.Range(request.Skip, count)
            .Select(index => new Channel
            {
                Id = baseId + index,
                PlaylistId = request.PlaylistId,
                Name = request.SortOrder == ChannelSortOrder.NameAsc && type == ChannelType.Live
                    ? "Sorted live channel"
                    : $"{prefix} channel {index}",
                StreamUrl = $"https://stream.test/{prefix.ToLowerInvariant()}/{index}",
                GroupTitle = type == ChannelType.VOD ? "Movies" : "Live",
                Type = type
            })
            .ToList();
    }

    private static List<Series> CreateSeriesPage(int count)
        => Enumerable.Range(0, count)
            .Select(index => new Series
            {
                Id = 901 + index,
                PlaylistId = 7,
                Name = $"Series item {index}",
                GroupTitle = "Series"
            })
            .ToList();

    private static ConcurrentQueue<CollectionEvent> ObserveCollection<T>(
        System.Collections.ObjectModel.ObservableCollection<T> collection)
    {
        var events = new ConcurrentQueue<CollectionEvent>();
        collection.CollectionChanged += (_, args) => events.Enqueue(new CollectionEvent(
            args.Action,
            args.NewStartingIndex,
            args.NewItems?.Count ?? 0));
        return events;
    }

    private static void AssertEventSequence(
        ConcurrentQueue<CollectionEvent> events,
        params (NotifyCollectionChangedAction Action, int NewStartingIndex, int NewItemCount)[] expected)
    {
        Assert.Equal(
            expected,
            events.Select(item => (item.Action, item.NewStartingIndex, item.NewItemCount)).ToArray());
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
            new Mock<IAppVersionService>().Object,
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

    private sealed record CollectionEvent(
        NotifyCollectionChangedAction Action,
        int NewStartingIndex,
        int NewItemCount);

    private sealed class RecordingPerformanceProbe : IPerformanceProbe
    {
        public bool IsEnabled => true;

        public ConcurrentQueue<(string Name, long Value, string? Scope)> Events { get; } = new();

        public void Mark(string name, long value = 0, string? scope = null)
            => Events.Enqueue((name, value, scope));
    }
}
