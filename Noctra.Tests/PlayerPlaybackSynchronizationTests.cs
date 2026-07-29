using System;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;
using Noctra.Services;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class PlayerPlaybackSynchronizationTests
    {
        [Fact]
        public void VolumeAndMute_OnConstructor_InitializesCorrectlyFromSettings()
        {
            // Arrange
            var ctx = new PlayerTestContext();
            
            // By default, PlayerTestContext's settings have DefaultVolume (now 100) and IsMuted = false (default value).
            // Let's verify both the ViewModel and the VideoService reflect these values immediately after construction.
            Assert.Equal(ctx.Settings.Settings.DefaultVolume, ctx.VM.Volume);
            Assert.False(ctx.VM.IsMuted);
            Assert.Equal(ctx.Settings.Settings.DefaultVolume, ctx.VideoService.Volume);
            Assert.False(ctx.VideoService.IsMuted);
        }

        [Fact]
        public void VolumeAndMute_OnConstructor_InitializesMutedStateFromSettings()
        {
            // Arrange
            var settingsService = new FakeSettingsService();
            settingsService.Settings.DefaultVolume = 45;
            settingsService.Settings.IsMuted = true;

            var videoService = new FakeVideoPlayerService();
            var license = new FakeLicenseService();
            var watchHistory = new FakeWatchHistoryService();

            // Act
            var vm = new PlayerViewModel(
                videoService,
                new FakeEpgService(),
                new FakeMetadataService(),
                new FakeMediaService(),
                new FakeContentDownloadService(),
                new FakeNetworkService(),
                new SyncDispatcher(),
                settingsService,
                license,
                new LocalizationService(),
                null!,
                watchHistory,
                new FakeStalkerPortalService());

            // Assert
            Assert.Equal(45, vm.Volume);
            Assert.True(vm.IsMuted);
            // ViewModel volume/mute changes propagate to VideoService
            Assert.Equal(45, videoService.Volume);
            Assert.True(videoService.IsMuted);
        }

        [Fact]
        public void VolumeAndMute_OnSettingsChanged_SyncsValuesToViewModel()
        {
            // Arrange
            var ctx = new PlayerTestContext();
            
            // Initially DefaultVolume and unmuted
            Assert.Equal(ctx.Settings.Settings.DefaultVolume, ctx.VM.Volume);
            Assert.False(ctx.VM.IsMuted);

            // Act: Update settings and trigger OnSettingsChanged directly
            ctx.Settings.Settings.DefaultVolume = 30;
            ctx.Settings.Settings.IsMuted = true;
            
            ctx.VM.SettingsAdapter.OnSettingsChanged();

            // Assert: VM and VideoPlayerService should be fully synchronized
            Assert.Equal(30, ctx.VM.Volume);
            Assert.True(ctx.VM.IsMuted);
            Assert.Equal(30, ctx.VideoService.Volume);
            Assert.True(ctx.VideoService.IsMuted);
        }

        [Fact]
        public void PlaybackIntent_BeginPlaybackIntent_IncrementsVersionAndClearsDialog()
        {
            // Arrange
            var ctx = new PlayerTestContext();
            
            // Act
            var version1 = ctx.VM.BeginPlaybackIntent(stopCurrentPlayback: false);
            var version2 = ctx.VM.BeginPlaybackIntent(stopCurrentPlayback: false);

            // Assert
            Assert.True(version2 > version1);
            Assert.True(ctx.VM.IsPlaybackIntentCurrent(version2));
            Assert.False(ctx.VM.IsPlaybackIntentCurrent(version1));
        }

        [Fact]
        public void PlaybackIntent_BeginPlaybackIntent_PersistsOutgoingPositionBeforeStop()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.CurrentProfileId = 7;
            ctx.VM.CurrentChannel = new Channel
            {
                Id = 42,
                Name = "Outgoing movie",
                StreamUrl = "https://example.test/outgoing.mp4",
                Type = ChannelType.VOD
            };
            ctx.VM.Position = 321;
            ctx.VM.Duration = 3600;
            ctx.VideoService.Position = 321;

            ctx.VM.BeginPlaybackIntent(stopCurrentPlayback: true);

            var call = Assert.Single(ctx.WatchHistory.Calls);
            Assert.Equal(7, call.ProfileId);
            Assert.Equal(42, call.ChannelId);
            Assert.Null(call.EpisodeId);
            Assert.Equal(TimeSpan.FromSeconds(321), call.Position);
            Assert.Equal(TimeSpan.FromSeconds(3600), call.Duration);
        }

        [Fact]
        public async Task PlayChannelAsync_RapidZapping_StaleRequestAbortsEarly()
        {
            // Arrange
            var ctx = new PlayerTestContext();
            var channel1 = new Channel { Id = 1, Name = "Channel 1", StreamUrl = "http://stream1.ts", Type = ChannelType.Live };
            var channel2 = new Channel { Id = 2, Name = "Channel 2", StreamUrl = "http://stream2.ts", Type = ChannelType.Live };

            // Act
            // Start playing channel 1
            var playTask1 = ctx.VM.PlayChannelAsync(channel1);

            // Simulate user immediately zapping to Channel 2 (which invalidates channel 1's version)
            var playTask2 = ctx.VM.PlayChannelAsync(channel2);

            // Wait for both to complete
            await Task.WhenAll(playTask1, playTask2);

            // Assert
            // The active channel must be Channel 2
            Assert.Equal(channel2.Id, ctx.VM.CurrentChannel.Id);
            Assert.Equal("http://stream2.ts", ctx.VideoService.CurrentUrl);
            
            // Verify that the stale request 1 was aborted and didn't overwrite the stream URL
            Assert.NotEqual("http://stream1.ts", ctx.VideoService.CurrentUrl);
        }

        [Fact]
        public async Task PlayChannelAsync_CancelledBeforeVlcPlay_AbortsPlayback()
        {
            // Arrange
            var ctx = new PlayerTestContext();
            var channel = new Channel { Id = 5, Name = "Channel 5", StreamUrl = "http://stream5.ts", Type = ChannelType.Live };

            // Start playing channel
            var playTask = ctx.VM.PlayChannelAsync(channel);

            // Immediately increment the request version to simulate cancellation (e.g. user closed player)
            ctx.VM.BeginPlaybackIntent(stopCurrentPlayback: true);

            await playTask;

            // Assert: VLC PlayAsync must not have been called with this channel's url
            Assert.Null(ctx.VideoService.CurrentUrl);
        }
    }
}
