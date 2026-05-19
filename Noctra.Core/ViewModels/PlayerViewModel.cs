using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.ViewModels;

/// <summary>
/// Video player view model - refactored to decompose concerns
/// </summary>
public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private const double OverlayAutoHideDelayMs = 5000;
    
    public sealed record TrackOption(int Id, string Name);
    public sealed class SkipOverlayEventArgs : EventArgs
    {
        public SkipOverlayEventArgs(double seconds)
        {
            Seconds = seconds;
        }

        public double Seconds { get; }
    }
    
    public event EventHandler? PiPRequested;
    public event EventHandler? PremiumUpsellRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? NextLiveChannelRequested;
    public event EventHandler? PreviousLiveChannelRequested;
    public event EventHandler<Episode>? NextEpisodeRequested;
    public event EventHandler<Episode>? EpisodeRequested;
    public event EventHandler<Episode>? EpisodeProgressUpdated;
    public event EventHandler<SkipOverlayEventArgs>? SkipOverlayRequested;

    public enum SleepTimerOption { Off, Minutes15, Minutes30, Minutes60, EndOfEpisode }
    public enum FillMode { Fit, Fill, Stretch, Original }

    // ── Controllers / Subclasses (Decomposition Pattern) ────────────────────
    public PlayerPlaybackController PlaybackController { get; }
    public PlayerOverlayManager OverlayManager { get; }
    public PlayerEpisodeNavigator EpisodeNavigator { get; }
    public PlayerStallDetector StallDetector { get; }
    public PlayerQualityMonitor QualityMonitor { get; }
    public PlayerSettingsAdapter SettingsAdapter { get; }

    // ── Sleep Timer ────────────────────────────────────────────────────────
    internal CancellationTokenSource? _sleepCountdownCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSleepTimerActive))]
    [NotifyPropertyChangedFor(nameof(SleepTimerLabel))]
    private SleepTimerOption _sleepTimerMode = SleepTimerOption.Off;

    [ObservableProperty]
    private string _sleepTimerCountdown = string.Empty;

    [ObservableProperty]
    private bool _isSleepTimerPanelOpen;

    public bool IsSleepTimerActive => SleepTimerMode != SleepTimerOption.Off;

    public string SleepTimerLabel => SleepTimerMode switch
    {
        SleepTimerOption.Minutes15 => string.Format(_localizationService.GetString("Player.Sleep.LabelFormat"), 15),
        SleepTimerOption.Minutes30 => string.Format(_localizationService.GetString("Player.Sleep.LabelFormat"), 30),
        SleepTimerOption.Minutes60 => string.Format(_localizationService.GetString("Player.Sleep.LabelFormat"), 60),
        SleepTimerOption.EndOfEpisode => _localizationService.GetString(IsSeriesContent ? "Player.Sleep.EndOfEpisode" : "Player.Sleep.EndOfMovie"),
        _ => _localizationService.GetString("Player.Overlay.SleepTimer.Tooltip")
    };

    public string EndOfContentText => _localizationService.GetString(IsSeriesContent ? "Player.Sleep.EndContent.Episode" : "Player.Sleep.EndContent.Movie");
    public string EndOfContentDescription => _localizationService.GetString(IsSeriesContent ? "Player.Sleep.EndDescription.Episode" : "Player.Sleep.EndDescription.Movie");

    // ── Infrastructure Dependencies ──────────────────────────────────────────
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IEpgService _epgService;
    private readonly IMetadataService _metadataService;
    private readonly IMediaService _mediaService;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly INetworkService _networkService;
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;
    private readonly ILocalizationService _localizationService;
    private readonly MainViewModel _mainViewModel;
    private readonly IWatchHistoryService? _watchHistoryService;
    private readonly IStalkerPortalService _stalkerPortalService;
    private readonly IDispatcherService _dispatcherService;

    // ── State Fields ────────────────────────────────────────────────────────
    internal int _playRequestVersion;
    internal bool _isPreferenceApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiveInfoVisible))]
    [NotifyPropertyChangedFor(nameof(IsSeriesPlotVisible))]
    [NotifyPropertyChangedFor(nameof(IsVodPlotVisible))]
    private bool _isLiveContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeriesPlotVisible))]
    private bool _isSeriesContent;

    public bool IsPremium => _licenseService.IsPremium;

    [ObservableProperty]
    private bool _isDownloadedPlayback;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isPiPMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isPiPControlsForceVisible;

    public bool IsPiPControlsVisible => IsPiPMode && IsPiPControlsForceVisible;

    internal Timer? _unreachableWarningTimer;

    partial void OnIsPiPModeChanged(bool value)
    {
        IsPiPControlsForceVisible = value;
        IsVisible = true;
        RestartAutoHideTimer();
        OnPropertyChanged(nameof(IsPiPControlsVisible));
    }

    [ObservableProperty]
    private string _currentTimeStr = "00:00";

    [ObservableProperty]
    private string _channelName = string.Empty;

    [ObservableProperty]
    private string _channelLogo = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private string _streamInfo = string.Empty;

    [ObservableProperty]
    private double _bufferingProgress;

    [ObservableProperty]
    private bool _isBuffering;

    public bool IsBufferShieldVisible => IsBuffering && (IsLiveContent || !_suppressBufferShieldForSeek);

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _playerLoadingWarningMessage = string.Empty;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private bool _isResizing;

    [ObservableProperty]
    private bool _isAudioSettingsOpen;

    [ObservableProperty]
    private string _networkStatus = "Wi-Fi";

    [ObservableProperty]
    private string _networkIcon = "Wifi";

    [ObservableProperty]
    private string _remainingTime = "-00:00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVodPlotVisible))]
    private Channel? _currentChannel;

    [ObservableProperty]
    private bool _isCurrentChannelFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiveInfoVisible))]
    private EpgProgram? _currentProgram;

    public bool HasCurrentProgramInfo =>
        CurrentProgram != null &&
        !string.IsNullOrWhiteSpace(CurrentProgram.Title) &&
        !string.Equals(CurrentProgram.Title, _localizationService.GetString("Player.Epg.NoInfo"), StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _overlaySecondaryText = string.Empty;

    [ObservableProperty]
    private string _overlayMessage = string.Empty;

    [ObservableProperty]
    private bool _isOverlayMessageVisible;

    internal CancellationTokenSource? _overlayMessageCts;

    public bool IsLiveInfoVisible => IsLiveContent && CurrentProgram != null && !string.IsNullOrWhiteSpace(CurrentProgram.Title);
    public bool IsSeriesPlotVisible => IsSeriesContent && !IsLiveContent && CurrentEpisode != null && !string.IsNullOrWhiteSpace(CurrentEpisode.Plot);
    public bool IsVodPlotVisible => !IsLiveContent && !IsSeriesContent && CurrentChannel != null && !string.IsNullOrWhiteSpace(CurrentChannel.Plot);

    [ObservableProperty]
    private bool _isPlaying;

    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
        {
            _unreachableWarningTimer?.Dispose();
            _unreachableWarningTimer = null;
            PlayerLoadingWarningMessage = string.Empty;
        }
    }

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private FillMode _videoFillMode = FillMode.Fit;

    [ObservableProperty]
    private int _subtitleFontSize = 40;

    [ObservableProperty]
    private int _subtitleBackgroundOpacity = 0;

    [ObservableProperty]
    private int _subtitleMargin = 40;

    private CancellationTokenSource? _subtitleSaveCts;

    partial void OnSubtitleFontSizeChanged(int value) => QueueSubtitleSettingsSave();
    partial void OnSubtitleBackgroundOpacityChanged(int value) => QueueSubtitleSettingsSave();
    partial void OnSubtitleMarginChanged(int value) => QueueSubtitleSettingsSave();

    private void QueueSubtitleSettingsSave()
    {
        if (_settingsService == null) return;

        bool changed = false;
        if (_settingsService.Settings.SubtitleFontSize != SubtitleFontSize)
        {
            _settingsService.Settings.SubtitleFontSize = SubtitleFontSize;
            changed = true;
        }
        if (_settingsService.Settings.SubtitleBackgroundOpacity != SubtitleBackgroundOpacity)
        {
            _settingsService.Settings.SubtitleBackgroundOpacity = SubtitleBackgroundOpacity;
            changed = true;
        }
        if (_settingsService.Settings.SubtitleMargin != SubtitleMargin)
        {
            _settingsService.Settings.SubtitleMargin = SubtitleMargin;
            changed = true;
        }

        if (changed)
        {
            var oldCts = _subtitleSaveCts;
            _subtitleSaveCts = new CancellationTokenSource();
            var token = _subtitleSaveCts.Token;

            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, token);
                    if (!token.IsCancellationRequested)
                    {
                        await _settingsService.SaveAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception) { }
            }, token);
        }
    }

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private string _positionText = "00:00:00";

    [ObservableProperty]
    private string _durationText = "00:00:00";

    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    private bool _showControls = true;

    [ObservableProperty]
    private ObservableCollection<TrackOption> _audioTracks = new();

    [ObservableProperty]
    private ObservableCollection<TrackOption> _subtitleTracks = new();

    [ObservableProperty]
    private int _selectedAudioTrack = -1;

    [ObservableProperty]
    private int _selectedSubtitleTrack = -1;

    [ObservableProperty]
    private bool _isQualitySettingsOpen;

    [ObservableProperty]
    private bool _isEpisodesPanelOpen;

    [ObservableProperty]
    private StreamQualityInfo? _streamQuality;

    public string QualityResolutionText => StreamQuality?.Height > 0
        ? StreamQuality.ResolutionLabel
        : _localizationService.GetString("Common.Unknown");

    public string QualityFpsText => StreamQuality?.Fps > 0
        ? $"{StreamQuality.Fps} FPS"
        : _localizationService.GetString("Common.Unknown");

    public string QualityVideoCodecText => !string.IsNullOrWhiteSpace(StreamQuality?.VideoCodecDisplay)
        ? StreamQuality.VideoCodecDisplay
        : _localizationService.GetString("Common.Unknown");

    public string QualityVideoBitrateText => StreamQuality?.VideoBitrate > 0
        ? FormatBitrate(StreamQuality.VideoBitrate)
        : _localizationService.GetString("Common.Unknown");

    public string QualityAudioText => !string.IsNullOrWhiteSpace(StreamQuality?.AudioDetailLabel)
        ? StreamQuality.AudioDetailLabel
        : _localizationService.GetString("Common.Unknown");

    public bool HasTopQualityBadgesReady =>
        StreamQuality != null &&
        StreamQuality.Height > 0 &&
        StreamQuality.Fps > 0 &&
        !string.IsNullOrWhiteSpace(StreamQuality.VideoCodecDisplay);

    [ObservableProperty]
    private bool _isInfoPanelOpen;

    [ObservableProperty]
    private List<Season> _episodeSeasons = new();

    [ObservableProperty]
    private string _episodesPanelTitle = string.Empty;

    [ObservableProperty]
    private Episode? _nextEpisode;

    [ObservableProperty]
    private string _currentEpisodeIdentity = string.Empty;

    [ObservableProperty]
    private bool _isNextEpisodePromptVisible;

    [ObservableProperty]
    private bool _isCreditsZone;

    [ObservableProperty]
    private bool _isDownloadInProgress;

    [ObservableProperty]
    private string _downloadStatusMessage = string.Empty;

    public bool CanShowDownloadButton => CurrentChannel != null && !IsLiveContent && !IsDownloadedPlayback;
    public bool CanShowInfoButton => !IsDownloadedPlayback;

    public bool CanDownloadCurrentContent =>
        CanShowDownloadButton &&
        !IsDownloadInProgress &&
        !string.IsNullOrWhiteSpace(CurrentChannel?.StreamUrl);

    public string DownloadButtonText => IsDownloadInProgress 
        ? _localizationService.GetString("Player.Download.Downloading") 
        : _localizationService.GetString("Player.Download.Download");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeriesPlotVisible))]
    private Episode? _currentEpisode;

    // ── Playback Internal States ─────────────────────────────────────────────
    internal bool _creditsTriggered;
    internal bool _isUserSeeking;
    internal bool _hasPendingSkipSeekTarget;
    internal double _pendingSkipSeekTarget;
    internal DateTime _pendingSkipSeekExpiresUtc = DateTime.MinValue;
    internal double _skipAggregationSeconds;
    internal DateTime _skipAggregationLastUpdatedUtc = DateTime.MinValue;
    internal bool _isPlaybackEnded;
    internal int _recoveryState;
    internal double _lastPausedPosition;
    internal long _lastPausedTimeMs;
    internal double _pendingResumeSeekPosition;
    internal int _pendingResumeSeekAttempts;
    internal double _lastKnownValidPosition;
    internal int _prematureEndRecoveryCount;
    internal DateTime _lastPrematureEndRecoveryUtc = DateTime.MinValue;
    internal const int MaxPrematureEndRecoveries = 5;
    internal static readonly TimeSpan PrematureEndRecoveryCooldown = TimeSpan.FromSeconds(3);
    
    internal int _isPlayPauseInProgress;
    private int _isClockProcessing;
    internal bool _isIntentionallyPaused;
    internal bool _livePauseRequiresHardRestart;
    internal DateTime _lastLiveProgressAtUtc = DateTime.MinValue;
    internal DateTime _lastLivePositionEventAtUtc = DateTime.MinValue;
    internal DateTime _lastLiveAutoRecoverAttemptAtUtc = DateTime.MinValue;
    internal DateTime _liveRecoveryWindowStartUtc = DateTime.MinValue;
    internal int _volumeBeforeMute = 100;
    internal bool _suppressBufferShieldForSeek;
    internal int _seekShieldSuppressionToken;
    internal long _lastSeekTargetMs = -1;
    internal Series? _currentSeriesContext;
    internal bool _isContentTransitioning;
    internal bool _isUpdatingFromService;
    internal int _isDownloadActionRunning;



    internal DateTime _lastWatchHistoryUpdateUtc = DateTime.MinValue;
    internal DateTime _sessionPlaybackStartTimeUtc = DateTime.MinValue;

    internal readonly System.Threading.Timer _autoHideTimer;
    private readonly System.Timers.Timer _clockTimer;
    internal readonly System.Timers.Timer _watchHistoryTimer;

    private static readonly object _logLock = new object();
    internal void LogDebug(string msg) {
        Task.Run(() => {
            try {
                lock (_logLock) {
                    File.AppendAllText(@"d:\IPTVPlayer\vlc_debug_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [PVM] {msg}\n");
                }
            } catch { }
        });
    }

    public int? CurrentProfileId { get; set; }

    // ── Properties to expose internal services to subclasses ─────────────────
    internal IVideoPlayerService VideoPlayerService => _videoPlayerService;
    internal IEpgService EpgService => _epgService;
    internal IMediaService MediaService => _mediaService;
    internal IContentDownloadService ContentDownloadService => _contentDownloadService;
    internal INetworkService NetworkService => _networkService;
    internal ISettingsService SettingsService => _settingsService;
    internal ILicenseService LicenseService => _licenseService;
    internal ILocalizationService LocalizationService => _localizationService;
    internal IWatchHistoryService? WatchHistoryService => _watchHistoryService;
    internal IStalkerPortalService StalkerPortalService => _stalkerPortalService;
    internal IDispatcherService DispatcherService => _dispatcherService;
    internal MainViewModel MainViewModel => _mainViewModel;

    public void RaisePropertyChanged(string propertyName) => OnPropertyChanged(propertyName);
    internal void RaiseNextEpisodeRequestedEvent(Episode episode) => NextEpisodeRequested?.Invoke(this, episode);
    internal void RaiseEpisodeRequestedEvent(Episode episode) => EpisodeRequested?.Invoke(this, episode);
    internal void RaiseEpisodeProgressUpdatedEvent(Episode episode) => EpisodeProgressUpdated?.Invoke(this, episode);
    internal void RaiseSkipOverlayEvent(double seconds) => SkipOverlayRequested?.Invoke(this, new SkipOverlayEventArgs(seconds));

    public PlayerViewModel(
        IVideoPlayerService videoPlayerService,
        IEpgService epgService,
        IMetadataService metadataService,
        IMediaService mediaService,
        IContentDownloadService contentDownloadService,
        INetworkService networkService,
        IDispatcherService dispatcherService,
        ISettingsService settingsService,
        ILicenseService licenseService,
        ILocalizationService localizationService,
        MainViewModel mainViewModel,
        IWatchHistoryService? watchHistoryService,
        IStalkerPortalService stalkerPortalService)
    {
        _videoPlayerService = videoPlayerService;
        _epgService = epgService;
        _metadataService = metadataService;
        _mediaService = mediaService;
        _contentDownloadService = contentDownloadService;
        _networkService = networkService;
        _dispatcherService = dispatcherService;
        _settingsService = settingsService;
        _licenseService = licenseService;
        _localizationService = localizationService;
        _mainViewModel = mainViewModel;
        _watchHistoryService = watchHistoryService;
        _stalkerPortalService = stalkerPortalService;

        // Initialize Controllers
        PlaybackController = new PlayerPlaybackController(this);
        OverlayManager = new PlayerOverlayManager(this);
        EpisodeNavigator = new PlayerEpisodeNavigator(this);
        StallDetector = new PlayerStallDetector(this);
        QualityMonitor = new PlayerQualityMonitor(this);
        SettingsAdapter = new PlayerSettingsAdapter(this);

        _localizationService.LanguageChanged += OnLanguageChanged;

        ConnectionStatus = _localizationService.GetString("Player.Status.Connecting");
        StallDetector.UpdateNetworkStatus(_networkService.CurrentNetworkStatus);
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;

        if (_settingsService?.Settings != null)
        {
            SubtitleFontSize = _settingsService.Settings.SubtitleFontSize;
            SubtitleBackgroundOpacity = _settingsService.Settings.SubtitleBackgroundOpacity;
            SubtitleMargin = _settingsService.Settings.SubtitleMargin;

            // Sync initial volume and mute states from settings
            Volume = _settingsService.Settings.DefaultVolume;
            IsMuted = _settingsService.Settings.IsMuted;
            _volumeBeforeMute = Volume > 0 ? Volume : 100;
        }

        _settingsService.SettingsChanged += OnSettingsChanged;

        _autoHideTimer = new System.Threading.Timer(_ =>
            _dispatcherService.BeginInvoke(() =>
            {
                if (CanAutoHideOverlay())
                {
                    IsVisible = false;
                    if (IsPiPMode)
                        IsPiPControlsForceVisible = false;
                }
            }), null, Timeout.Infinite, Timeout.Infinite);

        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += async (s, e) =>
        {
            if (Interlocked.Exchange(ref _isClockProcessing, 1) == 1)
            {
                return;
            }

            try
            {
                _dispatcherService.BeginInvoke(() => CurrentTimeStr = DateTime.Now.ToString("HH:mm"));

                await Task.WhenAll(
                    Task.Run(async () => {
                        try { await CheckForEpgUpdateAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] EPG update failed: {ex.Message}"); }
                    }),
                    Task.Run(async () => {
                        try { await StallDetector.MonitorLivePlaybackHealthAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Live monitor failed: {ex.Message}"); }
                    })
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Clock tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isClockProcessing, 0);
            }
        };
        _clockTimer.Start();
        CurrentTimeStr = DateTime.Now.ToString("HH:mm");

        _watchHistoryTimer = new System.Timers.Timer(5000);
        _watchHistoryTimer.Elapsed += async (s, e) => await TrackWatchHistoryAsync();
        _watchHistoryTimer.AutoReset = true;

        _videoPlayerService.PlayingChanged += OnVideoPlayerServicePlayingChanged;
        _videoPlayerService.PlaybackEnded += OnVideoPlayerServicePlaybackEnded;
        _videoPlayerService.QualityDetected += OnVideoPlayerServiceQualityDetected;
        _videoPlayerService.BufferingChanged += OnVideoPlayerServiceBufferingChanged;
        _videoPlayerService.ErrorOccurred += OnVideoPlayerServiceErrorOccurred;
        _videoPlayerService.PositionChanged += OnVideoPlayerServicePositionChanged;
        _videoPlayerService.VolumeChanged += OnVideoPlayerServiceVolumeChanged;

        _licenseService.SubscriptionChanged += OnLicenseServiceSubscriptionChanged;
    }

    private void OnLicenseServiceSubscriptionChanged()
    {
        _dispatcherService.BeginInvoke(() => 
        {
            OnPropertyChanged(nameof(IsPremium));
            SetSleepTimerCommand.NotifyCanExecuteChanged();
        });
    }

    // ── Internal Helpers ─────────────────────────────────────────────────────
    internal void UpdateOverlaySecondaryText()
    {
        if (IsLiveContent)
        {
            OverlaySecondaryText = CurrentProgram?.Title ?? string.Empty;
            return;
        }

        if (CurrentChannel == null)
        {
            OverlaySecondaryText = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(CurrentChannel.Plot))
        {
            OverlaySecondaryText = CurrentChannel.Plot!;
            return;
        }

        var parts = new List<string>();
        if (CurrentChannel.ReleaseYear.HasValue)
        {
            parts.Add(CurrentChannel.ReleaseYear.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(CurrentChannel.Cast))
        {
            parts.Add(CurrentChannel.Cast!);
        }

        OverlaySecondaryText = string.Join(" • ", parts);
    }

    internal void PrepareForContentLoading()
    {
        CancelResumeDialog();
        _isContentTransitioning = true;
        _isPlaybackEnded = false;
        _isUserSeeking = false;
        _pendingResumeSeekPosition = 0;
        _pendingResumeSeekAttempts = 0;
        _lastPausedPosition = 0;
        _lastPausedTimeMs = 0;
        _lastSeekTargetMs = -1;
        _prematureEndRecoveryCount = 0;
        _isIntentionallyPaused = false;
        _lastPrematureEndRecoveryUtc = DateTime.MinValue;
        Interlocked.Increment(ref _seekShieldSuppressionToken);
        _suppressBufferShieldForSeek = false;
        PlaybackController.ResetSeekInteractionState();
        IsPlaying = false;
        Position = 0;
        PositionText = "00:00:00";
        Duration = 0;
        DurationText = "00:00:00";
        RemainingTime = IsLiveContent ? "00:00:00" : "-00:00:00";
        IsBuffering = true;
        BufferingProgress = 0;
        
        PlayerLoadingWarningMessage = string.Empty;

        // Yeni içerik yüklenirken eski state sızıntısını önle
        _isStartingOver = false;
        _oldResumePosition = 0;
        ResumePositionText = string.Empty;
        _sessionPlaybackStartTimeUtc = DateTime.MinValue;

        OnPropertyChanged(nameof(IsBufferShieldVisible));
    }

    internal EpgProgram GetFallbackProgram()
    {
        var now = DateTime.UtcNow;
        return new EpgProgram 
        { 
            Title = _localizationService.GetString("Player.Epg.NoInfo"),
            StartTime = now,
            EndTime = now.AddHours(1),
            Description = _localizationService.GetString("Player.Status.NoEpgInfo")
        };
    }

    private async Task CheckForEpgUpdateAsync()
    {
        if (CurrentChannel == null || !IsLiveContent || CurrentProgram == null) return;

        bool isFallback = CurrentProgram.Title == "Program bilgisi yok";

        if (DateTime.UtcNow > CurrentProgram.EndTime || isFallback)
        {
            var newProgram = await _epgService.GetCurrentProgramAsync(CurrentChannel);
            newProgram ??= GetFallbackProgram();

            if (newProgram.Title != CurrentProgram.Title)
            {
                _dispatcherService.Invoke(() => CurrentProgram = newProgram);
            }
        }
    }

    internal Task EnsurePlaybackHealthAsync(Channel channel, int requestVersion)
        => StallDetector.EnsurePlaybackHealthAsync(channel, requestVersion);

    internal Task TrackWatchHistoryAsync()
        => EpisodeNavigator.TrackWatchHistoryAsync();

    internal Task FlushWatchHistoryAsync(bool force, TimeSpan? incrementDelta = null)
        => EpisodeNavigator.FlushWatchHistoryAsync(force, incrementDelta);

    internal void UpdateMediaInfo()
        => QualityMonitor.UpdateMediaInfo();

    internal Task RefreshTracksWithRetryAsync()
        => QualityMonitor.RefreshTracksWithRetryAsync();

    internal void RestartAutoHideTimer()
        => OverlayManager.RestartAutoHideTimer();

    private bool CanAutoHideOverlay()
        => OverlayManager.CanAutoHideOverlay();

    internal void CheckIntroCreditsPosition(double pos)
        => EpisodeNavigator.CheckIntroCreditsPosition(pos);

    internal void RefreshEpisodeBrowserContext(Series? series)
        => EpisodeNavigator.RefreshEpisodeBrowserContext(series);

    internal void UpdateDurationFromService(bool force = false)
        => QualityMonitor.UpdateDurationFromService(force);

    // ── Command Forwarding (Maintains 100% Backward Compatibility) ───────────
    [RelayCommand]
    private async Task PlayPause() => await PlaybackController.PlayPause();

    [RelayCommand]
    private async Task Stop() => await PlaybackController.Stop();

    [RelayCommand]
    private void ToggleMute() => PlaybackController.ToggleMute();

    [RelayCommand]
    private void StartSeeking() => PlaybackController.StartSeeking();

    [RelayCommand]
    private void Seek(double position) => PlaybackController.Seek(position);

    [RelayCommand]
    private void SkipForward(object? parameter) => PlaybackController.SkipForward(parameter);

    [RelayCommand]
    private void SkipBackward(object? parameter) => PlaybackController.SkipBackward(parameter);

    [RelayCommand]
    private void ShowOverlay() => OverlayManager.ShowOverlay();

    [RelayCommand]
    private void ToggleLock() => OverlayManager.ToggleLock();

    [RelayCommand]
    private void OpenAudioSettings() => OverlayManager.OpenAudioSettings();

    [RelayCommand]
    private void OpenQualitySettings() => OverlayManager.OpenQualitySettings();

    [RelayCommand]
    private void OpenInfoPanel() => OverlayManager.OpenInfoPanel();

    [RelayCommand]
    private void ClosePanels() => OverlayManager.ClosePanels();

    [RelayCommand]
    private void ShowSleepTimerMenu() => OverlayManager.ShowSleepTimerMenu();

    [RelayCommand]
    private void SetSleepTimer(SleepTimerOption mode) => OverlayManager.SetSleepTimer(mode);

    [RelayCommand]
    private void CancelSleepTimer() => OverlayManager.CancelSleepTimer();

    [RelayCommand]
    private async Task PlayNextEpisode() => await EpisodeNavigator.PlayNextEpisode();

    [RelayCommand(CanExecute = nameof(CanDownloadCurrentContent))]
    private async Task DownloadCurrentContentAsync() => await EpisodeNavigator.DownloadCurrentContentAsync();

    [RelayCommand(CanExecute = nameof(CanPlayEpisodeFromOverlay))]
    private void PlayEpisodeFromOverlay(Episode? episode) => EpisodeNavigator.PlayEpisodeFromOverlay(episode);

    private bool CanPlayEpisodeFromOverlay(Episode? episode) => EpisodeNavigator.CanPlayEpisodeFromOverlay(episode);

    [RelayCommand]
    private void SetSubtitleSize(string size) => SettingsAdapter.SetSubtitleSize(size);

    [RelayCommand]
    private void SetSubtitleBackground(string opacity) => SettingsAdapter.SetSubtitleBackground(opacity);

    [RelayCommand]
    private void SetSubtitlePosition(string margin) => SettingsAdapter.SetSubtitlePosition(margin);

    [RelayCommand]
    private void CycleVideoFillMode() => SettingsAdapter.CycleVideoFillMode();

    [RelayCommand]
    private async Task ToggleLiveFavoriteAsync()
    {
        var channel = CurrentChannel;
        if (channel == null) return;

        try
        {
            await _mainViewModel.ToggleFavoriteCommand.ExecuteAsync(channel);
            if (CurrentChannel?.Id == channel.Id)
            {
                IsCurrentChannelFavorite = channel.IsFavorite;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"ToggleLiveFavoriteAsync failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenPremiumUpsell() => PremiumUpsellRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EnterPiP() => PiPRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleFullScreen()
    {
        LogDebug($"UI Action: ToggleFullScreen clicked (Target={!IsFullScreen})");
        IsFullScreen = !IsFullScreen;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetAudioTrack(int id)
    {
        LogDebug($"UI Action: SetAudioTrack clicked (Id={id})");
        _videoPlayerService.SetAudioTrack(id);
        SelectedAudioTrack = id;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetSubtitleTrack(int id)
    {
        LogDebug($"UI Action: SetSubtitleTrack clicked (Id={id})");
        _videoPlayerService.SetSubtitleTrack(id);
        SelectedSubtitleTrack = id;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetPlaybackSpeed(float speed)
    {
        _videoPlayerService.PlaybackRate = speed;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private async Task ClosePlayer()
    {
        await Stop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PlayNextLiveChannel()
    {
        LogDebug("UI Action: PlayNextLiveChannel clicked");
        NextLiveChannelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PlayPreviousLiveChannel()
    {
        LogDebug("UI Action: PlayPreviousLiveChannel clicked");
        PreviousLiveChannelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UserInteraction()
    {
        if (IsPiPMode)
            IsPiPControlsForceVisible = true;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void OpenEpisodes()
    {
        LogDebug("UI Action: OpenEpisodes clicked");
        if (!IsSeriesContent) return;
        if (EpisodeSeasons.Count == 0) return;

        var isOpening = !IsEpisodesPanelOpen;
        IsAudioSettingsOpen = false;
        IsQualitySettingsOpen = false;
        IsInfoPanelOpen = false;
        IsEpisodesPanelOpen = isOpening;
        IsLocked = isOpening;
        RestartAutoHideTimer();
    }

    // ── Resume Dialog ───────────────────────────────────────────────────────
    private TaskCompletionSource<bool>? _resumeDialogTcs;

    [ObservableProperty] private bool _isResumeDialogVisible;
    [ObservableProperty] private string _resumePositionText = string.Empty;
    [ObservableProperty] private bool _isPremiumResume;

    public Task<bool> ShowResumeDialogAsync(double positionSeconds)
    {
        _oldResumePosition = positionSeconds;
        ResumePositionText = TimeSpan.FromSeconds(positionSeconds).ToString(@"hh\:mm\:ss");
        IsPremiumResume = _licenseService.IsFeatureAvailable("resume_playback");
        IsResumeDialogVisible = true;
        _resumeDialogTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _resumeDialogTcs.Task;
    }

    public void SetResumePosition(double seconds)
    {
        _lastPausedPosition = seconds;
        _lastPausedTimeMs = (long)(seconds * 1000);
    }

    public void CancelResumeDialog()
    {
        if (IsResumeDialogVisible)
        {
            IsResumeDialogVisible = false;
            _resumeDialogTcs?.TrySetCanceled();
            _resumeDialogTcs = null;
        }
        ResumePositionText = string.Empty;
    }

    internal bool _isStartingOver;
    internal double _oldResumePosition;

    [RelayCommand]
    private void ResumeFromPosition()
    {
        _isStartingOver = false;
        IsResumeDialogVisible = false;
        _resumeDialogTcs?.TrySetResult(true);
        _resumeDialogTcs = null;
        ResumePositionText = string.Empty;
    }

    [RelayCommand]
    private void StartFromBeginning()
    {
        _isStartingOver = true;
        IsResumeDialogVisible = false;
        _resumeDialogTcs?.TrySetResult(false);
        _resumeDialogTcs = null;
        ResumePositionText = string.Empty;
    }

    // ── Property / State Changed Interceptions ──────────────────────────────
    partial void OnCurrentChannelChanged(Channel? value)
    {
        IsCurrentChannelFavorite = value?.IsFavorite ?? false;
        DownloadStatusMessage = string.Empty;
        IsDownloadInProgress = false;
        Interlocked.Exchange(ref _isDownloadActionRunning, 0);
        IsDownloadedPlayback = PlayerEpisodeNavigator.LooksLikeDownloadedPlaybackStreamUrl(value?.StreamUrl);

        if (value != null)
        {
            PlaybackController.CancelSeekBufferShieldSuppression();
            IsLiveContent = value.Type == ChannelType.Live;
            _livePauseRequiresHardRestart = false;
            _isIntentionallyPaused = false;
            _lastLiveProgressAtUtc = DateTime.UtcNow;
            _lastLivePositionEventAtUtc = DateTime.UtcNow;
            _liveRecoveryWindowStartUtc = DateTime.MinValue;
            _isPlaybackEnded = false;
            PlaybackController.ResetSeekInteractionState();
            Position = 0;
            PositionText = "00:00:00";
            Duration = 0;
            DurationText = "00:00:00";
            RemainingTime = IsLiveContent ? "00:00:00" : "-00:00:00";
        }

        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(HasCurrentProgramInfo));
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentProgramChanged(EpgProgram? value)
    {
        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(HasCurrentProgramInfo));
    }

    partial void OnStreamQualityChanged(StreamQualityInfo? value)
    {
        OnPropertyChanged(nameof(HasTopQualityBadgesReady));
        OnPropertyChanged(nameof(QualityResolutionText));
        OnPropertyChanged(nameof(QualityFpsText));
        OnPropertyChanged(nameof(QualityVideoCodecText));
        OnPropertyChanged(nameof(QualityVideoBitrateText));
        OnPropertyChanged(nameof(QualityAudioText));
        QualityMonitor.UpdateStreamInfoFromQuality();
    }

    partial void OnIsLiveContentChanged(bool value)
    {
        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(IsBufferShieldVisible));
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSeriesContentChanged(bool value)
    {
        SettingsAdapter.OnLanguageChanged();
    }

    partial void OnIsDownloadedPlaybackChanged(bool value)
    {
        if (value)
        {
            IsInfoPanelOpen = false;
        }
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanShowInfoButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(DownloadButtonText));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnVolumeChanged(int value)
    {
        if (!_isUpdatingFromService)
        {
            _videoPlayerService.Volume = value;
            if (value > 0)
            {
                _volumeBeforeMute = value;
            }
        }

        if (value > 0 && IsMuted)
        {
            IsMuted = false;
        }
        else if (value == 0 && !IsMuted)
        {
            IsMuted = true;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        _videoPlayerService.IsMuted = value;
    }

    partial void OnIsLockedChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsAudioSettingsOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsQualitySettingsOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsInfoPanelOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsBufferingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBufferShieldVisible));

        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsNextEpisodePromptVisibleChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsEpisodesPanelOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        PlaybackController.CancelSeekBufferShieldSuppression();
        RestartAutoHideTimer();
    }

    partial void OnIsDraggingChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsResizingChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    // ── Infrastructure Event Bridge ──────────────────────────────────────────
    private void OnVideoPlayerServicePlayingChanged(object? s, bool playing)
        => PlaybackController.OnVideoPlayerServicePlayingChanged(s, playing);

    private void OnVideoPlayerServicePlaybackEnded(object? sender, EventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            if (_isContentTransitioning) return;

            _isPlaybackEnded = true;

            if (SleepTimerMode == SleepTimerOption.EndOfEpisode)
            {
                _ = Task.Delay(1500).ContinueWith(_ =>
                    _dispatcherService.BeginInvoke(OverlayManager.TriggerSleepShutdown));
            }

            var duration = _videoPlayerService.Duration;
            var currentPos = Position;
            
            bool isPrematureEnd = (IsLiveContent) || (!IsLiveContent && duration > 0 && (duration - currentPos) > 10);

            if (isPrematureEnd)
            {
                var now = DateTime.UtcNow;
                if (now - _lastPrematureEndRecoveryUtc > TimeSpan.FromSeconds(30))
                    _prematureEndRecoveryCount = 0;

                if (_prematureEndRecoveryCount >= MaxPrematureEndRecoveries)
                {
                    LogDebug($"VM: PREMATURE END recovery limit reached ({MaxPrematureEndRecoveries}). Giving up.");
                    ConnectionStatus = _localizationService.GetString("Player.Status.Unstable");
                    return;
                }

                if (now - _lastPrematureEndRecoveryUtc < PrematureEndRecoveryCooldown)
                {
                    LogDebug("VM: PREMATURE END cooldown active, skipping recovery.");
                    return;
                }

                _prematureEndRecoveryCount++;
                _lastPrematureEndRecoveryUtc = now;
                var lastValid = _lastKnownValidPosition > 1 ? _lastKnownValidPosition : currentPos;
                LogDebug($"VM: PREMATURE END DETECTED. Live: {IsLiveContent}, Pos/Dur: {currentPos}/{duration}s (Valid: {lastValid}). Suspected server truncation. Attempt {_prematureEndRecoveryCount}/{MaxPrematureEndRecoveries}");
                
                _ = StallDetector.AutoRecoverPrematureEndAsync(lastValid);
                return;
            }
            
            EpisodeNavigator.TryShowNextEpisodePromptAtEnd();
        });
    }

    private void OnVideoPlayerServiceQualityDetected(object? s, StreamQualityInfo quality)
        => QualityMonitor.OnVideoPlayerServiceQualityDetected(s, quality);

    private void OnVideoPlayerServiceBufferingChanged(object? s, float progress)
        => PlaybackController.OnVideoPlayerServiceBufferingChanged(s, progress);

    private void OnVideoPlayerServiceErrorOccurred(object? s, string errorMessage)
    {
        _dispatcherService.Invoke(() =>
        {
            PlayerLoadingWarningMessage = string.Empty;
            ConnectionStatus = errorMessage;
            IsBuffering = true;
            BufferingProgress = 0;
        });
    }

    private void OnVideoPlayerServicePositionChanged(object? s, double pos)
        => PlaybackController.OnVideoPlayerServicePositionChanged(s, pos);

    private void OnVideoPlayerServiceVolumeChanged(object? s, int vol)
        => PlaybackController.OnVideoPlayerServiceVolumeChanged(s, vol);

    private void OnNetworkStatusChanged(object? sender, string status)
        => StallDetector.OnNetworkStatusChanged(sender, status);

    private void OnSettingsChanged()
        => SettingsAdapter.OnSettingsChanged();

    private void OnLanguageChanged()
        => SettingsAdapter.OnLanguageChanged();

    public void SetCurrentEpisode(Episode? episode, Episode? nextEpisode = null, Series? series = null)
        => EpisodeNavigator.SetCurrentEpisode(episode, nextEpisode, series);

    public Task PlayChannelAsync(Channel channel, double? startPosition = null)
        => PlaybackController.PlayChannelAsync(channel, startPosition);

    private static string FormatBitrate(int bitrate)
    {
        if (bitrate >= 1_000_000) return $"{bitrate / 1_000_000.0:F2} Mbps";
        if (bitrate >= 1_000) return $"{bitrate / 1_000.0:F1} Kbps";
        return $"{bitrate} bps";
    }

    internal void ApplyVideoFillMode()
    {
        var mediaPlayer = _videoPlayerService.GetMediaPlayer();
        if (mediaPlayer == null) return;

        try
        {
            mediaPlayer.AspectRatio = VideoFillMode switch
            {
                FillMode.Fill => "16:9",
                FillMode.Stretch => "16:9",
                FillMode.Original => (StreamQuality != null && StreamQuality.Width > 0 && StreamQuality.Height > 0)
                    ? $"{StreamQuality.Width}:{StreamQuality.Height}"
                    : null,
                _ => null
            };
            mediaPlayer.CropGeometry = VideoFillMode == FillMode.Fill ? "16:9" : null;
            
            LogDebug($"VM: VideoFillMode applied: {VideoFillMode}");
        }
        catch (Exception ex)
        {
            LogDebug($"VM: Error applying VideoFillMode: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_videoPlayerService != null)
        {
            _videoPlayerService.PlayingChanged -= OnVideoPlayerServicePlayingChanged;
            _videoPlayerService.PlaybackEnded -= OnVideoPlayerServicePlaybackEnded;
            _videoPlayerService.QualityDetected -= OnVideoPlayerServiceQualityDetected;
            _videoPlayerService.BufferingChanged -= OnVideoPlayerServiceBufferingChanged;
            _videoPlayerService.ErrorOccurred -= OnVideoPlayerServiceErrorOccurred;
            _videoPlayerService.PositionChanged -= OnVideoPlayerServicePositionChanged;
            _videoPlayerService.VolumeChanged -= OnVideoPlayerServiceVolumeChanged;
        }

        if (_licenseService != null)
        {
            _licenseService.SubscriptionChanged -= OnLicenseServiceSubscriptionChanged;
        }

        _autoHideTimer?.Dispose();
        _clockTimer?.Dispose();
        _watchHistoryTimer?.Dispose();
        _unreachableWarningTimer?.Dispose();

        _sleepCountdownCts?.Cancel();
        _sleepCountdownCts?.Dispose();
        _sleepCountdownCts = null;

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }
        if (_networkService != null)
        {
            _networkService.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }
}
