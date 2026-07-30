using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Noctra.Avalonia.Controls;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

public sealed class DesktopResumeAndCardActionsTests
{
    [Fact]
    public void ContinueWatching_OffersStateAwareActionsAndHistoryRemoval()
    {
        var channel = new Channel
        {
            Type = ChannelType.VOD,
            IsFavorite = false,
            IsInMyList = false
        };
        var request = new DesktopCardActionRequest(
            channel,
            DesktopCardGridKind.ContinueWatching,
            DesktopCardPresentationMode.Default);

        var actions = DesktopCardActions.BuildActions(request);

        Assert.Equal(
            new[]
            {
                DesktopCardActionKind.AddToMyList,
                DesktopCardActionKind.ToggleFavorite,
                DesktopCardActionKind.RemoveFromHistory
            },
            actions);
    }

    [Fact]
    public void DefaultCard_UsesRemovalStateForMyListAndFavorite()
    {
        var channel = new Channel
        {
            Type = ChannelType.VOD,
            IsFavorite = true,
            IsInMyList = true
        };
        var request = new DesktopCardActionRequest(
            channel,
            DesktopCardGridKind.Vod,
            DesktopCardPresentationMode.Default);

        var actions = DesktopCardActions.BuildActions(request);

        Assert.Equal(
            new[]
            {
                DesktopCardActionKind.RemoveFromMyList,
                DesktopCardActionKind.ToggleFavorite
            },
            actions);
        Assert.True(DesktopCardActions.IsDestructive(
            DesktopCardActionKind.ToggleFavorite,
            channel));
    }

    [Fact]
    public void LiveCard_DoesNotOfferMyListAction()
    {
        var channel = new Channel { Type = ChannelType.Live };
        var request = new DesktopCardActionRequest(
            channel,
            DesktopCardGridKind.Live,
            DesktopCardPresentationMode.Default);

        var actions = DesktopCardActions.BuildActions(request);

        Assert.Single(actions);
        Assert.Equal(DesktopCardActionKind.ToggleFavorite, actions[0]);
    }

    [Fact]
    public async Task ResumeResolver_HistoryFailureFallsBackToRealModelProgress()
    {
        var history = new Mock<IWatchHistoryService>(MockBehavior.Strict);
        history
            .Setup(service => service.GetLatestForMediaAsync(
                7,
                42,
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("history unavailable"));

        var resolver = new PlayerResumeResolver(history.Object);
        var channel = new Channel
        {
            Id = 42,
            Type = ChannelType.VOD,
            StreamUrl = "https://example.test/movie.mp4",
            WatchedPosition = TimeSpan.FromMinutes(10),
            Duration = TimeSpan.FromMinutes(60),
            LastWatched = DateTime.UtcNow
        };

        var result = await resolver.ResolveAsync(7, channel, episode: null);

        Assert.Equal(TimeSpan.FromMinutes(10).TotalSeconds, result);
    }

    [Fact]
    public async Task ResumeResolver_AuthoritativeCompletedEpisodeDoesNotFallBackToChannelHistory()
    {
        var history = new Mock<IWatchHistoryService>(MockBehavior.Strict);
        history
            .Setup(service => service.GetLatestForMediaAsync(
                7,
                null,
                99,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WatchHistory?)null);

        var resolver = new PlayerResumeResolver(history.Object);
        var streamUrl = "https://example.test/series/s01e01.mp4";
        var channel = new Channel
        {
            Id = 42,
            Type = ChannelType.Series,
            StreamUrl = streamUrl,
            WatchedPosition = TimeSpan.FromMinutes(12),
            Duration = TimeSpan.FromMinutes(45),
            LastWatched = DateTime.UtcNow
        };
        var episode = new Episode
        {
            Id = 99,
            StreamUrl = streamUrl,
            WatchedPosition = TimeSpan.FromMinutes(44),
            Duration = TimeSpan.FromMinutes(45),
            LastWatched = DateTime.UtcNow,
            IsCompleted = true
        };

        var result = await resolver.ResolveAsync(7, channel, episode);

        Assert.Null(result);
        history.Verify(x => x.GetLatestForMediaAsync(7, null, 99, It.IsAny<CancellationToken>()), Times.Once);
        history.VerifyNoOtherCalls();
    }
}
