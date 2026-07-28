using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    // ─── Fakes ────────────────────────────────────────────────────────────────────

    internal sealed class SyncDispatcher : IDispatcherService
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
        public Task InvokeAsync(Func<Task> func) => func();
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
    }

    internal sealed class FakeVideoPlayerService : IVideoPlayerService
    {
        public string? CurrentUrl { get; private set; }
        public StreamQualityInfo? StreamQuality => null;
        public bool IsPlaying { get; private set; }
        public PlaybackState State => IsPlaying ? PlaybackState.Playing : PlaybackState.Stopped;
        public bool HasLoadedMedia => CurrentUrl != null;
        public long CurrentTimeMilliseconds => (long)(Position * 1000);
        public double Position { get; set; }
        public double BufferedPosition { get; set; }
        public double Duration { get; set; } = 3600;
        public float PlaybackRate { get; set; } = 1f;
        public int Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public IReadOnlyList<(int Id, string? Name)> AudioTracks => Array.Empty<(int, string?)>();
        public IReadOnlyList<(int Id, string? Name)> SubtitleTracks => Array.Empty<(int, string?)>();
        public PlaybackMediaMetadata? LastMetadata { get; private set; }
        public PlaybackMediaMetadata? MetadataAtPlay { get; private set; }

        public event EventHandler? PlayerReady;
        public event EventHandler<bool>? PlayingChanged;
        public event EventHandler<double>? PositionChanged;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StreamQualityInfo>? QualityDetected;
        public event EventHandler<int>? VolumeChanged;
        public event EventHandler<float>? BufferingChanged;
        public event EventHandler<string?>? SubtitleTextChanged;

        public Task PlayAsync(string url, double startTimeSeconds = 0)
        {
            MetadataAtPlay = LastMetadata;
            CurrentUrl = url;
            IsPlaying = true;
            PlayingChanged?.Invoke(this, true);
            return Task.CompletedTask;
        }

        public Task ReinitializeAsync() => Task.CompletedTask;
        public void UpdateMediaMetadata(PlaybackMediaMetadata metadata) => LastMetadata = metadata;

        public Task HardSeekAsync(double seconds) { Position = seconds; return Task.CompletedTask; }
        public void Pause() { IsPlaying = false; PlayingChanged?.Invoke(this, false); }
        public void Resume() { IsPlaying = true; PlayingChanged?.Invoke(this, true); }
        public void Stop() { IsPlaying = false; CurrentUrl = null; }
        public Task EndSessionAsync(CancellationToken cancellationToken = default) { Stop(); return Task.CompletedTask; }
        public int LastAudioTrackId { get; private set; } = -2;
        public int LastSubtitleTrackId { get; private set; } = -2;

        public void SetAudioTrack(int trackId) { LastAudioTrackId = trackId; }
        public void SetSubtitleTrack(int trackId) { LastSubtitleTrackId = trackId; }
        public void SeekToTime(long milliseconds) => Position = milliseconds / 1000.0;
        public void PlayLoadedMedia() => Resume();
        public void SetVideoLayout(string? aspectRatio, string? cropGeometry) { }
        public void Dispose() { }

        public void SimulatePositionChanged(double pos) => PositionChanged?.Invoke(this, pos);
        public void SimulatePlayingChanged(bool playing) { IsPlaying = playing; PlayingChanged?.Invoke(this, playing); }
        public void SimulateError(string msg) => ErrorOccurred?.Invoke(this, msg);
        public void SimulatePlaybackEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        public void SimulateVolumeChanged(int vol) => VolumeChanged?.Invoke(this, vol);
    }

    internal sealed class FakeEpgService : IEpgService
    {
        public bool IsLoaded => false;
        public DateTime? LastUpdated => null;
        public string? LastError => null;

        public Task<EpgProgram?> GetCurrentProgramAsync(Channel channel) => Task.FromResult<EpgProgram?>(null);
        public Task<int> LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1, IProgress<EpgProgressInfo>? progress = null, bool clearBeforeSave = false, IDictionary<string, string>? headers = null) => Task.FromResult(0);
        public Task ClearEpgAsync() => Task.CompletedTask;
        public Task VacuumAsync() => Task.CompletedTask;
        public Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to) => Task.FromResult(new List<EpgProgram>());
        public Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5) => Task.FromResult(new List<EpgProgram>());
        public Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId) => Task.FromResult(new List<EpgProgram>());
        public void ClearLastError() { }
        public Task<int> GetTotalProgramCountAsync() => Task.FromResult(0);
        public Task<int> GetDistinctChannelCountAsync() => Task.FromResult(0);
        public Task<Dictionary<int, EpgProgram?>> GetCurrentProgramsAsync(IEnumerable<Channel> channels) => Task.FromResult(new Dictionary<int, EpgProgram?>());
        public Task<Dictionary<string, List<EpgProgram>>> GetProgramsBulkAsync(IEnumerable<string> channelIds, DateTime fromLocal, DateTime toLocal) => Task.FromResult(new Dictionary<string, List<EpgProgram>>());
        public Task<DateTime?> GetMaxProgramEndTimeAsync() => Task.FromResult<DateTime?>(null);
    }

    internal sealed class FakeMetadataService : IMetadataService
    {
        public Task EnrichChannelAsync(Channel channel) => Task.CompletedTask;
        public Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null, string? languageCode = null, CancellationToken cancellationToken = default) => Task.FromResult<ChannelMetadata?>(null);
        public Task<List<string>> GetGenresAsync(List<int> genreIds, string? languageCode = null, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public void ClearCache() { }
        public Task<TmdbDetail?> FetchSeriesDetailsAsync(int tmdbId, string? languageCode = null, CancellationToken cancellationToken = default) => Task.FromResult<TmdbDetail?>(null);
        public Task<TmdbSeasonDetail?> FetchSeasonDetailsAsync(int tmdbId, int seasonNumber, string? languageCode = null, CancellationToken cancellationToken = default) => Task.FromResult<TmdbSeasonDetail?>(null);
        public Task<ChannelMetadata?> SearchSeriesAsync(string searchQuery, string? languageCode = null, CancellationToken cancellationToken = default) => Task.FromResult<ChannelMetadata?>(null);
        public void ApplyHeuristics(TmdbDetail details, ChannelMetadata metadata, string languageCode, string? contextTitle) { }
    }

    internal sealed class FakeMediaService : IMediaService
    {
        public event Action<int>? OnAggregationCompleted;
        public void RaiseAggregationCompleted(int playlistId) => OnAggregationCompleted?.Invoke(playlistId);
        public Task AggregateContentAsync(int playlistId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<Series>> GetSeriesAsync(int playlistId, CancellationToken cancellationToken = default) => Task.FromResult(new List<Series>());
        public Task<List<Series>> GetSeriesListAsync(int playlistId, CancellationToken cancellationToken = default) => Task.FromResult(new List<Series>());
        public Task UpdateSeriesAsync(Series series, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class FakeContentDownloadService : IContentDownloadService
    {
        public event EventHandler? DownloadsChanged;
        public event EventHandler<DownloadItem>? DownloadCompleted;

        public Task<DownloadContentResult> QueueDownloadAsync(DownloadContentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new DownloadContentResult(false, false, "stub"));
        public Task<string> ResolvePlayableUrlAsync(string streamUrl, CancellationToken cancellationToken = default) => Task.FromResult(streamUrl);
        public Task CleanupPlaybackCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<DownloadItem>> GetDownloadsAsync(int profileId, CancellationToken cancellationToken = default) => Task.FromResult(new List<DownloadItem>());
        public Task CancelDownloadAsync(int downloadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseDownloadAsync(int downloadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeDownloadAsync(int downloadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FailActiveDownloadsForProfileAsync(int profileId, string errorMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProfileDownloadsAsync(int profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class FakeNetworkService : INetworkService
    {
        public string CurrentNetworkStatus => "Online";
        public event EventHandler<string>? NetworkStatusChanged;
        public void SimulateStatusChanged(string status) => NetworkStatusChanged?.Invoke(this, status);
    }

    internal sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new AppSettings { DefaultVolume = 100 };
        public event Action? SettingsChanged;
        public Task SaveAsync() => Task.CompletedTask;
        public Task LoadAsync() => Task.CompletedTask;
        public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
        public Task LoadProfileSettingsAsync(int profileId) => Task.CompletedTask;
        public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) => Task.FromResult<AppSettings?>(Settings);
        public void ResetToDefaults() { }
    }

    internal sealed class FakeLicenseService : ILicenseService
    {
        public bool IsPremium { get; set; }
        public bool CanUpgradeToPremium => !IsPremium;
        public bool IsEditionLockedPremium => false;
        public SubscriptionTier CurrentTier => IsPremium ? SubscriptionTier.Premium : SubscriptionTier.Free;
        public bool IsFeatureAvailable(string feature) => IsPremium;
        public void SetTierForTesting(SubscriptionTier tier) { IsPremium = tier == SubscriptionTier.Premium; }
        public event Action? SubscriptionChanged;

        public void ActivatePremium() { IsPremium = true; SubscriptionChanged?.Invoke(); }
        public void DeactivatePremium() { IsPremium = false; SubscriptionChanged?.Invoke(); }
        public string GetPriceText() => "0";
        public SubscriptionInfo GetCurrentSubscription() => new() { Tier = CurrentTier };
        public bool IsWithinLimit(string limit, int count) => true;
        public int GetLimit(string limit) => int.MaxValue;
        public Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier) => Task.FromResult(true);
        public Task RefreshSubscriptionStatusAsync() => Task.CompletedTask;
    }

    internal sealed class FakeWatchHistoryService : IWatchHistoryService
    {
        public List<(int ProfileId, int? ChannelId, int? EpisodeId, TimeSpan Position, bool Completed, TimeSpan? Duration)> Calls = new();

        public Task TrackWatchAsync(int profileId, int? channelId, int? episodeId, TimeSpan position,
            bool completed = false, TimeSpan? duration = null, TimeSpan? incrementDelta = null, bool allowReset = false, CancellationToken ct = default)
        {
            Calls.Add((profileId, channelId, episodeId, position, completed, duration));
            return Task.CompletedTask;
        }

        public Task<List<WatchHistory>> GetHistoryAsync(int profileId, int skip = 0, int take = 50, CancellationToken ct = default) => Task.FromResult(new List<WatchHistory>());
        public Task DeleteProfileHistoryAsync(int profileId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default) => Task.FromResult<WatchHistory?>(null);
        public Task CleanupOlderThanDaysAsync(int profileId, int days, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFromHistoryAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class FakeStalkerPortalService : IStalkerPortalService
    {
        public Task<bool> AuthenticateAsync(string portalUrl, string macAddress, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<List<Channel>> GetChannelsAsync(string portalUrl, string macAddress, bool includeVod = true, CancellationToken cancellationToken = default) => Task.FromResult(new List<Channel>());
        public Task<List<StalkerCategory>> GetCategoriesAsync(string portalUrl, string macAddress, CancellationToken cancellationToken = default) => Task.FromResult(new List<StalkerCategory>());
        public Task<List<Channel>> GetChannelsByCategoryAsync(string portalUrl, string macAddress, string categoryId, string categoryType, CancellationToken cancellationToken = default) => Task.FromResult(new List<Channel>());
        public Task GetChannelsProgressiveAsync(string portalUrl, string macAddress, bool includeVod, Func<List<StalkerCategory>, Action<string>, Task<List<StalkerCategory>>> onCategoriesDiscovered, Func<List<Channel>, StalkerCategory, Task> onCategoryLoaded, IProgress<StalkerLoadProgress>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string GetEpgUrl(string portalUrl) => string.Empty;
        public Task<StalkerSeriesInfo?> GetSeriesInfoAsync(string portalUrl, string macAddress, string seriesId, CancellationToken cancellationToken = default) => Task.FromResult<StalkerSeriesInfo?>(null);
        public Task<string?> CreateLinkAsync(string portalUrl, string macAddress, string type, string cmd, string episodeNum = "0", CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    internal sealed class PlayerTestContext
    {
        public FakeVideoPlayerService VideoService { get; } = new();
        public FakeLicenseService License { get; } = new();
        public FakeSettingsService Settings { get; } = new();
        public FakeWatchHistoryService WatchHistory { get; } = new();
        public PlayerViewModel VM { get; }

        public PlayerTestContext(bool isPremium = false)
        {
            License.IsPremium = isPremium;

            VM = new PlayerViewModel(
                VideoService,
                new FakeEpgService(),
                new FakeMetadataService(),
                new FakeMediaService(),
                new FakeContentDownloadService(),
                new FakeNetworkService(),
                new SyncDispatcher(),
                Settings,
                License,
                new LocalizationService(),
                null!,  // MainViewModel — not needed for these tests
                WatchHistory,
                new FakeStalkerPortalService());

            VM.CurrentChannel = new Channel { Id = 1, Name = "Test Channel", StreamUrl = "http://test.ts", Type = ChannelType.VOD };
        }

        public double ParseSkipSeconds(object? parameter) =>
            VM.PlaybackController.ParseSkipSeconds(parameter);

        public double ClampSeekPosition(double pos) =>
            VM.PlaybackController.ClampSeekPosition(pos);

        public bool IsEpisodeCompleted(double duration, double position) =>
            PlayerEpisodeNavigator.IsEpisodeCompleted(duration, position);

        public void ApplySkipDelta(double delta) =>
            VM.PlaybackController.ApplySkipDelta(delta);

        public static string FormatSkipToast(double seconds)
        {
            var sign = seconds >= 0 ? "+" : "-";
            var abs = TimeSpan.FromSeconds(Math.Abs(seconds));
            var formatted = abs.TotalHours >= 1
                ? abs.ToString(@"h\:mm\:ss")
                : abs.ToString(@"m\:ss");
            return $"{sign}{formatted}";
        }
    }

    public class PlayerViewModelControlsTests
    {
        private static List<EpgProgram> MergeAdjacentSameTitlePrograms(IEnumerable<EpgProgram> programs)
        {
            var mi = typeof(PlayerViewModel).GetMethod("MergeAdjacentSameTitlePrograms",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (List<EpgProgram>)mi.Invoke(null, new object[] { programs })!;
        }

        [Fact]
        public void ToggleLock_ShowsUnlockAffordanceImmediately()
        {
            var ctx = new PlayerTestContext();

            ctx.VM.ToggleLockCommand.Execute(null);

            Assert.True(ctx.VM.IsLocked);
            Assert.True(ctx.VM.IsLockIndicatorVisible);
        }

        [Fact]
        public async Task ShowLockIndicatorBriefly_RestartsVisibilityWindow()
        {
            var ctx = new PlayerTestContext();

            ctx.VM.ShowLockIndicatorBriefly();
            await Task.Delay(2000);
            ctx.VM.ShowLockIndicatorBriefly();
            await Task.Delay(700);

            Assert.True(ctx.VM.IsLockIndicatorVisible);
        }

        [Fact]
        public async Task PlayChannelAsync_PublishesMediaMetadataBeforePlayback()
        {
            var ctx = new PlayerTestContext();
            var channel = new Channel
            {
                Id = 42,
                Name = "Noctra News",
                StreamUrl = "https://example.test/live.m3u8",
                LogoUrl = "https://example.test/logo.png",
                GroupTitle = "News",
                Type = ChannelType.Live
            };

            await ctx.VM.PlaybackController.PlayChannelAsync(channel);

            Assert.Equal("Noctra News", ctx.VideoService.MetadataAtPlay?.Title);
            Assert.Equal("News", ctx.VideoService.MetadataAtPlay?.Subtitle);
            Assert.Equal("https://example.test/logo.png", ctx.VideoService.MetadataAtPlay?.ArtworkUrl);
        }

        [Fact]
        public void ToggleMute_WhenNotMuted_SetsMutedAndZeroesVolume()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = 75;

            ctx.VM.ToggleMuteCommand.Execute(null);

            Assert.True(ctx.VM.IsMuted);
            Assert.Equal(0, ctx.VM.Volume);
        }

        [Fact]
        public void ToggleMute_WhenMuted_UnmutesAndRestoresPreviousVolume()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = 60;
            ctx.VM.ToggleMuteCommand.Execute(null);
            ctx.VM.ToggleMuteCommand.Execute(null);

            Assert.False(ctx.VM.IsMuted);
            Assert.Equal(60, ctx.VM.Volume);
        }

        [Fact]
        public void MergeAdjacentSameTitlePrograms_PreviousCurrentNextSameTitle_MergesIntoOne()
        {
            var start = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);
            var programs = new[]
            {
                new EpgProgram { ChannelId = "cartoon", Title = "Kral Sakir", StartTime = start, EndTime = start.AddMinutes(30) },
                new EpgProgram { ChannelId = "cartoon", Title = "  Kral   Sakir ", StartTime = start.AddMinutes(30), EndTime = start.AddMinutes(60) },
                new EpgProgram { ChannelId = "cartoon", Title = "KRAL SAKIR", StartTime = start.AddMinutes(60), EndTime = start.AddMinutes(90) }
            };

            var merged = MergeAdjacentSameTitlePrograms(programs);

            Assert.Single(merged);
            Assert.Equal(start, merged[0].StartTime);
            Assert.Equal(start.AddMinutes(90), merged[0].EndTime);
            Assert.Equal("Kral Sakir", merged[0].Title);
        }

        [Fact]
        public void MergeAdjacentSameTitlePrograms_DifferentTitle_DoesNotMerge()
        {
            var start = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);
            var programs = new[]
            {
                new EpgProgram { ChannelId = "cartoon", Title = "Kral Sakir", StartTime = start, EndTime = start.AddMinutes(30) },
                new EpgProgram { ChannelId = "cartoon", Title = "Gumball", StartTime = start.AddMinutes(30), EndTime = start.AddMinutes(60) }
            };

            var merged = MergeAdjacentSameTitlePrograms(programs);

            Assert.Equal(2, merged.Count);
        }

        [Fact]
        public void MergeAdjacentSameTitlePrograms_SameTitleWithRealGap_DoesNotMerge()
        {
            var start = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);
            var programs = new[]
            {
                new EpgProgram { ChannelId = "cartoon", Title = "Kral Sakir", StartTime = start, EndTime = start.AddMinutes(30) },
                new EpgProgram { ChannelId = "cartoon", Title = "Kral Sakir", StartTime = start.AddMinutes(35), EndTime = start.AddMinutes(60) }
            };

            var merged = MergeAdjacentSameTitlePrograms(programs);

            Assert.Equal(2, merged.Count);
        }

        [Fact]
        public void ToggleMute_WhenVolWasZeroBeforeMute_RestoresTo100()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = 0;
            ctx.VM.IsMuted = true;

            ctx.VM.ToggleMuteCommand.Execute(null);

            Assert.False(ctx.VM.IsMuted);
            Assert.Equal(100, ctx.VM.Volume);
        }

        [Fact]
        public void Volume_SetToZero_AutoSetsMutedTrue()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = 50;

            ctx.VM.Volume = 0;

            Assert.True(ctx.VM.IsMuted);
        }

        [Fact]
        public void Volume_SetAboveZero_WhenMuted_AutoClearsMuted()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = 0;

            ctx.VM.Volume = 30;

            Assert.False(ctx.VM.IsMuted);
        }

        [Fact]
        public void Volume_Change_PropagatedToVideoService()
        {
            var ctx = new PlayerTestContext();

            ctx.VM.Volume = 55;

            Assert.Equal(55, ctx.VideoService.Volume);
        }

        [Fact]
        public void MobileCompactControls_HideWhenAnyBottomSheetPanelIsOpen()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.IsVisible = true;

            Assert.True(ctx.VM.AreMobileControlsVisible);
            Assert.False(ctx.VM.IsMobileDetailPanelOpen);
            Assert.True(ctx.VM.IsMobileCompactControlsVisible);

            ctx.VM.IsSleepTimerPanelOpen = true;
            Assert.True(ctx.VM.IsMobileDetailPanelOpen);
            Assert.False(ctx.VM.IsMobileCompactControlsVisible);

            ctx.VM.IsSleepTimerPanelOpen = false;
            ctx.VM.IsResumeDialogVisible = true;
            Assert.True(ctx.VM.IsMobileDetailPanelOpen);
            Assert.False(ctx.VM.IsMobileCompactControlsVisible);

            ctx.VM.IsResumeDialogVisible = false;
            ctx.VM.IsNextEpisodePromptVisible = true;
            Assert.True(ctx.VM.IsMobileDetailPanelOpen);
            Assert.False(ctx.VM.IsMobileCompactControlsVisible);

            ctx.VM.IsNextEpisodePromptVisible = false;
            Assert.False(ctx.VM.IsMobileDetailPanelOpen);
            Assert.True(ctx.VM.IsMobileCompactControlsVisible);
        }

        [Fact]
        public void MobilePanelState_TracksActivePanelAndExcludesEpgFromBottomSheet()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.IsVisible = true;

            Assert.Equal(PlayerViewModel.MobilePanelState.None, ctx.VM.ActiveMobilePanelState);
            Assert.False(ctx.VM.IsPanelOpen);
            Assert.True(ctx.VM.IsMobileCompactControlsVisible);

            ctx.VM.IsAudioSettingsOpen = true;

            Assert.Equal(PlayerViewModel.MobilePanelState.Audio, ctx.VM.ActiveMobilePanelState);
            Assert.True(ctx.VM.IsPanelOpen);
            Assert.True(ctx.VM.IsMobileDetailPanelOpen);
            Assert.False(ctx.VM.IsMobileCompactControlsVisible);

            ctx.VM.IsAudioSettingsOpen = false;
            ctx.VM.IsEpgPanelOpen = true;

            Assert.Equal(PlayerViewModel.MobilePanelState.Epg, ctx.VM.ActiveMobilePanelState);
            Assert.True(ctx.VM.IsPanelOpen);
            Assert.False(ctx.VM.AreMobileControlsVisible);
            Assert.False(ctx.VM.IsMobileDetailPanelOpen);
            Assert.False(ctx.VM.IsMobileCompactControlsVisible);
        }

        [Fact]
        public void MobileControlAliases_FollowLegacyVisibilityAndPanelState()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            ctx.VM.IsVisible = true;
            ctx.VM.IsPiPMode = false;
            ctx.VM.IsEpgPanelOpen = false;

            Assert.True(ctx.VM.IsPlayerVisible);
            Assert.True(ctx.VM.IsControlsVisible);
            Assert.True(ctx.VM.IsTopOverlayVisible);
            Assert.True(ctx.VM.IsBottomControlsVisible);

            ctx.VM.IsInfoPanelOpen = true;

            Assert.True(ctx.VM.IsControlsVisible);
            Assert.True(ctx.VM.IsTopOverlayVisible);
            Assert.False(ctx.VM.IsBottomControlsVisible);

            ctx.VM.IsInfoPanelOpen = false;
            ctx.VM.IsPiPMode = true;

            Assert.True(ctx.VM.IsPlayerVisible);
            Assert.False(ctx.VM.IsControlsVisible);
            Assert.False(ctx.VM.IsTopOverlayVisible);
            Assert.False(ctx.VM.IsBottomControlsVisible);
        }

        [Fact]
        public void VolumeChanged_FromService_UpdatesVmVolume()
        {
            var ctx = new PlayerTestContext();

            ctx.VideoService.SimulateVolumeChanged(42);

            Assert.Equal(42, ctx.VM.Volume);
        }

        [Fact]
        public void VolumeChanged_FromService_WhenMuted_DoesNotUnmute()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsMuted = true;

            ctx.VideoService.SimulateVolumeChanged(80);

            Assert.True(ctx.VM.IsMuted);
            Assert.True(ctx.VideoService.IsMuted);
            Assert.Equal(80, ctx.VM.Volume);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(50, false)]
        [InlineData(100, false)]
        public void Volume_BoundaryValues_MutedStateCorrect(int vol, bool expectedMuted)
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = vol;
            Assert.Equal(expectedMuted, ctx.VM.IsMuted);
        }

        [Fact]
        public void Seek_VodContent_UpdatesPositionAndPositionText()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;

            ctx.VM.SeekCommand.Execute(1800.0);

            Assert.Equal(1800, ctx.VM.Position, precision: 1);
            Assert.Equal("00:30:00", ctx.VM.PositionText);
        }

        [Fact]
        public void Seek_UpdatesRemainingTime()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;

            ctx.VM.SeekCommand.Execute(3000.0);

            Assert.Equal("-00:10:00", ctx.VM.RemainingTime);
        }

        [Fact]
        public void Seek_LiveContent_PositionUnchanged()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = true;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 100;

            ctx.VM.SeekCommand.Execute(1800.0);

            Assert.Equal(100, ctx.VM.Position);
        }

        [Fact]
        public void Seek_BelowZero_ClampsToZero()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;

            ctx.VM.SeekCommand.Execute(-100.0);

            Assert.Equal(0, ctx.VM.Position);
        }

        [Fact]
        public void Seek_AboveDuration_ClampsToDuration()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;

            ctx.VM.SeekCommand.Execute(9999.0);

            Assert.True(ctx.VM.Position <= 3600);
        }

        [Fact]
        public void Seek_ResetsSkipCarryState()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 500;

            ctx.ApplySkipDelta(10);
            ctx.ApplySkipDelta(10);

            ctx.VM.SeekCommand.Execute(1000.0);

            ctx.ApplySkipDelta(10);
            Assert.True(ctx.VM.Position >= 1005 && ctx.VM.Position <= 1020);
        }

        [Fact]
        public void SkipForward_VodContent_IncreasesPosition()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 100;

            ctx.VM.SkipForwardCommand.Execute(10);

            Assert.True(ctx.VM.Position > 100);
        }

        [Fact]
        public void SkipBackward_VodContent_DecreasesPosition()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 100;

            ctx.VM.SkipBackwardCommand.Execute(10);

            Assert.True(ctx.VM.Position < 100);
        }

        [Fact]
        public void SkipForward_LiveContent_IsIgnored()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = true;
            ctx.VM.Position = 50;

            ctx.VM.SkipForwardCommand.Execute(10);

            Assert.Equal(50, ctx.VM.Position);
        }

        [Fact]
        public void SkipBackward_AtBeginning_ClampsToZero()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 5;

            ctx.VM.SkipBackwardCommand.Execute(30);

            Assert.Equal(0, ctx.VM.Position);
        }

        [Fact]
        public void SkipForward_AtEnd_DoesNotExceedDuration()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 3595;

            ctx.VM.SkipForwardCommand.Execute(30);

            Assert.True(ctx.VM.Position <= 3600);
        }

        [Fact]
        public void MultipleSkipForward_AccumulatesWithinCarryWindow()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 100;

            ctx.VM.SkipForwardCommand.Execute(10);
            ctx.VM.SkipForwardCommand.Execute(10);
            ctx.VM.SkipForwardCommand.Execute(10);

            Assert.True(ctx.VM.Position >= 125 && ctx.VM.Position <= 135);
        }

        [Fact]
        public void ParseSkipSeconds_Null_Returns10()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(10, ctx.ParseSkipSeconds(null));
        }

        [Fact]
        public void ParseSkipSeconds_NegativeInt_ReturnsAbsoluteValue()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(30, ctx.ParseSkipSeconds(-30));
        }

        [Fact]
        public void ParseSkipSeconds_Double_ReturnsAbsoluteValue()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(15.5, ctx.ParseSkipSeconds(15.5), precision: 2);
        }

        [Fact]
        public void ParseSkipSeconds_ValidString_ParsesCorrectly()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(45, ctx.ParseSkipSeconds("45"), precision: 0);
        }

        [Fact]
        public void ParseSkipSeconds_InvalidString_Returns10()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(10, ctx.ParseSkipSeconds("abc"));
        }

        [Theory]
        [InlineData(null, 10)]
        [InlineData(5, 5)]
        [InlineData(10, 10)]
        [InlineData(30, 30)]
        [InlineData("15", 15)]
        public void ParseSkipSeconds_Theory(object? input, double expected)
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(expected, ctx.ParseSkipSeconds(input), precision: 0);
        }

        [Fact]
        public void FormatSkipToast_PositiveSeconds_HasPlusSign()
        {
            Assert.StartsWith("+", PlayerTestContext.FormatSkipToast(10));
        }

        [Fact]
        public void FormatSkipToast_NegativeSeconds_HasMinusSign()
        {
            Assert.StartsWith("-", PlayerTestContext.FormatSkipToast(-10));
        }

        [Fact]
        public void FormatSkipToast_10Seconds_FormatsCorrectly()
        {
            Assert.Equal("+0:10", PlayerTestContext.FormatSkipToast(10));
        }

        [Fact]
        public void FormatSkipToast_90Seconds_Formats1Min30()
        {
            Assert.Equal("+1:30", PlayerTestContext.FormatSkipToast(90));
        }

        [Fact]
        public void FormatSkipToast_Over1Hour_IncludesHours()
        {
            var result = PlayerTestContext.FormatSkipToast(3661);
            Assert.StartsWith("+1:", result);
        }

        [Fact]
        public void FormatSkipToast_Zero_FormatsAsZeroMin()
        {
            var result = PlayerTestContext.FormatSkipToast(0);
            Assert.StartsWith("+", result);
            Assert.Contains("0:00", result);
        }

        [Fact]
        public void PositionText_OnSeek_FormattedAsHHMMSS()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 7200;

            ctx.VM.SeekCommand.Execute(3661.0);

            Assert.Equal("01:01:01", ctx.VM.PositionText);
        }

        [Fact]
        public void RemainingTime_OnSeek_HasMinusPrefix()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;

            ctx.VM.SeekCommand.Execute(3000.0);

            Assert.StartsWith("-", ctx.VM.RemainingTime);
        }

        [Fact]
        public void RemainingTime_AtZeroPosition_ShowsFullDuration()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM._lastKnownValidPosition = 10.0;

            ctx.VM.SeekCommand.Execute(0.0);

            Assert.Equal("-01:00:00", ctx.VM.RemainingTime);
        }

        [Fact]
        public void PositionText_DefaultState_ShowsZero()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal("00:00:00", ctx.VM.PositionText);
        }

        [Fact]
        public void DurationText_DefaultState_ShowsZero()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal("00:00:00", ctx.VM.DurationText);
        }

        [Fact]
        public void IsEpisodeCompleted_At90Percent_ReturnsTrue()
        {
            var ctx = new PlayerTestContext();
            Assert.True(ctx.IsEpisodeCompleted(3600, 3240));
        }

        [Fact]
        public void IsEpisodeCompleted_Under90Percent_ReturnsFalse()
        {
            var ctx = new PlayerTestContext();
            Assert.False(ctx.IsEpisodeCompleted(3600, 3236));
        }

        [Fact]
        public void IsEpisodeCompleted_Last3Minutes_ReturnsTrue()
        {
            var ctx = new PlayerTestContext();
            Assert.True(ctx.IsEpisodeCompleted(7200, 7020));
        }

        [Fact]
        public void IsEpisodeCompleted_800SecondsRemaining_ReturnsFalse()
        {
            var ctx = new PlayerTestContext();
            // 2 saatlik film (7200sn), 800sn (13dk) kaldı.
            // 6400 / 7200 = 88.8% (< 90%) VE 800sn kaldı (> 180sn) -> False
            Assert.False(ctx.IsEpisodeCompleted(7200, 6400));
        }

        [Fact]
        public void IsEpisodeCompleted_DurationZero_ReturnsFalse()
        {
            var ctx = new PlayerTestContext();
            Assert.False(ctx.IsEpisodeCompleted(0, 100));
        }

        [Fact]
        public void IsEpisodeCompleted_PositionZero_ReturnsFalse()
        {
            var ctx = new PlayerTestContext();
            Assert.False(ctx.IsEpisodeCompleted(3600, 0));
        }

        [Fact]
        public void IsEpisodeCompleted_ShortVideo_PercentThresholdApplied()
        {
            var ctx = new PlayerTestContext();
            Assert.True(ctx.IsEpisodeCompleted(60, 54));
        }

        [Theory]
        [InlineData(3600, 3600, true)]
        [InlineData(3600, 3241, true)]
        [InlineData(3600, 1800, false)]
        [InlineData(3600, 0, false)]
        [InlineData(0, 0, false)]
        [InlineData(0, 100, false)]
        public void IsEpisodeCompleted_Theory(double duration, double position, bool expected)
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(expected, ctx.IsEpisodeCompleted(duration, position));
        }

        [Fact]
        public async Task ShowResumeDialog_SetsIsResumeDialogVisibleTrue()
        {
            var ctx = new PlayerTestContext();
            var task = ctx.VM.ShowResumeDialogAsync(600);

            Assert.True(ctx.VM.IsResumeDialogVisible);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        [Fact]
        public async Task ShowResumeDialog_FormatsPositionText()
        {
            var ctx = new PlayerTestContext();
            var task = ctx.VM.ShowResumeDialogAsync(3661);

            Assert.Equal("01:01:01", ctx.VM.ResumePositionText);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        [Fact]
        public async Task ShowResumeDialog_WhenVisible_RestoresPositionTextIfCleared()
        {
            var ctx = new PlayerTestContext();
            var task = ctx.VM.ShowResumeDialogAsync(1277);

            ctx.VM.ResumePositionText = string.Empty;

            Assert.True(ctx.VM.IsResumeDialogVisible);
            Assert.Equal("00:21:17", ctx.VM.ResumePositionText);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        [Fact]
        public async Task ResumeFromPosition_ResolvesTaskTrue_HidesDialog()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            var task = ctx.VM.ShowResumeDialogAsync(600);

            ctx.VM.ResumeFromPositionCommand.Execute(null);

            var result = await task;
            Assert.True(result);
            Assert.False(ctx.VM.IsResumeDialogVisible);
        }

        [Fact]
        public async Task StartFromBeginning_ResolvesTaskFalse_HidesDialog()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            var task = ctx.VM.ShowResumeDialogAsync(600);

            ctx.VM.StartFromBeginningCommand.Execute(null);

            var result = await task;
            Assert.False(result);
            Assert.False(ctx.VM.IsResumeDialogVisible);
        }

        [Fact]
        public async Task ShowResumeDialog_PremiumUser_IsPremiumResumeTrue()
        {
            var ctx = new PlayerTestContext(isPremium: true);
            var task = ctx.VM.ShowResumeDialogAsync(600);

            Assert.True(ctx.VM.IsPremiumResume);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        [Fact]
        public async Task ShowResumeDialog_FreeUser_IsPremiumResumeFalse()
        {
            var ctx = new PlayerTestContext(isPremium: false);
            var task = ctx.VM.ShowResumeDialogAsync(600);

            Assert.False(ctx.VM.IsPremiumResume);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        private static void InvokePrepareForContentLoading(PlayerViewModel vm)
        {
            var mi = typeof(PlayerViewModel).GetMethod("PrepareForContentLoading",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(mi);
            mi!.Invoke(vm, null);
        }

        [Fact]
        public void PrepareForContentLoading_ResetsPositionAndTexts()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Position = 500;

            InvokePrepareForContentLoading(ctx.VM);

            Assert.Equal(0, ctx.VM.Position);
            Assert.Equal("00:00:00", ctx.VM.PositionText);
            Assert.Equal(0, ctx.VM.Duration);
            Assert.Equal("00:00:00", ctx.VM.DurationText);
        }

        [Fact]
        public void PrepareForContentLoading_SetsIsBufferingTrue()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;

            InvokePrepareForContentLoading(ctx.VM);

            Assert.True(ctx.VM.IsBuffering);
        }

        [Fact]
        public void PrepareForContentLoading_SetsIsPlayingFalse()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsPlaying = true;
            ctx.VM.IsLiveContent = false;

            InvokePrepareForContentLoading(ctx.VM);

            Assert.False(ctx.VM.IsPlaying);
        }

        [Fact]
        public void PrepareForContentLoading_CancelsActiveResumeDialog()
        {
            var ctx = new PlayerTestContext();
            var _ = ctx.VM.ShowResumeDialogAsync(600);

            InvokePrepareForContentLoading(ctx.VM);

            Assert.False(ctx.VM.IsResumeDialogVisible);
        }

        [Fact]
        public void Seek_LiveContent_DoesNotChangePosition()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = true;
            ctx.VM.Position = 200;

            ctx.VM.SeekCommand.Execute(1000.0);

            Assert.Equal(200, ctx.VM.Position);
        }

        [Fact]
        public void SkipForward_LiveContent_DoesNotChangePosition()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = true;
            ctx.VM.Position = 300;

            ctx.VM.SkipForwardCommand.Execute(30);

            Assert.Equal(300, ctx.VM.Position);
        }

        [Fact]
        public void SkipBackward_LiveContent_DoesNotChangePosition()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = true;
            ctx.VM.Position = 300;

            ctx.VM.SkipBackwardCommand.Execute(30);

            Assert.Equal(300, ctx.VM.Position);
        }

        [Fact]
        public void ClampSeekPosition_Negative_ReturnsZero()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Duration = 3600;
            Assert.Equal(0, ctx.ClampSeekPosition(-100));
        }

        [Fact]
        public void ClampSeekPosition_ExceedsDuration_ReturnsDuration()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Duration = 3600;
            Assert.True(ctx.ClampSeekPosition(5000) <= 3600);
        }

        [Fact]
        public void ClampSeekPosition_WithinRange_Unchanged()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Duration = 3600;
            Assert.Equal(1800, ctx.ClampSeekPosition(1800), precision: 1);
        }

        [Fact]
        public void ClampSeekPosition_Zero_ReturnsZero()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Duration = 3600;
            Assert.Equal(0, ctx.ClampSeekPosition(0));
        }

        [Fact]
        public void SkipForward_CarryWindow_AccumulatesFromPreviousTarget()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 100;

            ctx.ApplySkipDelta(10);
            var afterFirst = ctx.VM.Position;

            ctx.ApplySkipDelta(10);

            Assert.True(ctx.VM.Position > afterFirst);
        }

        [Fact]
        public void SkipBackward_ChainedSkips_DoNotGoNegative()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 15;

            ctx.ApplySkipDelta(-10);
            ctx.ApplySkipDelta(-10);
            ctx.ApplySkipDelta(-10);

            Assert.Equal(0, ctx.VM.Position, precision: 1);
        }

        [Fact]
        public void ToggleMute_OddNumberOfTimes_EndsMuted()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Volume = 80;

            for (int i = 0; i < 5; i++)
                ctx.VM.ToggleMuteCommand.Execute(null);

            Assert.True(ctx.VM.IsMuted);
            Assert.Equal(0, ctx.VM.Volume);
        }

        [Fact]
        public void SetVolumeTo100_WhenMuted_ClearsMutedState()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.ToggleMuteCommand.Execute(null);

            ctx.VM.Volume = 100;

            Assert.False(ctx.VM.IsMuted);
        }

        [Fact]
        public void IsMutedChanged_PropagatedToVideoService()
        {
            var ctx = new PlayerTestContext();

            ctx.VM.IsMuted = true;

            Assert.True(ctx.VideoService.IsMuted);
        }

        [Fact]
        public void PositionChanged_Event_WhenNotTransitioning_UpdatesPosition()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VideoService.SimulatePlayingChanged(true);

            ctx.VideoService.SimulatePositionChanged(500);

            Assert.Equal(500, ctx.VM.Position, precision: 1);
        }

        [Fact]
        public void PositionChanged_Event_FormatsPositionTextCorrectly()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsLiveContent = false;
            ctx.VideoService.SimulatePlayingChanged(true);

            ctx.VideoService.SimulatePositionChanged(3661);

            Assert.Equal("01:01:01", ctx.VM.PositionText);
        }

        [Fact]
        public void PositionChanged_Event_PublishesBufferedPositionClampedToDuration()
        {
            var ctx = new PlayerTestContext();
            var bufferedPosition = typeof(PlayerViewModel).GetProperty("BufferedPosition");
            Assert.NotNull(bufferedPosition);

            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VideoService.BufferedPosition = 4200;

            ctx.VideoService.SimulatePositionChanged(600);

            Assert.Equal(3600d, (double)bufferedPosition!.GetValue(ctx.VM)!);
        }

        [Fact]
        public void PositionChanged_Event_ClearsBufferedPositionForLivePlayback()
        {
            var ctx = new PlayerTestContext();
            var bufferedPosition = typeof(PlayerViewModel).GetProperty("BufferedPosition");
            Assert.NotNull(bufferedPosition);

            bufferedPosition!.SetValue(ctx.VM, 500d);
            ctx.VM.IsLiveContent = true;
            ctx.VideoService.BufferedPosition = 900;

            ctx.VideoService.SimulatePositionChanged(600);

            Assert.Equal(0d, (double)bufferedPosition.GetValue(ctx.VM)!);
        }

        [Fact]
        public void CurrentChannelChanged_ResetsBufferedPosition()
        {
            var ctx = new PlayerTestContext();
            var bufferedPosition = typeof(PlayerViewModel).GetProperty("BufferedPosition");
            Assert.NotNull(bufferedPosition);

            bufferedPosition!.SetValue(ctx.VM, 500d);
            ctx.VM.CurrentChannel = new Channel
            {
                Id = 2,
                Name = "Next",
                StreamUrl = "https://example.test/next.mp4",
                Type = ChannelType.VOD
            };

            Assert.Equal(0d, (double)bufferedPosition.GetValue(ctx.VM)!);
        }

        [Fact]
        public void ErrorOccurred_Event_SetsIsBufferingTrue()
        {
            var ctx = new PlayerTestContext();

            ctx.VideoService.SimulateError("Bağlantı hatası");

            Assert.True(ctx.VM.IsBuffering);
        }

        [Fact]
        public void ErrorOccurred_Event_SetsNonEmptyConnectionStatus()
        {
            var ctx = new PlayerTestContext();

            ctx.VideoService.SimulateError("Stream URL geçersiz");

            Assert.False(string.IsNullOrWhiteSpace(ctx.VM.ConnectionStatus));
        }

        [Fact]
        public void PlayingChanged_TrueEvent_SetsIsPlayingTrue()
        {
            var ctx = new PlayerTestContext();

            ctx.VideoService.SimulatePlayingChanged(true);

            Assert.True(ctx.VM.IsPlaying);
        }

        [Fact]
        public void PlayingChanged_TrueEvent_CompletesBufferingAndAllowsOverlayAutoHide()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.IsBuffering = true;
            ctx.VM.BufferingProgress = 0;

            ctx.VideoService.SimulatePlayingChanged(true);

            Assert.False(ctx.VM.IsBuffering);
            Assert.Equal(100f, ctx.VM.BufferingProgress);
            Assert.True(ctx.VM.OverlayManager.CanAutoHideOverlay());
        }

        [Fact]
        public void PlayingChanged_FalseEvent_SetsIsPlayingFalse()
        {
            var ctx = new PlayerTestContext();
            ctx.VideoService.SimulatePlayingChanged(true);

            ctx.VideoService.SimulatePlayingChanged(false);

            Assert.False(ctx.VM.IsPlaying);
        }

        [Fact]
        public void SetResumePosition_ReflectsInDialogPositionText()
        {
            var ctx = new PlayerTestContext();

            ctx.VM.SetResumePosition(1200);

            var task = ctx.VM.ShowResumeDialogAsync(1200);
            Assert.Equal("00:20:00", ctx.VM.ResumePositionText);
            ctx.VM.CancelResumeDialog();
        }

        [Fact]
        public void BufferingProgress_DefaultIsZero()
        {
            var ctx = new PlayerTestContext();
            Assert.Equal(0, ctx.VM.BufferingProgress);
        }

        [Fact]
        public void ConnectionStatus_InitiallyNotNull()
        {
            var ctx = new PlayerTestContext();
            Assert.NotNull(ctx.VM.ConnectionStatus);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var ctx = new PlayerTestContext();
            var ex = Record.Exception(() => ctx.VM.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var ctx = new PlayerTestContext();
            ctx.VM.Dispose();
            var ex = Record.Exception(() => ctx.VM.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void NextEpisodePrompt_VisibilityBehavior_OnSeeking()
        {
            var ctx = new PlayerTestContext();
            var ep = new Episode { CreditsStartSec = 3500 };
            var nextEp = new Episode();
            ctx.VM.Duration = 3600;
            ctx.VM.IsLiveContent = false;
            
            ctx.VM.SetCurrentEpisode(ep, nextEp);

            // 1. Initial state
            Assert.False(ctx.VM.IsNextEpisodePromptVisible);

            // 2. Seek to credits zone (trigger)
            ctx.VM.SeekCommand.Execute(3550.0);
            
            // Should be visible
            Assert.True(ctx.VM.IsNextEpisodePromptVisible);

            // 3. Seek backward out of credits zone
            ctx.VM.SeekCommand.Execute(3400.0);
            
            // Should hide
            Assert.False(ctx.VM.IsNextEpisodePromptVisible);

            // 4. Seek to credits zone again
            ctx.VM.SeekCommand.Execute(3580.0);

            // Should show again
            Assert.True(ctx.VM.IsNextEpisodePromptVisible);
        }

        [Fact]
        public void NextEpisodePrompt_NotShown_WhenNoNextEpisode()
        {
            var ctx = new PlayerTestContext();
            var ep = new Episode { CreditsStartSec = 3500 };
            ctx.VM.Duration = 3600;
            ctx.VM.IsLiveContent = false;
            
            // nextEpisode is passed as null
            ctx.VM.SetCurrentEpisode(ep, null);

            // Seek to credits zone
            ctx.VM.SeekCommand.Execute(3550.0);
            
            // Should NOT be visible because NextEpisode is null
            Assert.False(ctx.VM.IsNextEpisodePromptVisible);
        }

        [Fact]
        public void NextEpisodePrompt_ClickNextEpisode_TriggersNextEpisodeEvent()
        {
            var ctx = new PlayerTestContext();
            var ep = new Episode { CreditsStartSec = 3500 };
            var nextEp = new Episode();
            ctx.VM.Duration = 3600;
            ctx.VM.IsLiveContent = false;
            
            ctx.VM.SetCurrentEpisode(ep, nextEp);
            
            bool eventTriggered = false;
            Episode? triggeredEpisode = null;
            ctx.VM.NextEpisodeRequested += (s, e) => {
                eventTriggered = true;
                triggeredEpisode = e;
            };

            ctx.VM.SeekCommand.Execute(3550.0);
            Assert.True(ctx.VM.IsNextEpisodePromptVisible);

            // Click play next episode
            ctx.VM.PlayNextEpisodeCommand.Execute(null);

            Assert.True(eventTriggered);
            Assert.Equal(nextEp, triggeredEpisode);
            Assert.False(ctx.VM.IsNextEpisodePromptVisible); // Should hide after click
        }
    }
}
