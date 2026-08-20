using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class EpgVisibleRefreshTests
{
    [Fact]
    public async Task TimerRefresh_QueriesOnlyTheCurrentViewportSnapshot()
    {
        var epg = new Mock<IEpgService>();
        var settings = new Mock<ISettingsService>();
        var vm = MainViewModelTestFactory.Create(settings, epg);
        vm.ActiveView = AppView.Live;
        var allLoaded = Enumerable.Range(1, 200)
            .Select(id => new Channel { Id = id, Type = ChannelType.Live })
            .ToList();
        vm.Channels.AddRange(allLoaded);
        var visible = allLoaded.Skip(87).Take(12).ToArray();
        var snapshotSetter = typeof(MainViewModel).GetMethod(
            "SetVisibleEpgChannels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(snapshotSetter);
        snapshotSetter!.Invoke(vm, [visible]);

        epg.Setup(service => service.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()))
            .ReturnsAsync(new Dictionary<int, EpgProgram?>());

        var refresh = typeof(MainViewModel).GetMethod(
            "EnrichVisibleChannelsWithEpgAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(refresh);
        await (Task)refresh!.Invoke(vm, null)!;

        epg.Verify(service => service.GetCurrentProgramsAsync(
            It.Is<IEnumerable<Channel>>(channels =>
                channels.Select(channel => channel.Id).SequenceEqual(visible.Select(channel => channel.Id)))),
            Times.Once);
    }

    [Fact]
    public async Task TimerRefresh_DoesNotQueryWhenViewportSnapshotIsEmpty()
    {
        var epg = new Mock<IEpgService>();
        var settings = new Mock<ISettingsService>();
        var vm = MainViewModelTestFactory.Create(settings, epg);
        vm.ActiveView = AppView.Live;

        var refresh = typeof(MainViewModel).GetMethod(
            "EnrichVisibleChannelsWithEpgAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(refresh);
        await (Task)refresh!.Invoke(vm, null)!;

        epg.Verify(service => service.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()), Times.Never);
    }

    [Fact]
    public async Task TimerRefresh_DoesNotQueryAfterLeavingLiveView()
    {
        var epg = new Mock<IEpgService>();
        var settings = new Mock<ISettingsService>();
        var vm = MainViewModelTestFactory.Create(settings, epg);
        vm.ActiveView = AppView.Live;
        var channels = new[] { new Channel { Id = 1, Type = ChannelType.Live } };
        typeof(MainViewModel).GetMethod("SetVisibleEpgChannels", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [channels]);
        vm.ActiveView = AppView.Movies;

        var refresh = typeof(MainViewModel).GetMethod(
            "EnrichVisibleChannelsWithEpgAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        await (Task)refresh!.Invoke(vm, null)!;

        epg.Verify(service => service.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()), Times.Never);
    }

    [Fact]
    public async Task TimerRefresh_DoesNotUseSnapshotAfterLiveFilterChanges()
    {
        var epg = new Mock<IEpgService>();
        var settings = new Mock<ISettingsService>();
        var vm = MainViewModelTestFactory.Create(settings, epg);
        vm.ActiveView = AppView.Live;
        var channels = new[] { new Channel { Id = 1, Type = ChannelType.Live } };
        typeof(MainViewModel).GetMethod("SetVisibleEpgChannels", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [channels]);
        vm.SelectedGroup = "sports";

        var refresh = typeof(MainViewModel).GetMethod(
            "EnrichVisibleChannelsWithEpgAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        await (Task)refresh!.Invoke(vm, null)!;

        epg.Verify(service => service.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()), Times.Never);
    }

    [Fact]
    public async Task TimerRefresh_DropsResultWhenViewportChangesDuringLookup()
    {
        var epg = new Mock<IEpgService>();
        var settings = new Mock<ISettingsService>();
        var vm = MainViewModelTestFactory.Create(settings, epg);
        vm.ActiveView = AppView.Live;
        var channel = new Channel { Id = 1, Type = ChannelType.Live };
        typeof(MainViewModel).GetMethod("SetVisibleEpgChannels", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { new[] { channel } });

        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookupResult = new TaskCompletionSource<Dictionary<int, EpgProgram?>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        epg.Setup(service => service.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()))
            .Returns(() =>
            {
                lookupStarted.TrySetResult();
                return lookupResult.Task;
            });

        var refresh = typeof(MainViewModel).GetMethod(
            "EnrichVisibleChannelsWithEpgAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var refreshTask = (Task)refresh!.Invoke(vm, null)!;
        await lookupStarted.Task;
        vm.SelectedGroup = "sports";
        lookupResult.SetResult(new Dictionary<int, EpgProgram?>
        {
            [1] = new EpgProgram { Title = "Stale program" }
        });
        await refreshTask;

        Assert.Null(channel.CurrentProgramTitle);
    }

    [Fact]
    public async Task QueuedSnapshotFromBeforeClearCannotBePublished()
    {
        var epg = new Mock<IEpgService>();
        var settings = new Mock<ISettingsService>();
        var vm = MainViewModelTestFactory.Create(settings, epg);
        vm.ActiveView = AppView.Live;
        var oldGeneration = vm.VisibleEpgChannelsInvalidationGeneration;
        vm.ClearVisibleEpgChannels();

        Assert.False(vm.TrySetVisibleEpgChannels(
            new[] { new Channel { Id = 1, Type = ChannelType.Live } },
            oldGeneration));

        var refresh = typeof(MainViewModel).GetMethod(
            "EnrichVisibleChannelsWithEpgAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        await (Task)refresh!.Invoke(vm, null)!;

        epg.Verify(service => service.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()), Times.Never);
    }

    private static class MainViewModelTestFactory
    {
        public static MainViewModel Create(
            Mock<ISettingsService> settings,
            Mock<IEpgService> epg)
        {
            settings.Setup(service => service.Settings).Returns(new AppSettings
            {
                EpgEnabled = true
            });

            return new MainViewModel(
                settings.Object,
                new Mock<IContentDownloadService>().Object,
                new Mock<IMetadataService>().Object,
                new ImmediateDispatcherService(),
                new Mock<IDialogService>().Object,
                null!,
                new Mock<IChannelService>().Object,
                new Mock<IMediaService>().Object,
                epg.Object,
                new Mock<IPlaylistService>().Object,
                new Mock<IWatchHistoryService>().Object,
                new Mock<IXtreamCodesService>().Object,
                new Mock<IStalkerPortalService>().Object,
                null!,
                null!,
                new Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<Noctra.Data.AppDbContext>>().Object,
                new Mock<ISecurityService>().Object,
                new Mock<ITmdbSyncService>().Object,
                new Mock<ILicenseService>().Object,
                new Mock<IAppVersionService>().Object,
                new Mock<ILocalizationService>().Object,
                new Mock<ILogger<MainViewModel>>().Object);
        }
    }

    private sealed class ImmediateDispatcherService : IDispatcherService
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
        public Task InvokeAsync(Func<Task> func) => func();
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
    }
}
