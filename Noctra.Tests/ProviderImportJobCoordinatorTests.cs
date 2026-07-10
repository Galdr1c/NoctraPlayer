using Moq;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class ProviderImportJobCoordinatorTests
{
    private readonly Mock<IImportJobService> _importJobService = new();

    [Fact]
    public async Task StartAsync_CreatesJobAndReturnsInitialStatus()
    {
        var coordinator = new ProviderImportJobCoordinator(_importJobService.Object, logger: null);
        var profile = new Profile { Id = 4, Name = "Owner" };
        var playlist = new Playlist { Id = 9, Name = "Provider" };

        _importJobService
            .Setup(service => service.StartAsync(
                ImportJobKind.Xtream,
                4,
                9,
                "Provider",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportJob { Id = 12, ProfileId = 4, PlaylistId = 9 });

        var result = await coordinator.StartAsync(
            ImportJobKind.Xtream,
            profile,
            playlist,
            "Discovering categories");

        Assert.NotNull(result.Job);
        Assert.Equal(12, result.Job.Id);
        Assert.NotNull(result.Status);
        Assert.True(result.Status.Value.HasActiveImportJob);
        Assert.Equal("Discovering categories", result.Status.Value.Stage);
        Assert.Equal(0, result.Status.Value.LiveCount);
        Assert.Equal(0, result.Status.Value.VodCount);
        Assert.Equal(0, result.Status.Value.SeriesCount);
    }

    [Fact]
    public async Task ReportProgressAsync_FromChannels_PersistsCountsAndReturnsStatus()
    {
        var coordinator = new ProviderImportJobCoordinator(_importJobService.Object, logger: null);
        var importJob = new ImportJob { Id = 22 };

        var status = await coordinator.ReportProgressAsync(
            importJob,
            "Movies",
            new[]
            {
                new Channel { Type = ChannelType.Live },
                new Channel { Type = ChannelType.VOD },
                new Channel { Type = ChannelType.Series },
                new Channel { Type = ChannelType.Series }
            },
            failedCategoryCount: 1);

        _importJobService.Verify(service => service.ReportProgressAsync(
            22,
            "Movies",
            1,
            1,
            2,
            1,
            It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(status);
        Assert.Equal("Movies", status.Value.Stage);
        Assert.Equal(1, status.Value.LiveCount);
        Assert.Equal(1, status.Value.VodCount);
        Assert.Equal(2, status.Value.SeriesCount);
        Assert.Equal(1, status.Value.FailedCategoryCount);
    }
}
