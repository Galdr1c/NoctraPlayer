using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class EpgEnrichmentScenariosTests
    {
        private readonly Mock<IPlaylistService> _playlistServiceMock = new();
        private readonly Mock<IEpgService> _epgServiceMock = new();
        private readonly Mock<ISettingsService> _settingsServiceMock = new();
        private readonly Mock<IDispatcherService> _dispatcherServiceMock = new();
        private readonly Mock<IDbContextFactory<AppDbContext>> _contextFactoryMock = new();
        private readonly Mock<ILogger<MainViewModel>> _loggerMock = new();
        private readonly Mock<IWatchHistoryService> _historyServiceMock = new();
        private readonly Mock<IMediaService> _mediaServiceMock = new();
        private readonly Mock<ITmdbSyncService> _tmdbServiceMock = new();
        private readonly Mock<IContentDownloadService> _downloadServiceMock = new();
        private readonly Mock<IMetadataService> _metadataServiceMock = new();
        private readonly Mock<IDialogService> _dialogServiceMock = new();
        private readonly Mock<IChannelService> _channelServiceMock = new();
        private readonly Mock<IXtreamCodesService> _xtreamServiceMock = new();
        private readonly Mock<IStalkerPortalService> _stalkerServiceMock = new();
        private readonly Mock<ISecurityService> _securityServiceMock = new();
        private readonly Mock<ILicenseService> _licenseServiceMock = new();
        private readonly Mock<IUpdateService> _updateServiceMock = new();

        public EpgEnrichmentScenariosTests()
        {
            // Simple dispatcher that executes immediately
            _dispatcherServiceMock.Setup(d => d.Invoke(It.IsAny<Action>()))
                .Callback<Action>(a => a());
            
            _settingsServiceMock.Setup(s => s.Settings).Returns(new AppSettings());
        }

        private MainViewModel CreateViewModel()
        {
            return new MainViewModel(
                _settingsServiceMock.Object,
                _downloadServiceMock.Object,
                _metadataServiceMock.Object,
                _dispatcherServiceMock.Object,
                _dialogServiceMock.Object,
                null!, // WatermarkViewModel
                _channelServiceMock.Object,
                _mediaServiceMock.Object,
                _epgServiceMock.Object,
                _playlistServiceMock.Object,
                _historyServiceMock.Object,
                _xtreamServiceMock.Object,
                _stalkerServiceMock.Object,
                null!, // LanguageDetectionService
                null!, // EpgSourceResolver
                _contextFactoryMock.Object,
                _securityServiceMock.Object,
                null!, // HttpClient
                _tmdbServiceMock.Object,
                _licenseServiceMock.Object,
                _updateServiceMock.Object,
                _loggerMock.Object
            );
        }

        private async Task InvokeEnrichChannelsWithEpgAsync(MainViewModel vm, IEnumerable<Channel> channels)
        {
            var method = typeof(MainViewModel).GetMethod("EnrichChannelsWithEpgAsync", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task)method.Invoke(vm, new object[] { channels })!;
            await task;
        }

        [Fact]
        public async Task EnrichChannelsWithEpgAsync_ShouldUpdateTitleAndProgress_WhenEpgExists()
        {
            // Arrange
            var vm = CreateViewModel();
            var channel = new Channel { Id = 1, Name = "Test Channel", Type = ChannelType.Live };
            var channels = new List<Channel> { channel };

            var epgProgram = new EpgProgram 
            { 
                Title = "Morning News", 
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                EndTime = DateTime.UtcNow.AddMinutes(30)
            };

            var epgResults = new Dictionary<int, EpgProgram?> { { 1, epgProgram } };
            _epgServiceMock.Setup(s => s.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()))
                .ReturnsAsync(epgResults);

            // Act
            await InvokeEnrichChannelsWithEpgAsync(vm, channels);

            // Assert
            Assert.Equal("Morning News", channel.CurrentProgramTitle);
            Assert.True(channel.EpgProgress > 0);
        }

        [Fact]
        public async Task EnrichChannelsWithEpgAsync_ShouldClearTitleAndProgress_WhenEpgDoesNotExist()
        {
            // Arrange
            var vm = CreateViewModel();
            var channel = new Channel 
            { 
                Id = 1, 
                Name = "Test Channel", 
                Type = ChannelType.Live,
                CurrentProgramTitle = "Old News",
                EpgProgress = 50
            };
            var channels = new List<Channel> { channel };

            var epgResults = new Dictionary<int, EpgProgram?> { { 1, null } };
            _epgServiceMock.Setup(s => s.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()))
                .ReturnsAsync(epgResults);

            // Act
            await InvokeEnrichChannelsWithEpgAsync(vm, channels);

            // Assert
            Assert.Null(channel.CurrentProgramTitle);
            Assert.Equal(0, channel.EpgProgress);
        }

        [Fact]
        public async Task EnrichChannelsWithEpgAsync_ShouldSkipNonLiveChannels()
        {
            // Arrange
            var vm = CreateViewModel();
            var liveChannel = new Channel { Id = 1, Name = "Live", Type = ChannelType.Live };
            var vodChannel = new Channel { Id = 2, Name = "Movie", Type = ChannelType.VOD };
            var channels = new List<Channel> { liveChannel, vodChannel };

            _epgServiceMock.Setup(s => s.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()))
                .ReturnsAsync(new Dictionary<int, EpgProgram?>());

            // Act
            await InvokeEnrichChannelsWithEpgAsync(vm, channels);

            // Assert
            // Verify that GetCurrentProgramsAsync was called with only 1 channel (the live one)
            _epgServiceMock.Verify(s => s.GetCurrentProgramsAsync(
                It.Is<IEnumerable<Channel>>(c => c.Count() == 1 && c.First().Id == 1)), Times.Once);
        }

        [Fact]
        public async Task EnrichChannelsWithEpgAsync_ShouldHandleEmptyListGracefully()
        {
            // Arrange
            var vm = CreateViewModel();
            var channels = new List<Channel>();

            // Act
            await InvokeEnrichChannelsWithEpgAsync(vm, channels);

            // Assert
            _epgServiceMock.Verify(s => s.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()), Times.Never);
        }

        [Fact]
        public async Task EnrichChannelsWithEpgAsync_MixedResults_ShouldUpdateCorrectly()
        {
            // Arrange
            var vm = CreateViewModel();
            var ch1 = new Channel { Id = 1, Type = ChannelType.Live };
            var ch2 = new Channel { Id = 2, Type = ChannelType.Live };
            var channels = new List<Channel> { ch1, ch2 };

            var prog1 = new EpgProgram { Title = "Show 1", StartTime = DateTime.UtcNow.AddMinutes(-10), EndTime = DateTime.UtcNow.AddMinutes(10) };
            
            var epgResults = new Dictionary<int, EpgProgram?> 
            { 
                { 1, prog1 },
                { 2, null }
            };

            _epgServiceMock.Setup(s => s.GetCurrentProgramsAsync(It.IsAny<IEnumerable<Channel>>()))
                .ReturnsAsync(epgResults);

            // Act
            await InvokeEnrichChannelsWithEpgAsync(vm, channels);

            // Assert
            Assert.Equal("Show 1", ch1.CurrentProgramTitle);
            Assert.Null(ch2.CurrentProgramTitle);
        }
    }
}
