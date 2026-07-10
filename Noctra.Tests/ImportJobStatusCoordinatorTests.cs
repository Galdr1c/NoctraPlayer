using Moq;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class ImportJobStatusCoordinatorTests
{
    [Fact]
    public void CreateProgress_FromChannels_CountsContentTypes()
    {
        var status = ImportJobStatusCoordinator.CreateProgress(
            "Loading category",
            new[]
            {
                new Channel { Type = ChannelType.Live },
                new Channel { Type = ChannelType.VOD },
                new Channel { Type = ChannelType.VOD },
                new Channel { Type = ChannelType.Series }
            },
            failedCategoryCount: 3);

        Assert.True(status.HasActiveImportJob);
        Assert.Equal("Loading category", status.Stage);
        Assert.Equal(1, status.LiveCount);
        Assert.Equal(2, status.VodCount);
        Assert.Equal(1, status.SeriesCount);
        Assert.Equal(3, status.FailedCategoryCount);
    }

    [Fact]
    public async Task RefreshAsync_NoCurrentProfile_DoesNotQueryServiceAndReturnsClearStatus()
    {
        var importJobService = new Mock<IImportJobService>();
        var coordinator = new ImportJobStatusCoordinator(importJobService.Object, logger: null);

        var status = await coordinator.RefreshAsync(currentProfileId: null);

        Assert.False(status.HasActiveImportJob);
        Assert.Equal(string.Empty, status.Stage);
        importJobService.Verify(
            service => service.GetActiveForProfileAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
