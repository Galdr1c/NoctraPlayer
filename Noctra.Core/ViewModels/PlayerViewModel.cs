using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System.Text.RegularExpressions;

namespace Noctra.ViewModels;

/// <summary>
/// Video player view model
/// </summary>
public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private const double OverlayAutoHideDelayMs = 2500;
    private const double NextEpisodePromptTailRatio = 0.06;
    private const double NextEpisodePromptMinTailSeconds = 25;
    private const double NextEpisodePromptMaxTailSeconds = 180;
    private const double SkipAggregationWindowMs = 1200;
    private const double SkipSeekCarryWindowMs = 1400;
    private const double SkipSeekCarryToleranceSeconds = 2.0;
    private const double SeekBufferShieldSuppressionMs = 5000;
    private const double EpisodeCompletedPercentThreshold = 90.0;
    private static readonly double EpisodeCompletedTailSeconds = TimeSpan.FromMinutes(3).TotalSeconds;

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

    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IEpgService _epgService;
    private readonly IMetadataService _metadataService;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly INetworkService _networkService;
    private int _playRequestVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isLiveContent;

    [ObservableProperty]
    private bool _isSeriesContent;

    [ObservableProperty]
    private bool _isDownloadedPlayback;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isPiPMode;

    public bool IsPiPControlsVisible => IsVisible && IsPiPMode;

    [ObservableProperty]
    private string _currentTimeStr = "00:00";

    [ObservableProperty]
    private string _channelName = string.Empty;

    [ObservableProperty]
    private string _channelLogo = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "Bağlanıyor...";

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
    private bool _isAudioSettingsOpen;

    [ObservableProperty]
    private string _networkStatus = "Wi-Fi";

    [ObservableProperty]
    private string _networkIcon = "Wifi"; // Default icon

    [ObservableProperty]
    private string _remainingTime = "-00:00:00";

    [ObservableProperty]
    private Channel? _currentChannel;

    [ObservableProperty]
    private EpgProgram? _currentProgram;

    public bool HasCurrentProgramInfo =>
        CurrentProgram != null &&
        !string.IsNullOrWhiteSpace(CurrentProgram.Title) &&
        !string.Equals(CurrentProgram.Title, "Program bilgisi yok", StringComparison.OrdinalIgnoreCase);
    
    [ObservableProperty]
    private string _overlaySecondaryText = string.Empty;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _volume = 100;

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
    private List<TrackOption> _audioTracks = new();

    [ObservableProperty]
    private List<TrackOption> _subtitleTracks = new();

    [ObservableProperty]
    private int _selectedAudioTrack = -1;

    [ObservableProperty]
    private int _selectedSubtitleTrack = -1;

    [ObservableProperty]
    private bool _isQualitySettingsOpen;

    [ObservableProperty]
    private bool _isEpisodesPanelOpen;

    [ObservableProperty]
    private Models.StreamQualityInfo? _streamQuality;

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

    public string DownloadButtonText => IsDownloadInProgress ? "Indiriliyor..." : "Indir";

    private Episode? _currentEpisode;
    private bool _creditsTriggered;
    private bool _isUserSeeking;
    private bool _hasPendingSkipSeekTarget;
    private double _pendingSkipSeekTarget;
    private DateTime _pendingSkipSeekExpiresUtc = DateTime.MinValue;
    private double _skipAggregationSeconds;
    private DateTime _skipAggregationLastUpdatedUtc = DateTime.MinValue;
    private bool _isPlaybackEnded;
    private int _isEndedSeekRecoverInProgress;
    private double _lastPausedPosition;
    private long _lastPausedTimeMs;
    private double _pendingResumeSeekPosition;
    private int _pendingResumeSeekAttempts;
    private int _isPlayPauseInProgress;
    private bool _livePauseRequiresHardRestart;
    private double _lastLiveObservedPosition = -1;
    private DateTime _lastLiveProgressAtUtc = DateTime.MinValue;
    private DateTime _lastLivePositionEventAtUtc = DateTime.MinValue;
    private int _isLiveAutoRecoverInProgress;
    private DateTime _lastLiveAutoRecoverAttemptAtUtc = DateTime.MinValue;
    private DateTime _liveRecoveryWindowStartUtc = DateTime.MinValue;
    private int _liveRecoveryAttemptsInWindow;
    private int _liveStallScore;
    private int _volumeBeforeMute = 100;
    private bool _suppressBufferShieldForSeek;
    private int _seekShieldSuppressionToken;
    private Series? _currentSeriesContext;
    private bool _isContentTransitioning;
    private int _isDownloadActionRunning;
    private readonly IDispatcherService _dispatcherService;
    private readonly IWatchHistoryService? _watchHistoryService;
    private readonly System.Timers.Timer _autoHideTimer;
    private readonly System.Timers.Timer _clockTimer;
    private readonly System.Timers.Timer _watchHistoryTimer;
    public int? CurrentProfileId { get; set; }

    public PlayerViewModel(
        IVideoPlayerService videoPlayerService,
        IEpgService epgService,
        IMetadataService metadataService,
        IContentDownloadService contentDownloadService,
        INetworkService networkService,
        IDispatcherService dispatcherService,
        IWatchHistoryService? watchHistoryService = null)
    {
        _videoPlayerService = videoPlayerService;
        _epgService = epgService;
        _metadataService = metadataService;
        _contentDownloadService = contentDownloadService;
        _networkService = networkService;
        _dispatcherService = dispatcherService;
        _watchHistoryService = watchHistoryService;

        // Initialize Network Status
        UpdateNetworkStatus(_networkService.CurrentNetworkStatus);
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;

        // Auto-hide timer
        _autoHideTimer = new System.Timers.Timer(OverlayAutoHideDelayMs);
        _autoHideTimer.Elapsed += (s, e) =>
            _dispatcherService.Invoke(() =>
            {
                if (CanAutoHideOverlay())
                {
                    IsVisible = false;
                }
            });
        _autoHideTimer.AutoReset = false;

        // Clock timer
        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += async (s, e) => 
        {
            try
            {
                _dispatcherService.Invoke(() => CurrentTimeStr = DateTime.Now.ToString("HH:mm"));
                await CheckForEpgUpdateAsync();
                await MonitorLivePlaybackHealthAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Clock tick failed: {ex.Message}");
            }
        };
        _clockTimer.Start();
        CurrentTimeStr = DateTime.Now.ToString("HH:mm");

        // Watch history timer (every 5 seconds)
        _watchHistoryTimer = new System.Timers.Timer(5000);
        _watchHistoryTimer.Elapsed += async (s, e) => await TrackWatchHistoryAsync();
        _watchHistoryTimer.AutoReset = true;

        _videoPlayerService.PlayingChanged += (s, playing) => 
        {
            _dispatcherService.Invoke(() =>
            {
                IsPlaying = playing;
                if (playing) 
                {
                    _isContentTransitioning = false;
                    _isPlaybackEnded = false;
                    if (BufferingProgress >= 99f)
                    {
                        IsBuffering = false;
                    }
                    UpdateMediaInfo();
                    _ = RefreshTracksWithRetryAsync();
                    RestartAutoHideTimer(); // Ensure controls stay visible for a few seconds after playback starts
                }
            });
        };

        _videoPlayerService.PlaybackEnded += (_, _) =>
        {
            _dispatcherService.Invoke(() =>
            {
                _isPlaybackEnded = true;
                TryShowNextEpisodePromptAtEnd();
            });
        };

        _videoPlayerService.QualityDetected += (s, quality) =>
        {
            _dispatcherService.Invoke(() =>
            {
                StreamQuality = quality;
                UpdateStreamInfoFromQuality();
            });
        };

        _videoPlayerService.BufferingChanged += (s, progress) =>
        {
            _dispatcherService.Invoke(() =>
            {
                BufferingProgress = progress;

                // Ignore stale buffering callbacks while switching content.
                if (_isContentTransitioning)
                {
                    IsBuffering = true;
                    return;
                }

                // Keep loading active until playback truly starts and buffering reaches 100.
                IsBuffering = !IsPlaying || progress < 100f;

                // Buffering bittiğinde kontrol katmanını mutlaka geri getir.
                if (!IsBuffering)
                {
                    IsVisible = true;
                    RestartAutoHideTimer();
                }
            });
        };

        _videoPlayerService.ErrorOccurred += (s, errorMessage) =>
        {
            _dispatcherService.Invoke(() =>
            {
                ConnectionStatus = errorMessage;
                // Broken/unreachable streams should stay in loading state until user changes content.
                IsBuffering = true;
                BufferingProgress = 0;
            });
        };

        _videoPlayerService.PositionChanged += (s, pos) =>
        {
            _dispatcherService.Invoke(() =>
            {
                var nowUtc = DateTime.UtcNow;
                _lastLivePositionEventAtUtc = nowUtc;

                // Drop tail position events from previous media during transitions.
                if (_isContentTransitioning)
                {
                    return;
                }

                UpdateDurationFromService();
                if (IsPlaying && IsBuffering)
                {
                    IsBuffering = false;
                }

                if (!_isUserSeeking)
                {
                    Position = pos;
                    PositionText = TimeSpan.FromSeconds(pos).ToString(@"hh\:mm\:ss");
                    TryApplyPendingResumeSeek();
                }

                ReleaseSkipSeekCarryIfSettled(nowUtc, pos);

                if (Duration > 0)
                {
                    var remaining = Math.Max(0, Duration - pos);
                    RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
                    CheckIntroCreditsPosition(pos);
                }
            });
        };
    }

    partial void OnCurrentChannelChanged(Channel? value)
    {
        DownloadStatusMessage = string.Empty;
        IsDownloadInProgress = false;
        Interlocked.Exchange(ref _isDownloadActionRunning, 0);
        IsDownloadedPlayback = LooksLikeDownloadedPlaybackStreamUrl(value?.StreamUrl);

        if (value != null)
        {
            CancelSeekBufferShieldSuppression();
            // Canlı TV kontrolü
            IsLiveContent = value.Type == ChannelType.Live;
            _livePauseRequiresHardRestart = false;
            _lastLiveObservedPosition = -1;
            _lastLiveProgressAtUtc = DateTime.UtcNow;
            _lastLivePositionEventAtUtc = DateTime.UtcNow;
            _liveRecoveryWindowStartUtc = DateTime.MinValue;
            _liveRecoveryAttemptsInWindow = 0;
            _liveStallScore = 0;
            _isPlaybackEnded = false;
            ResetSeekInteractionState();
            // Kanal geçişinde eski timeline değerleri görünmesin.
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
        UpdateStreamInfoFromQuality();
    }

    partial void OnIsLiveContentChanged(bool value)
    {
        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(IsBufferShieldVisible));
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
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

    public async Task PlayChannelAsync(Channel channel)
    {
        var requestVersion = Interlocked.Increment(ref _playRequestVersion);

        // Force previous media to stop so stale position events do not leak into the next item.
        _videoPlayerService.Stop();

        CurrentChannel = channel;
        CurrentProgram = GetFallbackProgram();
        IsLiveContent = channel.Type == ChannelType.Live;
        IsSeriesContent = channel.Type == ChannelType.Series;
        UpdateOverlaySecondaryText();
        PrepareForContentLoading();
        StreamQuality = null;
        StreamInfo = "Kalite tespit ediliyor...";
        try
        {
            var resolvedStreamUrl = await _contentDownloadService.ResolvePlayableUrlAsync(channel.StreamUrl);
            await _videoPlayerService.PlayAsync(resolvedStreamUrl);
        }
        catch
        {
            if (requestVersion == _playRequestVersion && CurrentChannel?.Id == channel.Id)
            {
                IsBuffering = false;
                BufferingProgress = 0;
            }

            throw;
        }

        if (requestVersion != _playRequestVersion || CurrentChannel?.Id != channel.Id)
        {
            return;
        }

        // Start watch history tracking for VOD content
        if (!IsLiveContent)
        {
            _watchHistoryTimer.Start();
        }
        else
        {
            _watchHistoryTimer.Stop();
        }

        // Always query DB-backed EPG; IsLoaded flag may belong to another service instance.
        var program = await _epgService.GetCurrentProgramAsync(channel);
        if (requestVersion == _playRequestVersion && CurrentChannel?.Id == channel.Id)
        {
            CurrentProgram = program ?? GetFallbackProgram();
            UpdateOverlaySecondaryText();
        }

        // VOD / Dizi için TMDB metadata'yı doğrudan zenginleştir.
        if (!IsLiveContent)
        {
            _ = EnrichCurrentChannelMetadataAsync(channel, requestVersion);
        }
    }

    private async Task EnrichCurrentChannelMetadataAsync(Channel channel, int requestVersion)
    {
        try
        {
            await _metadataService.EnrichChannelAsync(channel);

            if (requestVersion != _playRequestVersion || CurrentChannel?.Id != channel.Id)
            {
                return;
            }

            _dispatcherService.Invoke(UpdateOverlaySecondaryText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Metadata enrich failed: {ex.Message}");
        }
    }

    private void UpdateOverlaySecondaryText()
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

    private async Task TrackWatchHistoryAsync()
    {
        if (_watchHistoryService == null || CurrentProfileId == null || CurrentChannel == null || !IsPlaying)
            return;

        await FlushWatchHistoryAsync(force: false);
    }

    private async Task FlushWatchHistoryAsync(bool force)
    {
        if (_watchHistoryService == null || CurrentProfileId == null || CurrentChannel == null)
        {
            return;
        }

        if (!force && !IsPlaying)
        {
            return;
        }

        try
        {
            var isEpisodePlayback = CurrentChannel.Type == ChannelType.Series && _currentEpisode?.Id > 0;
            var channelId = !isEpisodePlayback && CurrentChannel.Id > 0 ? CurrentChannel.Id : (int?)null;
            var livePosition = Math.Max(0, _videoPlayerService.Position);
            var currentPosition = TimeSpan.FromSeconds(Math.Max(Position, livePosition));
            var currentDuration = Duration > 0 ? TimeSpan.FromSeconds(Duration) : (TimeSpan?)null;
            var isCompleted = IsEpisodeCompleted(Duration, Position);

            await _watchHistoryService.TrackWatchAsync(
                CurrentProfileId.Value,
                channelId,
                isEpisodePlayback ? _currentEpisode!.Id : null,
                currentPosition,
                isCompleted,
                currentDuration
            );

            if (isEpisodePlayback && _currentEpisode != null)
            {
                var finalCompleted = _currentEpisode.IsCompleted || isCompleted;
                _dispatcherService.BeginInvoke(() =>
                {
                    _currentEpisode.LastWatched = DateTime.Now;
                    _currentEpisode.WatchedPosition = finalCompleted && currentDuration.HasValue
                        ? currentDuration.Value
                        : currentPosition;
                    _currentEpisode.IsCompleted = finalCompleted;
                    if (currentDuration.HasValue)
                    {
                        _currentEpisode.Duration = currentDuration.Value;
                    }

                    RefreshEpisodeBrowserContext(_currentSeriesContext);
                    EpisodeProgressUpdated?.Invoke(this, _currentEpisode);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Watch history tracking error: {ex.Message}");
        }
    }

    private async Task CheckForEpgUpdateAsync()
    {
        if (CurrentChannel == null || !IsLiveContent || CurrentProgram == null) return;

        // Retry if we have fallback "Program bilgisi yok"
        bool isFallback = CurrentProgram.Title == "Program bilgisi yok";

        if (DateTime.Now > CurrentProgram.EndTime || isFallback)
        {
            // Program finished or fallback exists, fetch updated program
            var newProgram = await _epgService.GetCurrentProgramAsync(CurrentChannel);
            newProgram ??= GetFallbackProgram();

            if (newProgram.Title != CurrentProgram.Title)
            {
                _dispatcherService.Invoke(() => CurrentProgram = newProgram);
            }
        }
    }

    private async Task MonitorLivePlaybackHealthAsync()
    {
        var channel = CurrentChannel;
        if (channel == null)
        {
            return;
        }

        var isLivePlayback =
            IsLiveContent ||
            channel.Type == ChannelType.Live ||
            LooksLikeLiveStreamUrl(channel.StreamUrl);
        if (!isLivePlayback)
        {
            return;
        }

        // Kullanıcı canlı yayını manuel pause etmişse auto-recover devreye girmez.
        if (_livePauseRequiresHardRestart)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var currentPos = Position;

        if (_lastLiveObservedPosition < 0)
        {
            _lastLiveObservedPosition = currentPos;
            _lastLiveProgressAtUtc = nowUtc;
            _lastLivePositionEventAtUtc = nowUtc;
            return;
        }

        var hasProgress = Math.Abs(currentPos - _lastLiveObservedPosition) > 0.35;
        if (hasProgress)
        {
            _lastLiveObservedPosition = currentPos;
            _lastLiveProgressAtUtc = nowUtc;
            _liveStallScore = 0;
            if (nowUtc - _lastLiveAutoRecoverAttemptAtUtc > TimeSpan.FromSeconds(30))
            {
                _liveRecoveryAttemptsInWindow = 0;
            }
            return;
        }

        // Donma sinyali:
        // 1) Position ilerlemiyor
        // 2) PositionChanged olayı kesilmiş
        var stalledFor = nowUtc - _lastLiveProgressAtUtc;
        var noEventFor = nowUtc - _lastLivePositionEventAtUtc;
        var isStalled =
            noEventFor >= TimeSpan.FromSeconds(5) ||
            (stalledFor >= TimeSpan.FromSeconds(5) && noEventFor >= TimeSpan.FromSeconds(3));
        if (!isStalled)
        {
            _liveStallScore = 0;
            return;
        }

        // Tek örneklem hatalarına karşı kısa doğrulama.
        _liveStallScore = Math.Min(_liveStallScore + 1, 3);
        if (_liveStallScore < 2)
        {
            return;
        }

        if (IsBuffering && stalledFor < TimeSpan.FromSeconds(10))
        {
            return;
        }

        // Flapping önleme: reconnect denemeleri arasında cooldown.
        if (nowUtc - _lastLiveAutoRecoverAttemptAtUtc < TimeSpan.FromSeconds(15))
        {
            return;
        }

        // Flapping önleme: 2 dakikalık pencerede en fazla 3 auto-reconnect.
        if (_liveRecoveryWindowStartUtc == DateTime.MinValue || nowUtc - _liveRecoveryWindowStartUtc > TimeSpan.FromMinutes(2))
        {
            _liveRecoveryWindowStartUtc = nowUtc;
            _liveRecoveryAttemptsInWindow = 0;
        }

        if (_liveRecoveryAttemptsInWindow >= 3)
        {
            ConnectionStatus = "Yayın kararsız, bağlantı bekleniyor...";
            return;
        }

        if (Interlocked.Exchange(ref _isLiveAutoRecoverInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            if (CurrentChannel == null || CurrentChannel.Id != channel.Id)
            {
                return;
            }

            _lastLiveAutoRecoverAttemptAtUtc = nowUtc;
            _liveRecoveryAttemptsInWindow++;
            IsBuffering = true;
            BufferingProgress = 0;
            ConnectionStatus = "Yayın tekrar bağlanıyor...";
            IsVisible = true;
            RestartAutoHideTimer();

            _videoPlayerService.Stop();
            await Task.Delay(220);

            if (CurrentChannel == null || CurrentChannel.Id != channel.Id)
            {
                return;
            }

            await _videoPlayerService.PlayAsync(channel.StreamUrl);
            _lastLiveObservedPosition = -1;
            _lastLiveProgressAtUtc = DateTime.UtcNow;
            _lastLivePositionEventAtUtc = DateTime.UtcNow;
            _liveStallScore = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Live auto-recover failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isLiveAutoRecoverInProgress, 0);
        }
    }

    private EpgProgram GetFallbackProgram()
    {
        var now = DateTime.Now;
        return new EpgProgram 
        { 
            Title = "Program bilgisi yok",
            StartTime = now,
            EndTime = now.AddHours(1),
            Description = "Yayın için program bilgisi bulunamadı."
        };
    }


    private void UpdateStreamInfoFromQuality()
    {
        if (StreamQuality == null)
        {
            StreamInfo = "Kalite tespit ediliyor...";
            return;
        }

        var resolution = StreamQuality.ResolutionLabel;
        var fps = StreamQuality.Fps > 0 ? $" | {StreamQuality.Fps} fps" : string.Empty;
        StreamInfo = $"{resolution}{fps}";
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer.Stop();
        IsVisible = true;
        if (CanAutoHideOverlay())
        {
            _autoHideTimer.Start();
        }
    }

    private bool CanAutoHideOverlay()
    {
        return IsPlaying
            && !IsLocked
            && !IsBuffering
            && !IsAudioSettingsOpen
            && !IsQualitySettingsOpen
            && !IsEpisodesPanelOpen
            && !IsInfoPanelOpen
            && !IsNextEpisodePromptVisible;
    }

    [RelayCommand]
    private void ShowOverlay() => RestartAutoHideTimer();

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void OpenAudioSettings()
    {
        IsAudioSettingsOpen = !IsAudioSettingsOpen;
        if (IsAudioSettingsOpen)
        {
            IsQualitySettingsOpen = false;
            IsEpisodesPanelOpen = false;
            IsInfoPanelOpen = false;
            IsLocked = true;
            UpdateMediaInfo();
            _ = RefreshTracksWithRetryAsync();
        }
    }

    [RelayCommand]
    private void OpenQualitySettings()
    {
        IsQualitySettingsOpen = !IsQualitySettingsOpen;
        if (IsQualitySettingsOpen)
        {
            IsAudioSettingsOpen = false;
            IsEpisodesPanelOpen = false;
            IsInfoPanelOpen = false;
            IsLocked = true;
        }
    }

    [RelayCommand]
    private void OpenInfoPanel()
    {
        if (IsDownloadedPlayback)
        {
            return;
        }

        IsInfoPanelOpen = !IsInfoPanelOpen;
        if (IsInfoPanelOpen)
        {
            IsAudioSettingsOpen = false;
            IsQualitySettingsOpen = false;
            IsEpisodesPanelOpen = false;
            IsLocked = true;
        }
    }

    [RelayCommand]
    private void ClosePanels()
    {
        IsAudioSettingsOpen = false;
        IsQualitySettingsOpen = false;
        IsEpisodesPanelOpen = false;
        IsInfoPanelOpen = false;
        IsLocked = false;
        RestartAutoHideTimer();
    }

    private void UpdateMediaInfo()
    {
        UpdateDurationFromService(force: true);
        TryApplyPendingResumeSeek();

        AudioTracks = _videoPlayerService.AudioTracks
            .Where(t => t.Id >= 0 && !IsDisabledTrackLabel(t.Name))
            .Select(t => new TrackOption(t.Id, NormalizeTrackName(t.Name, $"Ses {t.Id}")))
            .ToList();

        SubtitleTracks = _videoPlayerService.SubtitleTracks
            .Select(t => IsDisabledTrackLabel(t.Name)
                ? new TrackOption(t.Id, "Kapalı")
                : new TrackOption(t.Id, NormalizeTrackName(t.Name, $"Altyazı {t.Id}")))
            .ToList();

        if (!SubtitleTracks.Any(t => string.Equals(t.Name, "Kapalı", StringComparison.OrdinalIgnoreCase)))
        {
            SubtitleTracks.Add(new TrackOption(-1, "Kapalı"));
        }
    }

    private void UpdateDurationFromService(bool force = false)
    {
        var latestDuration = _videoPlayerService.Duration;
        if (latestDuration <= 0)
        {
            if (force && Duration <= 0)
            {
                DurationText = "00:00:00";
            }
            return;
        }

        if (!force && Duration > 0 && Math.Abs(Duration - latestDuration) < 0.25)
        {
            return;
        }

        Duration = latestDuration;
        DurationText = TimeSpan.FromSeconds(latestDuration).ToString(@"hh\:mm\:ss");
    }

    private static bool IsDisabledTrackLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.Trim();
        return string.Equals(value, "Disable", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Devre Dışı", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Kapalı", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTrackName(string? rawName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return fallback;
        }

        var name = rawName.Trim();

        // Remove track numbers and common prefixes
        name = Regex.Replace(
            name,
            @"^\s*(track|audio|subtitle|ses|altyazı)\s*\d+\s*([:\-\)\.]|\s)\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Remove provider tags and common internet release tags
        name = Regex.Replace(
            name,
            @"(#\w+|\[NCTRA\]|\[.*?SEED\]|\[.*?RIP\]|\[.*?WEB\]|\[.*?HD\]|\[.*?TV\])",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Remove language codes in brackets or parentheses like (tr), [en]
        name = Regex.Replace(
            name,
            @"\((?i:tr|en|de|fr|es|it|ru|ar|pl|pt|nl|sv|da|no|fi)\)|\b(?i:tr|en|de|fr|es|it|ru|ar|pl|pt|nl|sv|da|no|fi)\b",
            string.Empty,
            RegexOptions.IgnoreCase);

        if (Regex.IsMatch(name, @"^\s*(track|audio|subtitle|ses|altyazı)\s*\d+\s*$", RegexOptions.IgnoreCase))
        {
            return fallback;
        }

        // Remove bracket characters while keeping inner text: [English] -> English
        name = name.Replace("[", string.Empty).Replace("]", string.Empty);
        
        // Clean up separator junk
        name = Regex.Replace(name, @"\s*[:\-\.]+\s*$", string.Empty); // Trailing
        name = Regex.Replace(name, @"^[:\-\.]+\s*", string.Empty);    // Leading
        
        name = Regex.Replace(name, @"\s+", " ").Trim();
        name = CollapseDuplicateLabelParts(name);

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string CollapseDuplicateLabelParts(string value)
    {
        var parts = value
            .Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (parts.Count <= 1)
        {
            return value;
        }

        static string Key(string text) => Regex.Replace(text, @"[\W_]+", string.Empty).ToLowerInvariant();

        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var key = Key(part);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            unique.Add(part);
        }

        if (unique.Count == 0)
        {
            return value;
        }

        return string.Join(" - ", unique);
    }

    private async Task RefreshTracksWithRetryAsync()
    {
        // Some streams expose track metadata shortly after playback starts.
        var delays = new[] { 250, 800, 1600 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay);
            if (!IsPlaying || CurrentChannel == null)
            {
                return;
            }

            _dispatcherService.Invoke(UpdateMediaInfo);

            if (AudioTracks.Count > 0 || SubtitleTracks.Count > 0)
            {
                return;
            }
        }
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        if (Interlocked.Exchange(ref _isPlayPauseInProgress, 1) == 1)
        {
            return;
        }

        try
        {
        var treatAsLivePlayback =
            IsLiveContent ||
            CurrentChannel?.Type == ChannelType.Live ||
            LooksLikeLiveStreamUrl(CurrentChannel?.StreamUrl);

        if (IsPlaying)
        {
            if (treatAsLivePlayback)
            {
                // Live içeriği duraklatınca son kare ekranda kalsın (beyaz ekran olmasın).
                // Tekrar play'de hard restart ile canlı uca dönüyoruz.
                _videoPlayerService.Pause();
                _livePauseRequiresHardRestart = true;
                return;
            }

            var mediaPlayer = _videoPlayerService.GetMediaPlayer();
            _lastPausedTimeMs = mediaPlayer?.Time ?? 0;
            _lastPausedPosition = _lastPausedTimeMs > 0 ? _lastPausedTimeMs / 1000.0 : Position;
            _videoPlayerService.Pause();
        }
        else if (CurrentChannel != null)
        {
            var mediaPlayer = _videoPlayerService.GetMediaPlayer();
            var hasLoadedMedia = mediaPlayer?.Media != null;
            await ResumePlaybackAsync(CurrentChannel.StreamUrl, hasLoadedMedia);
        }
        
        RestartAutoHideTimer();
        }
        finally
        {
            Interlocked.Exchange(ref _isPlayPauseInProgress, 0);
        }
    }

    private async Task ResumePlaybackAsync(string streamUrl, bool hasLoadedMedia)
    {
        var treatAsLivePlayback =
            IsLiveContent ||
            CurrentChannel?.Type == ChannelType.Live ||
            LooksLikeLiveStreamUrl(streamUrl);

        if (treatAsLivePlayback)
        {
            _pendingResumeSeekPosition = 0;
            _pendingResumeSeekAttempts = 0;
            _lastPausedPosition = 0;
            _lastPausedTimeMs = 0;
            if (_livePauseRequiresHardRestart)
            {
                _videoPlayerService.Stop();
                await Task.Delay(120);
                IsBuffering = true;
                BufferingProgress = 0;
                await _videoPlayerService.PlayAsync(streamUrl);
                _livePauseRequiresHardRestart = false;
            }
            else if (hasLoadedMedia)
            {
                _videoPlayerService.Resume();
            }
            else
            {
                IsBuffering = true;
                BufferingProgress = 0;
                await _videoPlayerService.PlayAsync(streamUrl);
            }
            return;
        }

        if (_lastPausedPosition <= 1)
        {
            if (hasLoadedMedia)
            {
                _videoPlayerService.Resume();
            }
            else
            {
                await _videoPlayerService.PlayAsync(streamUrl);
            }

            return;
        }

        var targetPosition = _lastPausedPosition;
        // Slight forward compensation to offset keyframe-based resume landing behind target.
        var targetTimeMs = (_lastPausedTimeMs > 0 ? _lastPausedTimeMs : (long)(targetPosition * 1000)) + 350;
        _pendingResumeSeekPosition = targetPosition;
        _pendingResumeSeekAttempts = 0;

        // Prefer native resume for smoothness. Only force seek when drift is significant.
        if (hasLoadedMedia)
        {
            _videoPlayerService.Resume();
            await Task.Delay(220);

            var resumePlayer = _videoPlayerService.GetMediaPlayer();
            var resumedMs = resumePlayer?.Time ?? 0;
            if (resumedMs > 0)
            {
                var driftMs = targetTimeMs - resumedMs;
                if (driftMs <= 1000)
                {
                    _pendingResumeSeekPosition = 0;
                    _pendingResumeSeekAttempts = 0;
                    await EnsurePlaybackStartedAsync(streamUrl);
                    return;
                }
            }
            else if (Position + 1 >= targetPosition)
            {
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
        }
        else
        {
            await _videoPlayerService.PlayAsync(streamUrl);
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(220 + (attempt * 60));
            if (IsLiveContent || CurrentChannel == null)
            {
                return;
            }

            var mediaPlayer = _videoPlayerService.GetMediaPlayer();
            if (mediaPlayer != null && targetTimeMs > 0)
            {
                mediaPlayer.Time = targetTimeMs;
            }
            else
            {
                _videoPlayerService.Position = targetPosition;
            }
            await Task.Delay(120);

            var currentTimeMs = mediaPlayer?.Time ?? 0;
            if (currentTimeMs > 0 && currentTimeMs + 1200 >= targetTimeMs)
            {
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }

            if (Position + 1 >= targetPosition)
            {
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
        }

        await EnsurePlaybackStartedAsync(streamUrl);
    }

    private async Task EnsurePlaybackStartedAsync(string streamUrl)
    {
        if (IsPlaying || CurrentChannel == null || IsLiveContent)
        {
            return;
        }

        await Task.Delay(280);
        if (IsPlaying)
        {
            return;
        }

        await _videoPlayerService.PlayAsync(streamUrl);
    }

    private static bool LooksLikeLiveStreamUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var normalized = url.ToLowerInvariant();
        if (normalized.Contains("/movie/") || normalized.Contains("/series/") || normalized.Contains("/vod/"))
        {
            return false;
        }

        return normalized.Contains("/live/");
    }

    private void TryApplyPendingResumeSeek()
    {
        if (IsLiveContent || _pendingResumeSeekPosition <= 1)
        {
            return;
        }

        var mediaPlayer = _videoPlayerService.GetMediaPlayer();
        var pendingTimeMs = _lastPausedTimeMs > 0 ? _lastPausedTimeMs : (long)(_pendingResumeSeekPosition * 1000);
        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            if (mediaPlayer.Time + 1000 >= pendingTimeMs)
            {
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                return;
            }
        }

        if (Position + 1 >= _pendingResumeSeekPosition)
        {
            _pendingResumeSeekPosition = 0;
            _pendingResumeSeekAttempts = 0;
            return;
        }

        if (_pendingResumeSeekAttempts >= 20)
        {
            _pendingResumeSeekPosition = 0;
            _pendingResumeSeekAttempts = 0;
            return;
        }

        _pendingResumeSeekAttempts++;
        // Avoid micro-corrections that make resume feel jumpy.
        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            var driftMs = pendingTimeMs - mediaPlayer.Time;
            if (driftMs <= 1000)
            {
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                return;
            }
        }

        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            mediaPlayer.Time = pendingTimeMs;
        }
        else
        {
            _videoPlayerService.Position = _pendingResumeSeekPosition;
        }
    }

    [RelayCommand]
    private async Task Stop()
    {
        _isContentTransitioning = false;
        _isPlaybackEnded = false;
        ResetSeekInteractionState();
        _watchHistoryTimer.Stop();
        await FlushWatchHistoryAsync(force: true);
        _videoPlayerService.Stop();
        _livePauseRequiresHardRestart = false;
        _lastLiveObservedPosition = -1;
        _lastLiveProgressAtUtc = DateTime.MinValue;
        _lastLivePositionEventAtUtc = DateTime.MinValue;
        _lastLiveAutoRecoverAttemptAtUtc = DateTime.MinValue;
        _liveRecoveryWindowStartUtc = DateTime.MinValue;
        _liveRecoveryAttemptsInWindow = 0;
        _liveStallScore = 0;
        CurrentChannel = null;
        CurrentProgram = null;
        IsVisible = true;
        await _contentDownloadService.CleanupPlaybackCacheAsync();
    }

    partial void OnVolumeChanged(int value)
    {
        _videoPlayerService.Volume = value;
        if (value > 0)
        {
            _volumeBeforeMute = value;
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

    [RelayCommand]
    private void ToggleMute()
    {
        if (!IsMuted)
        {
            if (Volume > 0)
            {
                _volumeBeforeMute = Volume;
            }

            IsMuted = true;
            if (Volume != 0)
            {
                Volume = 0;
            }
        }
        else
        {
            IsMuted = false;
            if (Volume == 0)
            {
                Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 50;
            }
        }

        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void StartSeeking()
    {
        _isUserSeeking = true;
    }

    [RelayCommand]
    private void Seek(double position)
    {
        _isUserSeeking = false;
        if (IsLiveContent) return;
        EnableSeekBufferShieldSuppression();
        var clamped = ClampSeekPosition(position);
        ResetSkipSeekCarry();
        ResetSkipOverlayAggregation();
        Position = clamped;
        PositionText = TimeSpan.FromSeconds(clamped).ToString(@"hh\:mm\:ss");
        if (Duration > 0)
        {
            var remaining = Math.Max(0, Duration - clamped);
            RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
        }
        CheckIntroCreditsPosition(clamped);
        SetPlaybackPosition(clamped);
        if (_isPlaybackEnded)
        {
            _ = EnsurePlaybackResumedAfterEndedSeekAsync(clamped);
        }
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SkipForward(object? parameter)
    {
        ApplySkipDelta(ParseSkipSeconds(parameter));
    }

    /// <summary>
    /// Mevcut bölümü ayarlar (dizi oynatma başlatıldığında çağrılır)
    /// </summary>
    public void SetCurrentEpisode(Episode? episode, Episode? nextEpisode = null, Series? series = null)
    {
        _currentEpisode = episode;
        CurrentEpisodeIdentity = BuildEpisodeIdentity(episode);
        NextEpisode = nextEpisode;
        _creditsTriggered = false;
        IsCreditsZone = false;
        IsNextEpisodePromptVisible = false;
        IsEpisodesPanelOpen = false;

        if (episode == null)
        {
            _currentSeriesContext = null;
            EpisodeSeasons = new List<Season>();
            EpisodesPanelTitle = string.Empty;
            PlayEpisodeFromOverlayCommand.NotifyCanExecuteChanged();
            PlayNextEpisodeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanDownloadCurrentContent));
            DownloadCurrentContentCommand.NotifyCanExecuteChanged();
            return;
        }

        _currentSeriesContext = series
            ?? episode.Season?.Series
            ?? _currentSeriesContext;
        RefreshEpisodeBrowserContext(_currentSeriesContext);

        if (episode?.Duration is TimeSpan knownDuration && knownDuration.TotalSeconds > 0)
        {
            var seconds = knownDuration.TotalSeconds;
            if (Duration <= 0 || Math.Abs(Duration - seconds) > 1)
            {
                Duration = seconds;
                DurationText = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
            }
        }

        PlayNextEpisodeCommand.NotifyCanExecuteChanged();
        PlayEpisodeFromOverlayCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    private void TryShowNextEpisodePromptAtEnd()
    {
        if (_creditsTriggered ||
            IsLiveContent ||
            CurrentChannel?.Type != ChannelType.Series ||
            NextEpisode == null)
        {
            return;
        }

        _creditsTriggered = true;
        IsCreditsZone = true;
        IsNextEpisodePromptVisible = true;

        if (GetAutoSkipCreditsSetting())
        {
            _ = PlayNextEpisodeCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Position bazlı intro/credits tespiti
    /// </summary>
    private void CheckIntroCreditsPosition(double pos)
    {
        if (_currentEpisode == null || IsLiveContent || NextEpisode == null)
        {
            return;
        }

        if (!TryGetCreditsTriggerThreshold(out var triggerAt))
        {
            return;
        }

        var isInCreditsZone = pos >= triggerAt;
        var exitThreshold = Math.Max(0, triggerAt - 3);
        var hasExitedCreditsZone = pos < exitThreshold;

        if (_creditsTriggered)
        {
            if (hasExitedCreditsZone)
            {
                _creditsTriggered = false;
                IsCreditsZone = false;
                IsNextEpisodePromptVisible = false;
            }
            else
            {
                IsCreditsZone = true;
                IsNextEpisodePromptVisible = true;
            }

            return;
        }

        if (!isInCreditsZone)
        {
            return;
        }

        _creditsTriggered = true;
        IsCreditsZone = true;
        IsNextEpisodePromptVisible = true;

        if (GetAutoSkipCreditsSetting())
        {
            _ = PlayNextEpisodeCommand.ExecuteAsync(null);
        }
    }

    private bool TryGetCreditsTriggerThreshold(out double triggerAt)
    {
        triggerAt = 0;
        if (_currentEpisode == null)
        {
            return false;
        }

        var hasDuration = Duration > 0;
        var fallbackFiveMinuteTrigger = hasDuration
            ? Math.Max(0, Duration - EpisodeCompletedTailSeconds)
            : double.MaxValue;

        if (_currentEpisode.CreditsStartSec is double creditsStartSec && creditsStartSec > 0)
        {
            triggerAt = hasDuration
                ? Math.Min(creditsStartSec, fallbackFiveMinuteTrigger)
                : creditsStartSec;
            return true;
        }

        if (!hasDuration)
        {
            return false;
        }

        var tailThreshold = Math.Clamp(
            Duration * NextEpisodePromptTailRatio,
            NextEpisodePromptMinTailSeconds,
            NextEpisodePromptMaxTailSeconds);
        triggerAt = Math.Min(
            Math.Max(0, Duration - tailThreshold),
            fallbackFiveMinuteTrigger);
        return true;
    }

    private bool GetAutoSkipCreditsSetting()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Noctra", "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                return settings?.AutoSkipCredits ?? false;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    [RelayCommand]
    private async Task PlayNextEpisode()
    {
        if (NextEpisode == null)
        {
            return;
        }

        if (IsDownloadedPlayback && !IsDownloadedStreamUrl(NextEpisode.StreamUrl))
        {
            DownloadStatusMessage = "Siradaki bolum indirilmemis.";
            RestartAutoHideTimer();
            return;
        }

        var nextEpisode = NextEpisode;
        PrepareForContentLoading();
        IsNextEpisodePromptVisible = false;
        IsCreditsZone = false;
        _creditsTriggered = false;
        NextEpisodeRequested?.Invoke(this, nextEpisode);
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDownloadCurrentContent))]
    private async Task DownloadCurrentContentAsync()
    {
        if (CurrentChannel == null || !CanDownloadCurrentContent)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isDownloadActionRunning, 1) == 1)
        {
            return;
        }

        IsDownloadInProgress = true;
        DownloadStatusMessage = "Indirme baslatiliyor...";
        RestartAutoHideTimer();

        try
        {
            var request = BuildDownloadRequest(CurrentChannel);
            var result = await _contentDownloadService.QueueDownloadAsync(request);
            DownloadStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            DownloadStatusMessage = UserFriendlyErrorMessage.WithPrefix("Indirme hatasi", ex);
        }
        finally
        {
            IsDownloadInProgress = false;
            Interlocked.Exchange(ref _isDownloadActionRunning, 0);
        }
    }

    [RelayCommand]
    private void SkipBackward(object? parameter)
    {
        ApplySkipDelta(-ParseSkipSeconds(parameter));
    }

    private DownloadContentRequest BuildDownloadRequest(Channel channel)
    {
        var profileId = CurrentProfileId ?? 0;
        var playlistId = channel.PlaylistId > 0
            ? channel.PlaylistId
            : _currentSeriesContext?.PlaylistId ?? 0;
        var audioTracks = AudioTracks
            .Select(t => new DownloadTrackOption(t.Id, t.Name))
            .ToList();
        var subtitleTracks = SubtitleTracks
            .Select(t => new DownloadTrackOption(t.Id, t.Name))
            .ToList();
        var poster = channel.CoverUrl ?? channel.LogoUrl;

        if (channel.Type == ChannelType.Series)
        {
            return new DownloadContentRequest(
                profileId,
                DownloadItemType.SeriesEpisode,
                _currentEpisode?.Name ?? channel.Name,
                _currentEpisode?.StreamUrl ?? channel.StreamUrl,
                poster,
                playlistId,
                channel.Id,
                _currentEpisode?.Id ?? 0,
                audioTracks,
                subtitleTracks);
        }

        return new DownloadContentRequest(
            profileId,
            DownloadItemType.Vod,
            channel.Name,
            channel.StreamUrl,
            poster,
            playlistId,
            channel.Id,
            0,
            audioTracks,
            subtitleTracks);
    }

    private void ApplySkipDelta(double deltaSeconds)
    {
        if (IsLiveContent)
        {
            return;
        }

        if (Math.Abs(deltaSeconds) <= 0.001)
        {
            RestartAutoHideTimer();
            return;
        }

        EnableSeekBufferShieldSuppression();

        var nowUtc = DateTime.UtcNow;
        var basePosition = Position;
        if (_hasPendingSkipSeekTarget && nowUtc <= _pendingSkipSeekExpiresUtc)
        {
            basePosition = _pendingSkipSeekTarget;
        }
        else
        {
            ResetSkipSeekCarry();
        }

        var targetPosition = ClampSeekPosition(basePosition + deltaSeconds);
        var effectiveDelta = targetPosition - basePosition;
        if (Math.Abs(effectiveDelta) <= 0.001)
        {
            RestartAutoHideTimer();
            return;
        }

        _pendingSkipSeekTarget = targetPosition;
        _pendingSkipSeekExpiresUtc = nowUtc.AddMilliseconds(SkipSeekCarryWindowMs);
        _hasPendingSkipSeekTarget = true;

        Position = targetPosition;
        PositionText = TimeSpan.FromSeconds(targetPosition).ToString(@"hh\:mm\:ss");
        if (Duration > 0)
        {
            var remaining = Math.Max(0, Duration - targetPosition);
            RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
        }
        CheckIntroCreditsPosition(targetPosition);

        SetPlaybackPosition(targetPosition);
        if (_isPlaybackEnded)
        {
            _ = EnsurePlaybackResumedAfterEndedSeekAsync(targetPosition);
        }
        RaiseSkipOverlay(effectiveDelta, nowUtc);
        RestartAutoHideTimer();
    }

    private async Task EnsurePlaybackResumedAfterEndedSeekAsync(double targetPosition)
    {
        if (!_isPlaybackEnded || IsLiveContent || CurrentChannel == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isEndedSeekRecoverInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            _videoPlayerService.Resume();
            await Task.Delay(180);

            if (IsPlaying)
            {
                _isPlaybackEnded = false;
                return;
            }

            var existingPlayer = _videoPlayerService.GetMediaPlayer();
            if (existingPlayer?.Media != null)
            {
                existingPlayer.Play();
                await Task.Delay(180);
                if (IsPlaying)
                {
                    _isPlaybackEnded = false;
                    return;
                }
            }

            var streamUrl = CurrentChannel?.StreamUrl;
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return;
            }

            await _videoPlayerService.PlayAsync(streamUrl);
            SetPlaybackPosition(targetPosition);

            _isPlaybackEnded = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Ended-seek recover failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isEndedSeekRecoverInProgress, 0);
        }
    }

    private void RaiseSkipOverlay(double deltaSeconds, DateTime nowUtc)
    {
        var shouldResetAggregation =
            _skipAggregationLastUpdatedUtc == DateTime.MinValue ||
            nowUtc - _skipAggregationLastUpdatedUtc > TimeSpan.FromMilliseconds(SkipAggregationWindowMs) ||
            Math.Sign(_skipAggregationSeconds) != Math.Sign(deltaSeconds);

        if (shouldResetAggregation)
        {
            _skipAggregationSeconds = deltaSeconds;
        }
        else
        {
            _skipAggregationSeconds += deltaSeconds;
        }

        _skipAggregationLastUpdatedUtc = nowUtc;
        SkipOverlayRequested?.Invoke(this, new SkipOverlayEventArgs(_skipAggregationSeconds));
    }

    private static double ParseSkipSeconds(object? parameter)
    {
        const double defaultSeconds = 10;

        if (parameter == null)
        {
            return defaultSeconds;
        }

        if (parameter is int intValue)
        {
            return Math.Abs(intValue);
        }

        if (parameter is double doubleValue)
        {
            return Math.Abs(doubleValue);
        }

        if (parameter is string str && double.TryParse(str, out var parsed))
        {
            return Math.Abs(parsed);
        }

        return defaultSeconds;
    }

    private void ReleaseSkipSeekCarryIfSettled(DateTime nowUtc, double currentPosition)
    {
        if (!_hasPendingSkipSeekTarget)
        {
            return;
        }

        if (nowUtc > _pendingSkipSeekExpiresUtc ||
            Math.Abs(currentPosition - _pendingSkipSeekTarget) <= SkipSeekCarryToleranceSeconds)
        {
            ResetSkipSeekCarry();
        }
    }

    private void ResetSkipSeekCarry()
    {
        _hasPendingSkipSeekTarget = false;
        _pendingSkipSeekTarget = 0;
        _pendingSkipSeekExpiresUtc = DateTime.MinValue;
    }

    private void ResetSkipOverlayAggregation()
    {
        _skipAggregationSeconds = 0;
        _skipAggregationLastUpdatedUtc = DateTime.MinValue;
    }

    private void ResetSeekInteractionState()
    {
        ResetSkipSeekCarry();
        ResetSkipOverlayAggregation();
    }

    private void EnableSeekBufferShieldSuppression()
    {
        _suppressBufferShieldForSeek = true;
        OnPropertyChanged(nameof(IsBufferShieldVisible));
        var token = Interlocked.Increment(ref _seekShieldSuppressionToken);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay((int)SeekBufferShieldSuppressionMs).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (token != _seekShieldSuppressionToken)
            {
                return;
            }

            _dispatcherService.BeginInvoke(CancelSeekBufferShieldSuppression);
        });
    }

    private void CancelSeekBufferShieldSuppression()
    {
        var wasSuppressed = _suppressBufferShieldForSeek;
        _suppressBufferShieldForSeek = false;
        
        // Safety check: If we are still buffering after the timeout and it's not live content,
        // force clear the buffering state to avoid getting stuck on black screen.
        if (wasSuppressed && !_isContentTransitioning && IsBuffering && !IsLiveContent)
        {
            IsBuffering = false;
        }

        OnPropertyChanged(nameof(IsBufferShieldVisible));
    }

    private static bool IsEpisodeCompleted(double durationSeconds, double positionSeconds)
    {
        if (durationSeconds <= 0 || positionSeconds <= 0)
        {
            return false;
        }

        var percentReached = (positionSeconds / durationSeconds) * 100.0;
        var remainingSeconds = Math.Max(0, durationSeconds - positionSeconds);

        return percentReached >= EpisodeCompletedPercentThreshold
            || (durationSeconds > EpisodeCompletedTailSeconds && remainingSeconds <= EpisodeCompletedTailSeconds);
    }

    private void PrepareForContentLoading()
    {
        _isContentTransitioning = true;
        _isPlaybackEnded = false;
        _isUserSeeking = false;
        _pendingResumeSeekPosition = 0;
        _pendingResumeSeekAttempts = 0;
        _lastPausedPosition = 0;
        _lastPausedTimeMs = 0;
        Interlocked.Increment(ref _seekShieldSuppressionToken);
        _suppressBufferShieldForSeek = false;
        ResetSeekInteractionState();
        IsPlaying = false;
        Position = 0;
        PositionText = "00:00:00";
        Duration = 0;
        DurationText = "00:00:00";
        RemainingTime = IsLiveContent ? "00:00:00" : "-00:00:00";
        IsBuffering = true;
        BufferingProgress = 0;
        OnPropertyChanged(nameof(IsBufferShieldVisible));
    }

    private void RefreshEpisodeBrowserContext(Series? series)
    {
        _currentSeriesContext = series;
        EpisodesPanelTitle = series?.Name ?? CurrentChannel?.Name ?? string.Empty;

        if (series == null || series.Seasons == null || series.Seasons.Count == 0)
        {
            EpisodeSeasons = new List<Season>();
            return;
        }

        EpisodeSeasons = series.Seasons
            .Where(s => s.Episodes != null && s.Episodes.Count > 0)
            .OrderBy(s => s.SeasonNumber)
            .ToList();
    }

    private Episode? FindNextEpisodeInBrowser(Episode episode)
    {
        if (EpisodeSeasons.Count == 0)
        {
            return null;
        }

        var orderedEpisodes = EpisodeSeasons
            .OrderBy(s => s.SeasonNumber)
            .SelectMany(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
            .ToList();

        var currentIndex = orderedEpisodes.FindIndex(e =>
            e.Id > 0 && episode.Id > 0
                ? e.Id == episode.Id
                : string.Equals(e.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0 || currentIndex + 1 >= orderedEpisodes.Count)
        {
            return null;
        }

        return orderedEpisodes[currentIndex + 1];
    }

    private static string BuildEpisodeIdentity(Episode? episode)
    {
        if (episode == null)
        {
            return string.Empty;
        }

        if (episode.Id > 0)
        {
            return $"id:{episode.Id}";
        }

        if (!string.IsNullOrWhiteSpace(episode.StreamUrl))
        {
            return $"url:{episode.StreamUrl.Trim()}";
        }

        return string.Empty;
    }

    private static bool IsDownloadedStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        var normalized = streamUrl.Trim().Trim('"', '\'');
        if (normalized.Length < 4)
        {
            return false;
        }

        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri))
            {
                return File.Exists(fileUri.LocalPath);
            }

            return File.Exists(normalized);
        }

        if (!Regex.IsMatch(normalized, @"^[a-zA-Z]:[\\/]"))
        {
            return false;
        }

        return File.Exists(normalized);
    }

    private static bool LooksLikeDownloadedPlaybackStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        var normalized = streamUrl.Trim().Trim('"', '\'');
        if (normalized.Length < 4)
        {
            return false;
        }

        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri) &&
            fileUri.IsFile)
        {
            normalized = fileUri.LocalPath;
        }

        var isLocalPath = normalized.StartsWith(@"\\", StringComparison.Ordinal) ||
                          Regex.IsMatch(normalized, @"^[a-zA-Z]:[\\/]");
        if (!isLocalPath)
        {
            return false;
        }

        var lowered = normalized.Replace('/', '\\').ToLowerInvariant();
        if (lowered.EndsWith(".nctra", StringComparison.Ordinal) ||
            lowered.EndsWith(".nctra.part", StringComparison.Ordinal))
        {
            return true;
        }

        if (lowered.Contains(@"\noctra\downloads\profile_", StringComparison.Ordinal))
        {
            return true;
        }

        return File.Exists(normalized);
    }

    private void SetPlaybackPosition(double position)
    {
        var clamped = ClampSeekPosition(position);
        var mediaPlayer = _videoPlayerService.GetMediaPlayer();
        if (mediaPlayer != null)
        {
            var targetTimeMs = (long)Math.Max(0, clamped * 1000);
            mediaPlayer.Time = targetTimeMs;
            return;
        }

        _videoPlayerService.Position = clamped;
    }

    private double ClampSeekPosition(double position)
    {
        if (double.IsNaN(position) || double.IsInfinity(position))
        {
            return Position;
        }

        if (Duration > 0)
        {
            return Math.Clamp(position, 0, Duration);
        }

        return Math.Max(0, position);
    }

    public event EventHandler<Episode>? NextEpisodeRequested;
    public event EventHandler<Episode>? EpisodeRequested;
    public event EventHandler<Episode>? EpisodeProgressUpdated;
    public event EventHandler<SkipOverlayEventArgs>? SkipOverlayRequested;

    [RelayCommand]
    private void OpenEpisodes()
    {
        if (!IsSeriesContent)
        {
            return;
        }

        if (EpisodeSeasons.Count == 0)
        {
            return;
        }

        var isOpening = !IsEpisodesPanelOpen;
        IsAudioSettingsOpen = false;
        IsQualitySettingsOpen = false;
        IsInfoPanelOpen = false;
        IsEpisodesPanelOpen = isOpening;
        IsLocked = isOpening;
        RestartAutoHideTimer();
    }

    [RelayCommand(CanExecute = nameof(CanPlayEpisodeFromOverlay))]
    private void PlayEpisodeFromOverlay(Episode? episode)
    {
        if (episode == null)
        {
            return;
        }

        _currentEpisode = episode;
        CurrentEpisodeIdentity = BuildEpisodeIdentity(episode);
        NextEpisode = FindNextEpisodeInBrowser(episode);
        _creditsTriggered = false;
        IsCreditsZone = false;
        IsNextEpisodePromptVisible = false;
        IsEpisodesPanelOpen = false;
        IsLocked = false;

        PrepareForContentLoading();
        EpisodeRequested?.Invoke(this, episode);
        RestartAutoHideTimer();
    }

    private bool CanPlayEpisodeFromOverlay(Episode? episode)
    {
        return episode != null &&
               !string.IsNullOrWhiteSpace(episode.StreamUrl);
    }

    partial void OnSelectedAudioTrackChanged(int value)
    {
        // Applied directly in SetAudioTrack command.
    }

    partial void OnSelectedSubtitleTrackChanged(int value)
    {
        // Applied directly in SetSubtitleTrack command.
    }

    [RelayCommand]
    private void EnterPiP()
    {
        PiPRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetAudioTrack(int id)
    {
        _videoPlayerService.SetAudioTrack(id);
        SelectedAudioTrack = id;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetSubtitleTrack(int id)
    {
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

    public event EventHandler? CloseRequested;
    public event EventHandler? NextLiveChannelRequested;
    public event EventHandler? PreviousLiveChannelRequested;

    [RelayCommand]
    private void PlayNextLiveChannel()
    {
        NextLiveChannelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PlayPreviousLiveChannel()
    {
        PreviousLiveChannelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UserInteraction() => RestartAutoHideTimer();

    partial void OnIsLockedChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Stop();
            IsVisible = true;
            return;
        }

        RestartAutoHideTimer();
    }

    partial void OnIsAudioSettingsOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Stop();
            IsVisible = true;
            return;
        }

        RestartAutoHideTimer();
    }

    partial void OnIsQualitySettingsOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Stop();
            IsVisible = true;
            return;
        }

        RestartAutoHideTimer();
    }

    partial void OnIsInfoPanelOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Stop();
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
            _autoHideTimer.Stop();
            IsVisible = true;
            return;
        }

        RestartAutoHideTimer();
    }


    partial void OnIsNextEpisodePromptVisibleChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Stop();
            IsVisible = true;
            return;
        }

        RestartAutoHideTimer();
    }

    partial void OnIsEpisodesPanelOpenChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Stop();
            IsVisible = true;
            return;
        }

        CancelSeekBufferShieldSuppression();

        RestartAutoHideTimer();
    }

    private void OnNetworkStatusChanged(object? sender, string status)
    {
        UpdateNetworkStatus(status);
    }

    private void UpdateNetworkStatus(string status)
    {
        NetworkStatus = status;
        NetworkIcon = status switch
        {
            "Ethernet" => "Ethernet",
            "Wi-Fi" => "Wifi",
            "Mobil veri" => "SignalCellular4Bar",
            "Offline" => "WifiOff",
            _ => "Web"
        };
    }

    public void Dispose()
    {
        _autoHideTimer?.Dispose();
        _clockTimer?.Dispose();
        _watchHistoryTimer?.Dispose();
        if (_networkService != null)
        {
            _networkService.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
    }
}



