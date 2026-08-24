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

public sealed class MainViewModelAdultCategoryOrderingTests
{
    [Fact]
    public void ContentPageRequest_ExposesKnownAdultGroupSnapshotForPagedQueries()
    {
        Assert.NotNull(typeof(ContentPageRequest).GetProperty("AdultGroupsLast"));
    }

    [Fact]
    public void CategoryOrder_KeepsPreferredNormalGroupsFirstAndAdultGroupsLast()
    {
        var viewModel = CreateViewModel(language: "en");
        var method = typeof(MainViewModel).GetMethod(
            "OrderGroupsByLanguagePreference",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var ordered = Assert.IsType<List<string>>(method!.Invoke(viewModel,
        [
            new List<string>
            {
                "For Adults",
                "DE | Sports",
                "US | News",
                "Adult Swim Classics",
                "Para Adultos"
            }
        ]));

        Assert.Equal(
            new[]
            {
                "US | News",
                "Adult Swim Classics",
                "DE | Sports",
                "For Adults",
                "Para Adultos"
            },
            ordered);
    }

    [Theory]
    [InlineData(ChannelSortOrder.NameAsc, "Normal Alpha,Normal Zulu,Adult Alpha,Adult Zulu")]
    [InlineData(ChannelSortOrder.NameDesc, "Normal Zulu,Normal Alpha,Adult Zulu,Adult Alpha")]
    [InlineData(ChannelSortOrder.OldestFirst, "Normal Alpha,Normal Zulu,Adult Alpha,Adult Zulu")]
    [InlineData(ChannelSortOrder.NewestFirst, "Normal Zulu,Normal Alpha,Adult Zulu,Adult Alpha")]
    public void AllSeriesSort_PreservesRequestedOrderInsideNormalAndAdultSegments(
        ChannelSortOrder sortOrder,
        string expectedCsv)
    {
        var viewModel = CreateViewModel();
        var source = new List<Series>
        {
            new() { Name = "Adult Alpha", GroupTitle = "For Adults", ReleaseYear = 2010 },
            new() { Name = "Normal Zulu", GroupTitle = "Drama", ReleaseYear = 2024 },
            new() { Name = "Adult Zulu", GroupTitle = "Para Adultos", ReleaseYear = 2030 },
            new() { Name = "Normal Alpha", GroupTitle = "News", ReleaseYear = 2020 }
        };
        var method = typeof(MainViewModel).GetMethod(
            "GetOrBuildAllSeriesSort",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var ordered = Assert.IsType<List<Series>>(method!.Invoke(
            viewModel,
            [source, sortOrder, Array.Empty<string>()]));

        Assert.Equal(expectedCsv.Split(','), ordered.Select(series => series.Name));
    }

    private static MainViewModel CreateViewModel(string language = "tr")
    {
        var settings = new AppSettings { Language = language };
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
            new Mock<IAppVersionService>().Object,
            localization.Object,
            new Mock<ILogger<MainViewModel>>().Object);
    }
}
