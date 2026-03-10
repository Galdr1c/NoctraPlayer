using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class PlayerViewModelNextEpisodeTests
    {
        private PlayerTestContext CreateContext(bool autoPlayNext)
        {
            var ctx = new PlayerTestContext();
            ctx.Settings.Settings.AutoPlayNext = autoPlayNext;
            return ctx;
        }

        [Fact]
        public void CheckIntroCreditsPosition_NearEnd_ShowsPrompt()
        {
            var ctx = CreateContext(false);
            var ep = new Episode { EpisodeNumber = 1, Name = "Ep 1", Duration = TimeSpan.FromMinutes(60) };
            var nextEp = new Episode { EpisodeNumber = 2, Name = "Ep 2", Duration = TimeSpan.FromMinutes(60) };
            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.Series };
            ctx.VM.Duration = 3600;
            ctx.VM.SetCurrentEpisode(ep, nextEp);

            // 60 minutes = 3600s
            // Tail ratio is 0.06 of 3600s = 216s (maxed to 180s)
            // Trigger is 3600 - 180 = 3420s
            var mi = typeof(PlayerViewModel).GetMethod("CheckIntroCreditsPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            mi!.Invoke(ctx.VM, new object[] { 3421.0 });

            Assert.True(ctx.VM.IsNextEpisodePromptVisible);
        }

        [Fact]
        public void CheckIntroCreditsPosition_SeekBack_HidesPrompt()
        {
            var ctx = CreateContext(false);
            var ep = new Episode { EpisodeNumber = 1, Name = "Ep 1", Duration = TimeSpan.FromMinutes(60) };
            var nextEp = new Episode { EpisodeNumber = 2, Name = "Ep 2", Duration = TimeSpan.FromMinutes(60) };
            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.Series };
            ctx.VM.Duration = 3600;
            ctx.VM.SetCurrentEpisode(ep, nextEp);

            var mi = typeof(PlayerViewModel).GetMethod("CheckIntroCreditsPosition", BindingFlags.NonPublic | BindingFlags.Instance);

            // Go near end
            mi!.Invoke(ctx.VM, new object[] { 3450.0 });
            Assert.True(ctx.VM.IsNextEpisodePromptVisible);

            // Seek back before the threshold (3420 - 3 = 3417)
            mi!.Invoke(ctx.VM, new object[] { 3000.0 });
            Assert.False(ctx.VM.IsNextEpisodePromptVisible);
        }

        [Fact]
        public void CheckIntroCreditsPosition_NoNextEpisode_DoesNotShowPrompt()
        {
            var ctx = CreateContext(false);
            var ep = new Episode { EpisodeNumber = 1, Name = "Ep 1", Duration = TimeSpan.FromMinutes(60) };
            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.Series };
            ctx.VM.Duration = 3600;
            ctx.VM.SetCurrentEpisode(ep, null); // No next episode

            var mi = typeof(PlayerViewModel).GetMethod("CheckIntroCreditsPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            mi!.Invoke(ctx.VM, new object[] { 3450.0 });

            Assert.False(ctx.VM.IsNextEpisodePromptVisible);
        }

        [Fact]
        public async Task AutoPlayNext_WaitsAndExecutes()
        {
            var ctx = CreateContext(true); // AutoPlayNext enabled
            var ep = new Episode { EpisodeNumber = 1, Name = "Ep 1", Duration = TimeSpan.FromMinutes(60) };
            var nextEp = new Episode { EpisodeNumber = 2, Name = "Ep 2", Duration = TimeSpan.FromMinutes(60) };
            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.Series };
            ctx.VM.Duration = 3600;
            ctx.VM.SetCurrentEpisode(ep, nextEp);

            bool nextEpisodeRequested = false;
            ctx.VM.NextEpisodeRequested += (sender, e) => nextEpisodeRequested = true;

            var mi = typeof(PlayerViewModel).GetMethod("CheckIntroCreditsPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            mi!.Invoke(ctx.VM, new object[] { 3450.0 });

            Assert.True(ctx.VM.IsNextEpisodePromptVisible);

            // Give it time to execute Task.Delay(3000)
            await Task.Delay(3500);

            Assert.True(nextEpisodeRequested);
        }
    }
}
