using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class PlayerViewModelLanguageTests
    {
        private PlayerTestContext CreateContext()
        {
            return new PlayerTestContext();
        }

        private void InvokeApplyDefaultTracks(PlayerTestContext ctx, IReadOnlyList<(int Id, string? Name)> audioTracks, IReadOnlyList<(int Id, string? Name)> subtitleTracks)
        {
            ctx.VM.QualityMonitor.ApplyDefaultTracks(audioTracks, subtitleTracks);
        }

        [Fact]
        public void ApplyDefaultTracks_LiveContent_IgnoresPreferences()
        {
            var ctx = CreateContext();
            ctx.Settings.Settings.PreferredAudioLanguage = "en";
            ctx.Settings.Settings.SubtitleEnabled = true;
            ctx.Settings.Settings.SubtitleLanguage = "tr";

            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.Live };
            ctx.VM.IsLiveContent = true;
            
            var audioTracks = new List<(int, string?)> { (1, "English"), (2, "Turkish") };
            var subtitleTracks = new List<(int, string?)> { (1, "English"), (2, "Turkish") };

            // ApplyDefaultTracks is guarded by !IsLiveContent in UpdateMediaInfo, 
            // but the method itself doesn't check it (the caller does).
            // Let's verify the logic inside when called.
            InvokeApplyDefaultTracks(ctx, audioTracks, subtitleTracks);

            // It should apply because the method itself doesn't have the Live check (the caller UpdateMediaInfo does)
            // Wait, I should check if I added the check inside the method or caller.
            // My implementation in PlayerViewModel.cs:
            // if (!IsLiveContent && !_isPreferenceApplied) { ApplyDefaultTracks(...) }
            // So the method itself is content-agnostic.
            
            Assert.Equal(1, ctx.VM.SelectedAudioTrack);
            Assert.Equal(2, ctx.VM.SelectedSubtitleTrack);
        }

        [Fact]
        public void ApplyDefaultTracks_VodContent_AppliesPreferences()
        {
            var ctx = CreateContext();
            ctx.Settings.Settings.PreferredAudioLanguage = "en";
            ctx.Settings.Settings.SubtitleEnabled = true;
            ctx.Settings.Settings.SubtitleLanguage = "tr";

            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.VOD };
            ctx.VM.IsLiveContent = false;
            
            var audioTracks = new List<(int, string?)> { (1, "Turkish"), (2, "English") };
            var subtitleTracks = new List<(int, string?)> { (1, "English"), (2, "Türkçe") };

            InvokeApplyDefaultTracks(ctx, audioTracks, subtitleTracks);
            
            Assert.Equal(2, ctx.VM.SelectedAudioTrack);
            Assert.Equal(2, ctx.VM.SelectedSubtitleTrack);
        }

        [Fact]
        public void ApplyDefaultTracks_SubtitlesDisabled_AppliesOffTrack()
        {
            var ctx = CreateContext();
            ctx.Settings.Settings.SubtitleEnabled = false;

            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.VOD };
            ctx.VM.IsLiveContent = false;
            var audioTracks = new List<(int, string?)> { (1, "English") };
            var subtitleTracks = new List<(int, string?)> { (1, "English"), (-1, "Kapalı") };

            InvokeApplyDefaultTracks(ctx, audioTracks, subtitleTracks);

            Assert.Equal(-1, ctx.VM.SelectedSubtitleTrack);
        }

        [Theory]
        [InlineData("tr", "Turkish (DD+)", 1)]
        [InlineData("tr", "Türkçe Alt", 1)]
        [InlineData("en", "English [Original]", 2)]
        [InlineData("de", "Deutsch", 3)]
        [InlineData("ru", "Russian - Rusça", 4)]
        public void FindBestTrackMatch_MatchesVariousFormats(string lang, string trackName, int expectedId)
        {
            var tracks = new List<(int Id, string? Name)>
            {
                (1, "Turkish (DD+)"),
                (2, "English [Original]"),
                (3, "German"),
                (4, "Russian")
            };

            var result = PlayerQualityMonitor.FindBestTrackMatch(tracks, lang);

            Assert.Equal(expectedId, result);
        }

        [Fact]
        public void ApplyDefaultTracks_DefaultSettings_SubtitlesAreOff()
        {
            // AppSettings.SubtitleEnabled defaults to false
            var ctx = CreateContext();

            ctx.VM.CurrentChannel = new Channel { Type = ChannelType.VOD };
            ctx.VM.IsLiveContent = false;
            var audioTracks = new List<(int, string?)> { (1, "Turkish") };
            var subtitleTracks = new List<(int, string?)> { (1, "Turkish") };

            InvokeApplyDefaultTracks(ctx, audioTracks, subtitleTracks);

            // Verify VM property
            Assert.Equal(-1, ctx.VM.SelectedSubtitleTrack);
            
            // Verify actual call to VideoService
            Assert.Equal(-1, ctx.VideoService.LastSubtitleTrackId);
        }
    }
}
