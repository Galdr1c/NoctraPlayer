using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class ContentQueryServiceTests
{
    [Fact]
    public async Task GetChannelGroupMetadataAsync_ForwardsCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var playlistService = new Mock<IPlaylistService>();
        playlistService
            .Setup(service => service.GetChannelGroupMetadataAsync(42, cancellationSource.Token))
            .ReturnsAsync((0, new List<string>(), new List<string>(), new List<string>(), new List<string>()));
        var service = new ContentQueryService(
            playlistService.Object,
            Mock.Of<IMediaService>(),
            Mock.Of<ISettingsService>(),
            Mock.Of<Microsoft.EntityFrameworkCore.IDbContextFactory<Noctra.Data.AppDbContext>>());

        await service.GetChannelGroupMetadataAsync(42, cancellationSource.Token);

        playlistService.VerifyAll();
    }

    [Fact]
    public async Task GetChannelPageAsync_AppliesHiddenGroupsForRequestedContentType()
    {
        using var cancellationSource = new CancellationTokenSource();
        var playlistService = new Mock<IPlaylistService>();
        var mediaService = new Mock<IMediaService>();
        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(service => service.Settings).Returns(new AppSettings
        {
            HiddenLiveGroups = ["Hidden live"],
            HiddenMovieGroups = ["Hidden movie"],
            HiddenSeriesGroups = ["Hidden series"]
        });
        playlistService
            .Setup(service => service.GetChannelsFilteredPageAsync(
                42,
                100,
                50,
                "news",
                "English",
                ChannelType.Live,
                true,
                ChannelSortOrder.NameAsc,
                It.Is<List<string>>(groups => groups.SequenceEqual(new[] { "Hidden live" })),
                cancellationSource.Token))
            .ReturnsAsync([
                new Channel
                {
                    Name = "Result",
                    StreamUrl = "http://stream.test/result",
                    Type = ChannelType.Live
                }
            ]);
        var service = new ContentQueryService(
            playlistService.Object,
            mediaService.Object,
            settingsService.Object,
            Mock.Of<Microsoft.EntityFrameworkCore.IDbContextFactory<Noctra.Data.AppDbContext>>());

        var result = await service.GetChannelPageAsync(new ContentPageRequest(
            PlaylistId: 42,
            Skip: 100,
            Take: 50,
            SearchText: "news",
            Group: "English",
            Type: ChannelType.Live,
            OnlyFavorites: true,
            SortOrder: ChannelSortOrder.NameAsc), cancellationSource.Token);

        Assert.Single(result);
        playlistService.VerifyAll();
    }
}
