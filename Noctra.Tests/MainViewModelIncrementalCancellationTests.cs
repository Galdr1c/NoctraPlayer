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
    public void SearchText_PunctuationOnly_TerminatesInIdleState()
    {
        var viewModel = CreateViewModel(new Mock<IContentQueryService>().Object);
        viewModel.ActiveView = AppView.Search;

        viewModel.SearchText = "!!";

        Assert.False(viewModel.IsSearching);
        Assert.False(viewModel.ShowSearchEmptyState);
        Assert.Empty(viewModel.SearchVodChannels);
        Assert.Empty(viewModel.SearchSeriesChannels);
        Assert.Empty(viewModel.SearchLiveChannels);
    }

    [Fact]
    public async Task SearchSecondPage_EvaluatesOnlyNewChannelsAndDoesNotRescoreSeries()
    {
        var firstPage = Enumerable.Range(1, 30)
            .Select(id => new Channel
            {
                Id = id,
                PlaylistId = 7,
                Name = $"Dark item {id}",
                StreamUrl = $"https://stream.test/{id}",
                Type = ChannelType.VOD
            })
            .ToList();
        var secondPage = new List<Channel>
        {
            new()
            {
                Id = 31,
                PlaylistId = 7,
                Name = "Dark item 31",
                StreamUrl = "https://stream.test/31",
                Type = ChannelType.VOD
            }
        };
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((31, new List<string>(), new List<string>(), new List<string>(), new List<string>()));
        contentQuery
            .Setup(service => service.GetSeriesListAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Series { Id = 100, PlaylistId = 7, Name = "Dark Series" }
            ]);
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((ContentPageRequest request, CancellationToken _) => Task.FromResult(
                request.Skip == 0 ? firstPage : request.Skip == 30 ? secondPage : new List<Channel>()));

        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Search;
        viewModel.SearchText = "dark";
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        await WaitForAsync(() =>
            viewModel.SearchRankingChannelEvaluationCount == 30 &&
            viewModel.SearchRankingSeriesEvaluationCount == 1 &&
            viewModel.SearchVodChannels.Any(channel => channel.Id == 1));
        Assert.Equal(1, viewModel.SearchRankingSeriesInputVisitCount);
        var vodCollection = viewModel.SearchVodChannels;
        var seriesCollection = viewModel.SearchSeriesChannels;
        var existingVod = viewModel.SearchVodChannels.First(channel => channel.Id == 1);
        var vodChanges = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        var seriesChanges = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        vodCollection.CollectionChanged += (_, change) => vodChanges.Add(change);
        seriesCollection.CollectionChanged += (_, change) => seriesChanges.Add(change);

        await viewModel.LoadMoreChannelsAsync();
        await WaitForAsync(() => viewModel.SearchRankingChannelEvaluationCount == 31);

        Assert.Equal(31, viewModel.SearchRankingChannelEvaluationCount);
        Assert.Equal(1, viewModel.SearchRankingSeriesEvaluationCount);
        Assert.Equal(1, viewModel.SearchRankingSeriesInputVisitCount);
        Assert.Same(vodCollection, viewModel.SearchVodChannels);
        Assert.Same(seriesCollection, viewModel.SearchSeriesChannels);
        Assert.Same(existingVod, viewModel.SearchVodChannels.First(channel => channel.Id == 1));
        Assert.Contains(viewModel.SearchVodChannels, channel => channel.Id == 31);
        Assert.DoesNotContain(
            vodChanges,
            change => change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset);
        Assert.Empty(seriesChanges);
    }

    [Fact]
    public async Task SearchRankingCompletion_AfterLeavingSearch_CannotCommitOldResults()
    {
        var contentQuery = new Mock<IContentQueryService>();
        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Search;

        var suppressPlaylist = typeof(MainViewModel).GetField(
            "_suppressSelectedPlaylistChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(suppressPlaylist);
        suppressPlaylist.SetValue(viewModel, true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        suppressPlaylist.SetValue(viewModel, false);

        var suppressSearch = typeof(MainViewModel).GetField(
            "_suppressNavigationFilterRefresh",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(suppressSearch);
        suppressSearch.SetValue(viewModel, true);
        viewModel.SearchText = "old query";
        suppressSearch.SetValue(viewModel, false);

        var beginGeneration = typeof(MainViewModel).GetMethod(
            "BeginIncrementalContentGeneration",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(beginGeneration);
        var generation = (int)beginGeneration.Invoke(viewModel, null)!;
        var gatedPage = new GatedChannelPage(new Channel
        {
            Id = 700,
            PlaylistId = 7,
            Name = "Old Query Result",
            Type = ChannelType.VOD
        });
        var updateSearch = typeof(MainViewModel).GetMethod(
            "UpdateSearchBucketsIncrementallyAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(updateSearch);
        var ranking = (Task)updateSearch.Invoke(
            viewModel,
            new object[] { gatedPage, generation, 7, CancellationToken.None })!;
        await gatedPage.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.ActiveView = AppView.Movies;
        gatedPage.Release.TrySetResult();
        await ranking.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(viewModel.SearchVodChannels, channel => channel.Id == 700);
    }

    [Fact]
    public async Task SearchRankingCompletion_AfterQueryReplacement_CannotCommitOldResults()
    {
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Channel>());
        contentQuery
            .Setup(service => service.GetSeriesListAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>());
        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Search;

        var suppressPlaylist = typeof(MainViewModel).GetField(
            "_suppressSelectedPlaylistChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(suppressPlaylist);
        suppressPlaylist.SetValue(viewModel, true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        suppressPlaylist.SetValue(viewModel, false);

        var suppressSearch = typeof(MainViewModel).GetField(
            "_suppressNavigationFilterRefresh",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(suppressSearch);
        suppressSearch.SetValue(viewModel, true);
        viewModel.SearchText = "old query";
        suppressSearch.SetValue(viewModel, false);

        var beginGeneration = typeof(MainViewModel).GetMethod(
            "BeginIncrementalContentGeneration",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(beginGeneration);
        var generation = (int)beginGeneration.Invoke(viewModel, null)!;
        var gatedPage = new GatedChannelPage(new Channel
        {
            Id = 701,
            PlaylistId = 7,
            Name = "Old Query Result",
            Type = ChannelType.VOD
        });
        var updateSearch = typeof(MainViewModel).GetMethod(
            "UpdateSearchBucketsIncrementallyAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(updateSearch);
        var ranking = (Task)updateSearch.Invoke(
            viewModel,
            new object[] { gatedPage, generation, 7, CancellationToken.None })!;
        await gatedPage.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.SearchText = "new query";
        gatedPage.Release.TrySetResult();
        await ranking.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(viewModel.SearchVodChannels, channel => channel.Id == 701);
        viewModel.SearchText = string.Empty;
    }

    [Fact]
    public async Task SearchRanking_DuringPlaylistHandoff_DoesNotAdmitPreviousPlaylistSeries()
    {
        var viewModel = CreateViewModel(new Mock<IContentQueryService>().Object);
        viewModel.ActiveView = AppView.Search;

        var suppressPlaylist = typeof(MainViewModel).GetField(
            "_suppressSelectedPlaylistChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(suppressPlaylist);
        suppressPlaylist.SetValue(viewModel, true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "New playlist" };
        suppressPlaylist.SetValue(viewModel, false);

        var suppressSearch = typeof(MainViewModel).GetField(
            "_suppressNavigationFilterRefresh",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(suppressSearch);
        suppressSearch.SetValue(viewModel, true);
        viewModel.SearchText = "dark";
        suppressSearch.SetValue(viewModel, false);

        var allSeriesCache = typeof(MainViewModel).GetField(
            "_allSeriesCache",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(allSeriesCache);
        allSeriesCache.SetValue(viewModel, new List<Series>
        {
            new() { Id = 600, PlaylistId = 6, Name = "Dark stale series" }
        });

        var beginGeneration = typeof(MainViewModel).GetMethod(
            "BeginIncrementalContentGeneration",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(beginGeneration);
        var generation = (int)beginGeneration.Invoke(viewModel, null)!;
        var updateSearch = typeof(MainViewModel).GetMethod(
            "UpdateSearchBucketsIncrementallyAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(updateSearch);

        var ranking = (Task)updateSearch.Invoke(
            viewModel,
            new object[] { Array.Empty<Channel>(), generation, 7, CancellationToken.None })!;
        await ranking;

        Assert.DoesNotContain(viewModel.SearchSeriesChannels, series => series.PlaylistId == 6);
    }

    [Fact]
    public async Task SearchRanking_WhenSeriesDatasetVersionChanges_ReplacesSameIdentityResult()
    {
        var viewModel = CreateViewModel(new Mock<IContentQueryService>().Object);
        viewModel.ActiveView = AppView.Search;
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", false);
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", true);
        viewModel.SearchText = "dark";
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", false);
        SetPrivateField(viewModel, "_allSeriesCache", new List<Series>
        {
            new() { Id = 77, PlaylistId = 7, Name = "Dark Original" }
        });

        var generation = BeginContentGeneration(viewModel);
        await InvokeSearchUpdate(viewModel, generation);
        Assert.Contains(viewModel.SearchSeriesChannels, series => series.Name == "Dark Original");

        SetPrivateField(viewModel, "_allSeriesCache", new List<Series>
        {
            new() { Id = 77, PlaylistId = 7, Name = "Unrelated Replacement" }
        });
        var versionField = typeof(MainViewModel).GetField(
            "_seriesSearchDatasetVersion",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(versionField);
        versionField.SetValue(viewModel, (long)versionField.GetValue(viewModel)! + 1);

        await InvokeSearchUpdate(viewModel, generation);

        Assert.DoesNotContain(viewModel.SearchSeriesChannels, series => series.Id == 77);
        Assert.Equal(2, viewModel.SearchRankingSeriesEvaluationCount);
    }

    [Fact]
    public async Task SearchRanking_WhenSeriesChangesDuringRanking_RetriesLatestDatasetBeforeCommit()
    {
        var viewModel = CreateViewModel(new Mock<IContentQueryService>().Object);
        viewModel.ActiveView = AppView.Search;
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", false);
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", true);
        viewModel.SearchText = "dark";
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", false);

        var versionField = typeof(MainViewModel).GetField(
            "_seriesSearchDatasetVersion",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(versionField);
        var replacement = new List<Series>
        {
            new() { Id = 78, PlaylistId = 7, Name = "Unrelated Replacement" }
        };
        var old = new Series
        {
            Id = 78,
            PlaylistId = 7,
            Name = "Dark Old",
            Seasons =
            [
                new Season
                {
                    Id = 1,
                    Episodes = new ActionEpisodeCollection(() =>
                    {
                        SetPrivateField(viewModel, "_allSeriesCache", replacement);
                        versionField.SetValue(viewModel, (long)versionField.GetValue(viewModel)! + 1);
                    })
                }
            ]
        };
        SetPrivateField(viewModel, "_allSeriesCache", new List<Series> { old });

        await InvokeSearchUpdate(viewModel, BeginContentGeneration(viewModel));

        Assert.DoesNotContain(viewModel.SearchSeriesChannels, series => series.Id == 78);
        Assert.Equal(2, viewModel.SearchRankingSeriesEvaluationCount);
    }

    [Fact]
    public async Task SearchPage_CallerCancellationDuringRanking_DoesNotAdvancePagination()
    {
        var hugeName = "dark " + new string('x', 250_000);
        var page = Enumerable.Range(1, 30).Select(id => new Channel
        {
            Id = id,
            PlaylistId = 7,
            Name = hugeName,
            Type = ChannelType.VOD,
            StreamUrl = $"https://stream.test/{id}"
        }).ToList();
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery.Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Search;
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", false);
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", true);
        viewModel.SearchText = "dark";
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", false);
        SetPrivateField(viewModel, "_hasMoreChannels", true);
        var generation = BeginContentGeneration(viewModel);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        await viewModel.LoadMoreChannelsAsync(cancellation.Token, generation);

        Assert.Equal(0, GetPrivateField<int>(viewModel, "_currentPage"));
        Assert.Empty(viewModel.FilteredChannels);
        Assert.Empty(viewModel.SearchVodChannels);
    }

    [Fact]
    public async Task SearchTerminalPage_CallerCancellationDuringSeriesRanking_RemainsRetryable()
    {
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery.Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Channel>());
        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Search;
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", true);
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Search" };
        SetPrivateField(viewModel, "_suppressSelectedPlaylistChanged", false);
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", true);
        viewModel.SearchText = "dark";
        SetPrivateField(viewModel, "_suppressNavigationFilterRefresh", false);
        SetPrivateField(viewModel, "_hasMoreChannels", true);
        SetPrivateField(viewModel, "_allSeriesCache", Enumerable.Range(1, 20).Select(id => new Series
        {
            Id = id,
            PlaylistId = 7,
            Name = "dark " + new string('x', 250_000)
        }).ToList());
        var generation = BeginContentGeneration(viewModel);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        await viewModel.LoadMoreChannelsAsync(cancellation.Token, generation);

        Assert.True(GetPrivateField<bool>(viewModel, "_hasMoreChannels"));
        Assert.Empty(viewModel.SearchSeriesChannels);
    }

    [Fact]
    public async Task ProviderCategoriesPersisted_AfterEmptyMetadataCache_ShowsGroupFilterWithoutProfileReload()
    {
        var metadataAttempt = 0;
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(7, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref metadataAttempt) == 1)
                {
                    return Task.FromResult((
                        0,
                        new List<string>(),
                        new List<string>(),
                        new List<string>(),
                        new List<string>()));
                }

                return Task.FromResult((
                    1,
                    new List<string> { "Live category" },
                    new List<string> { "Live category" },
                    new List<string>(),
                    new List<string>()));
            });
        contentQuery
            .Setup(service => service.GetSeriesListAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Series>());
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Channel
                {
                    Id = 701,
                    PlaylistId = 7,
                    Name = "First progressive channel",
                    StreamUrl = "https://stream.test/live",
                    GroupTitle = "Live category",
                    Type = ChannelType.Live
                }
            ]);

        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.ActiveView = AppView.Live;
        viewModel.SelectedChannelType = ChannelType.Live;
        viewModel.SelectedPlaylist = new Playlist { Id = 7, Name = "Fresh Stalker" };
        await WaitForAsync(() => Volatile.Read(ref metadataAttempt) == 1 && !viewModel.IsChannelLoading);

        Assert.False(viewModel.ShowGroupFilter);

        var refreshMethod = typeof(MainViewModel).GetMethod(
            "RefreshGroupMetadataAfterProviderPersistence",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(refreshMethod);
        refreshMethod.Invoke(viewModel, new object[] { 7 });

        await WaitForAsync(() => viewModel.ShowGroupFilter);
        Assert.Equal(2, Volatile.Read(ref metadataAttempt));
        Assert.Contains("Live category", viewModel.Groups);
    }

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

    [Fact]
    public async Task NavigatingHome_WhenInitialMetadataIsSuperseded_RecoversSeriesAndGroupMetadata()
    {
        var metadataStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new TaskCompletionSource<(int, List<string>, List<string>, List<string>, List<string>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var metadataAttempt = 0;
        var contentQuery = new Mock<IContentQueryService>();
        contentQuery
            .Setup(service => service.GetChannelGroupMetadataAsync(3, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref metadataAttempt) == 1)
                {
                    metadataStarted.TrySetResult(token);
                    token.Register(() => metadata.TrySetCanceled(token));
                    return metadata.Task;
                }

                return Task.FromResult((
                    1,
                    new List<string> { "Recovered M3U group" },
                    new List<string>(),
                    new List<string>(),
                    new List<string> { "Recovered M3U group" }));
            });
        contentQuery
            .Setup(service => service.GetSeriesListAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Series
                {
                    Id = 71,
                    PlaylistId = 3,
                    Name = "Recovered M3U series"
                }
            ]);
        contentQuery
            .Setup(service => service.GetChannelPageAsync(
                It.IsAny<ContentPageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Channel>());

        var viewModel = CreateViewModel(contentQuery.Object);
        viewModel.SelectedPlaylist = new Playlist { Id = 3, Name = "M3U" };
        var oldToken = await metadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.NavigateCommand.Execute(AppView.Home);

        await WaitForAsync(() =>
            viewModel.SeriesViewItems.Any(series => series.Id == 71) &&
            viewModel.Groups.Contains("Recovered M3U group"));
        Assert.True(oldToken.IsCancellationRequested);
        Assert.True(metadataAttempt >= 2);
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
        dispatcher.Setup(service => service.InvokeAsync(It.IsAny<Func<bool>>()))
            .Returns((Func<bool> action) => Task.FromResult(action()));

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

    private static void SetPrivateField(MainViewModel viewModel, string name, object value)
    {
        var field = typeof(MainViewModel).GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    private static T GetPrivateField<T>(MainViewModel viewModel, string name)
    {
        var field = typeof(MainViewModel).GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(viewModel)!;
    }

    private static int BeginContentGeneration(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod(
            "BeginIncrementalContentGeneration",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (int)method.Invoke(viewModel, null)!;
    }

    private static Task InvokeSearchUpdate(MainViewModel viewModel, int generation)
    {
        var method = typeof(MainViewModel).GetMethod(
            "UpdateSearchBucketsIncrementallyAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task)method.Invoke(
            viewModel,
            new object[] { Array.Empty<Channel>(), generation, 7, CancellationToken.None })!;
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

    private sealed class GatedChannelPage(Channel channel) : IReadOnlyCollection<Channel>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => 1;

        public IEnumerator<Channel> GetEnumerator()
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            yield return channel;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ActionEpisodeCollection(Action onFirstMoveNext) : ICollection<Episode>
    {
        private int _invoked;
        public int Count => 1;
        public bool IsReadOnly => true;
        public IEnumerator<Episode> GetEnumerator()
        {
            if (Interlocked.Exchange(ref _invoked, 1) == 0)
            {
                onFirstMoveNext();
            }
            yield return new Episode { Id = 1, Name = "Pilot" };
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Add(Episode item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(Episode item) => false;
        public void CopyTo(Episode[] array, int arrayIndex) => throw new NotSupportedException();
        public bool Remove(Episode item) => throw new NotSupportedException();
    }
}
