using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Noctra.Models;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class PlayerSleepTimerTests
    {
        [Fact]
        public void SleepTimer_InitialState_IsOff()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(PlayerViewModel.SleepTimerOption.Off, ctx.VM.SleepTimerMode);
            Assert.False(ctx.VM.IsSleepTimerActive);
            Assert.Equal(string.Empty, ctx.VM.SleepTimerCountdown);
        }

        [Fact]
        public async Task SleepTimer_SetMinutes15_WhenPremium_StartsTimer()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.SetSleepTimerCommand.Execute(PlayerViewModel.SleepTimerOption.Minutes15);

            Assert.Equal(PlayerViewModel.SleepTimerOption.Minutes15, ctx.VM.SleepTimerMode);
            Assert.True(ctx.VM.IsSleepTimerActive);
            
            // Wait a bit for the background Task.Run to update the string
            await Task.Delay(100);
            Assert.NotEmpty(ctx.VM.SleepTimerCountdown);
        }

        [Fact]
        public void SleepTimer_SetMinutes_WhenNotPremium_StaysOff()
        {
            var ctx = new PlayerTestContext(isPremium: false);
            ctx.VM.SetSleepTimerCommand.Execute(PlayerViewModel.SleepTimerOption.Minutes15);

            Assert.Equal(PlayerViewModel.SleepTimerOption.Off, ctx.VM.SleepTimerMode);
            Assert.False(ctx.VM.IsSleepTimerActive);
        }

        [Fact]
        public void SleepTimer_SetOnLiveContent_ShowsMessageAndStaysOff()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.Live };
            // Ensure IsLiveContent is true (it is an ObservableProperty set via _isLiveContent)
            // In the actual VM, _isLiveContent is updated when CurrentChannel changes.
            // Let's manually set it if needed or check if the VM does it.
            
            // Re-check PlayerViewModel.cs for how _isLiveContent is set.
            // Based on earlier grep, it's an ObservableProperty.
            
            ctx.VM.SetSleepTimerCommand.Execute(PlayerViewModel.SleepTimerOption.Minutes15);

            Assert.Equal(PlayerViewModel.SleepTimerOption.Off, ctx.VM.SleepTimerMode);
        }

        [Fact]
        public async Task SleepTimer_Expiration_StopsVideo()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            await ctx.VideoService.PlayAsync("test.m3u8");
            Assert.True(ctx.VideoService.IsPlaying);

            // We can't easily wait 15 minutes in a unit test.
            // But we can trigger the private TriggerSleepShutdown via reflection to verify it works.
            var mi = typeof(PlayerViewModel).GetMethod("TriggerSleepShutdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mi.Invoke(ctx.VM, null);

            Assert.False(ctx.VideoService.IsPlaying);
            Assert.Equal(PlayerViewModel.SleepTimerOption.Off, ctx.VM.SleepTimerMode);
        }

        [Fact]
        public void SleepTimer_Cancel_ResetsState()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.SetSleepTimerCommand.Execute(PlayerViewModel.SleepTimerOption.Minutes30);
            Assert.True(ctx.VM.IsSleepTimerActive);

            ctx.VM.CancelSleepTimerCommand.Execute(null);

            Assert.Equal(PlayerViewModel.SleepTimerOption.Off, ctx.VM.SleepTimerMode);
            Assert.False(ctx.VM.IsSleepTimerActive);
            Assert.Equal(string.Empty, ctx.VM.SleepTimerCountdown);
        }

        [Fact]
        public void SleepTimer_DynamicLabels_ChangeBasedOnContentType()
        {
            var ctx = new PlayerTestContext();
            
            // Scenario 1: Series
            ctx.VM.IsSeriesContent = true;
            Assert.Equal("Bu Bölüm Bitince", ctx.VM.EndOfContentText);
            Assert.Contains("Bölüm", ctx.VM.EndOfContentDescription);

            // Scenario 2: Movie (VOD)
            ctx.VM.IsSeriesContent = false;
            Assert.Equal("Bu Film Bitince", ctx.VM.EndOfContentText);
            Assert.Contains("Film", ctx.VM.EndOfContentDescription);
        }

        [Fact]
        public async Task SleepTimer_EndOfEpisode_TriggersShutdownOnPlaybackEnded()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.IsSeriesContent = true;
            ctx.VM.SetSleepTimerCommand.Execute(PlayerViewModel.SleepTimerOption.EndOfEpisode);
            
            Assert.Equal(PlayerViewModel.SleepTimerOption.EndOfEpisode, ctx.VM.SleepTimerMode);
            
            // Simulate PlaybackEnded
            ctx.VideoService.SimulatePlayingChanged(true);
            ctx.VideoService.SimulatePlaybackEnded();
            
            // The logic has a 1500ms delay:
            // _ = Task.Delay(1500).ContinueWith(_ => _dispatcherService.BeginInvoke(TriggerSleepShutdown));
            
            await Task.Delay(2000); // Wait for the delay and invocation
            
            Assert.False(ctx.VideoService.IsPlaying);
            Assert.Equal(PlayerViewModel.SleepTimerOption.Off, ctx.VM.SleepTimerMode);
        }
    }
}
