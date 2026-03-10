using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;

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
    private const double SeekBufferShieldSuppressionMs = 8000;
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

    private enum PlaybackRecoveryState
    {
        None = 0,
        LiveAutoRecovering = 1,
        EndedSeekRecovering = 2
    }

    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IEpgService _epgService;
    private readonly IMetadataService _metadataService;
    private readonly IMediaService _mediaService;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly INetworkService _networkService;
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isPiPControlsForceVisible;

    public bool IsPiPControlsVisible => IsPiPMode && _isPiPControlsForceVisible;

    partial void OnIsPiPModeChanged(bool value)
    {
        if (value)
        {
            IsPiPControlsForceVisible = true;
            IsVisible = true;
            RestartAutoHideTimer();
        }
        else
        {
            IsPiPControlsForceVisible = false;
            IsVisible = true;
            RestartAutoHideTimer();
        }
        OnPropertyChanged(nameof(IsPiPControlsVisible));
    }

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
    private string _playerLoadingWarningMessage = string.Empty;


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

    [ObservableProperty]
    private Episode? _currentEpisode;

    private bool _creditsTriggered;
    private bool _isUserSeeking;
    private bool _hasPendingSkipSeekTarget;
    private double _pendingSkipSeekTarget;
    private DateTime _pendingSkipSeekExpiresUtc = DateTime.MinValue;
    private double _skipAggregationSeconds;
    private DateTime _skipAggregationLastUpdatedUtc = DateTime.MinValue;
    private bool _isPlaybackEnded;
    private int _recoveryState; // PlaybackRecoveryState
    private double _lastPausedPosition;
    private long _lastPausedTimeMs;
    private double _pendingResumeSeekPosition;
    private int _pendingResumeSeekAttempts;
    private double _lastKnownValidPosition;
    private int _prematureEndRecoveryCount;
    private DateTime _lastPrematureEndRecoveryUtc = DateTime.MinValue;
    private const int MaxPrematureEndRecoveries = 5;
    private static readonly TimeSpan PrematureEndRecoveryCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PrematureEndRecoveryWindowReset = TimeSpan.FromMinutes(2);
    private int _isPlayPauseInProgress;
    private int _isClockProcessing;
    private bool _isIntentionallyPaused;
    private bool _livePauseRequiresHardRestart;
    private double _lastLiveObservedPosition = -1;
    private DateTime _lastLiveProgressAtUtc = DateTime.MinValue;
    private DateTime _lastLivePositionEventAtUtc = DateTime.MinValue;
    private DateTime _lastLiveAutoRecoverAttemptAtUtc = DateTime.MinValue;
    private DateTime _liveRecoveryWindowStartUtc = DateTime.MinValue;
    private int _liveRecoveryAttemptsInWindow;
    private int _liveStallScore;
    private int _volumeBeforeMute = 100;
    private bool _suppressBufferShieldForSeek;
    private int _seekShieldSuppressionToken;
    private int _seekVerifyToken;
    private long _lastSeekTargetMs = -1;
    private Series? _currentSeriesContext;
    private bool _isContentTransitioning;
    private bool _isUpdatingFromService;
    private int _isDownloadActionRunning;

    // Stall Detection fields
    private long _lastStallCheckTimeMs;
    private int _stallCounter;
    private const int StallThreshold = 25; // x120ms = ~3sn
    private readonly IDispatcherService _dispatcherService;
    private DateTime _lastWatchHistoryUpdateUtc = DateTime.MinValue;
    private readonly IWatchHistoryService? _watchHistoryService;
    private readonly System.Threading.Timer _autoHideTimer;
    private readonly System.Timers.Timer _clockTimer;
    private readonly System.Timers.Timer _watchHistoryTimer;

    internal void LogDebug(string msg) {
        Task.Run(() => {
            try {
                File.AppendAllText(@"d:\IPTVPlayer\vlc_debug_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [PVM] {msg}\n");
            } catch { }
        });
    }

    public int? CurrentProfileId { get; set; }

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
        IWatchHistoryService? watchHistoryService = null)
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
        _watchHistoryService = watchHistoryService;

        // Initialize Network Status

        UpdateNetworkStatus(_networkService.CurrentNetworkStatus);
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;

        _settingsService.SettingsChanged += OnSettingsChanged;

        // Auto-hide timer
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

        // Clock timer
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

                // Run background health and update tasks concurrently
                // This prevents a failure in one (e.g. EPG update) from blocking the other (Live health check)
                await Task.WhenAll(
                    Task.Run(async () => {
                        try { await CheckForEpgUpdateAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] EPG update failed: {ex.Message}"); }
                    }),
                    Task.Run(async () => {
                        try { await MonitorLivePlaybackHealthAsync(); }
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
        };        _clockTimer.Start();
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
                    RestartAutoHideTimer();
                }
            });
        };

        _videoPlayerService.PlaybackEnded += (_, _) =>
        {
            _dispatcherService.Invoke(() =>
            {
                if (_isContentTransitioning) return;

                _isPlaybackEnded = true;

                // Erken bitiş tespiti (Premature End Analysis) & Canlı Yayın Kopması
                var duration = _videoPlayerService.Duration;
                var currentPos = Position;
                
                // Canlı yayın `EndReached` atıyorsa bu direkt bağlantı kopmasıdır, hemen kurtar.
                // VOD ise ve bitime 10 saniyeden fazla varsa bu erken bitiştir (kopmadır), kurtar.
                bool isPrematureEnd = (IsLiveContent) || (!IsLiveContent && duration > 0 && (duration - currentPos) > 10);

                if (isPrematureEnd)
                {
                    var now = DateTime.UtcNow;
                    // Reset counter if enough time has passed since last issue
                    if (now - _lastPrematureEndRecoveryUtc > PrematureEndRecoveryWindowReset)
                        _prematureEndRecoveryCount = 0;

                    // Check if we've exceeded max attempts
                    if (_prematureEndRecoveryCount >= MaxPrematureEndRecoveries)
                    {
                        LogDebug($"VM: PREMATURE END recovery limit reached ({MaxPrematureEndRecoveries}). Giving up.");
                        ConnectionStatus = "Yayın kararsız — bağlantı sorunlu.";
                        return;
                    }

                    // Cooldown check — prevent rapid-fire recovery
                    if (now - _lastPrematureEndRecoveryUtc < PrematureEndRecoveryCooldown)
                    {
                        LogDebug("VM: PREMATURE END cooldown active, skipping recovery.");
                        return;
                    }

                    _prematureEndRecoveryCount++;
                    _lastPrematureEndRecoveryUtc = now;
                    var lastValid = _lastKnownValidPosition > 1 ? _lastKnownValidPosition : currentPos;
                    LogDebug($"VM: PREMATURE END DETECTED. Live: {IsLiveContent}, Pos/Dur: {currentPos}/{duration}s (Valid: {lastValid}). Suspected server truncation. Attempt {_prematureEndRecoveryCount}/{MaxPrematureEndRecoveries}");
                    
                    _ = AutoRecoverPrematureEndAsync(lastValid);
                    return; // Auto-recovering, do not show next episode prompt
                }
                
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
                if (_isIntentionallyPaused || IsDownloadedPlayback)
                {
                    // For offline files and intentional pauses, VLC still sends Buffering(0) which causes IsBuffering to stuck at true
                    // if we check !IsPlaying. Thus, only depend on progress < 100f for these cases.
                    IsBuffering = progress < 100f;
                }
                else
                {
                    IsBuffering = !IsPlaying || progress < 100f;
                }

                // Buffering bittiğinde kontrol katmanını mutlaka geri getir.
                if (!IsBuffering)
                {
                    PlayerLoadingWarningMessage = string.Empty;

                    IsVisible = true;
                    
                    if (IsPiPMode)
                    {
                        IsPiPControlsForceVisible = true;
                        OnPropertyChanged(nameof(IsPiPControlsVisible));
                    }
                    
                    RestartAutoHideTimer();
                }
            });
        };

        _videoPlayerService.ErrorOccurred += (s, errorMessage) =>
        {
            _dispatcherService.Invoke(() =>
            {
                PlayerLoadingWarningMessage = string.Empty;

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

                    if (pos > 1 && !IsBuffering && !_isContentTransitioning)
                    {
                        _lastKnownValidPosition = pos;
                    }

                    if (!IsLiveContent && Duration > 0)
                    {
                        var remaining = Math.Max(0, Duration - pos);
                        RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
                        CheckIntroCreditsPosition(pos);
                    }

                    TryApplyPendingResumeSeek();
                }

                // Heartbeat / Stall Monitor: Oynuyor görünürken ilerlemiyorsa logla
                if (IsPlaying && !IsBuffering && !_isUserSeeking && !_isContentTransitioning)
                {
                    var mediaPlayer = _videoPlayerService.GetMediaPlayer();
                    var currentTimeMs = mediaPlayer?.Time ?? 0;

                    if (currentTimeMs > 0 && currentTimeMs == _lastStallCheckTimeMs)
                    {
                        _stallCounter++;
                        if (_stallCounter == StallThreshold)
                        {
                            LogDebug($"STALL DETECTED: Heartbeat stopped at {pos}s (TimeMs: {currentTimeMs})");
                        }
                    }
                    else
                    {
                        _stallCounter = 0;
                    }
                    _lastStallCheckTimeMs = currentTimeMs;
                }

                ReleaseSkipSeekCarryIfSettled(nowUtc, pos);
                });        };

        // Initialize volume from service
        Volume = _videoPlayerService.Volume;
        _volumeBeforeMute = Volume > 0 ? Volume : 50;

        _videoPlayerService.VolumeChanged += (s, vol) =>
        {
            _dispatcherService.Invoke(() =>
            {
                if (_volume != vol)
                {
                    _isUpdatingFromService = true;
                    Volume = vol;
                    _isUpdatingFromService = false;
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
            _isIntentionallyPaused = false;
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

    private async Task AutoRecoverPrematureEndAsync(double lastPos)
    {
        LogDebug($"AutoRecoverPrematureEnd: Reconnecting silently to let proxy clear...");
        IsBuffering = true;

        var requestVersion = _playRequestVersion;
        await Task.Delay(500);

        // If user hasn't clicked Stop or changed channel
        if (CurrentChannel == null || _isContentTransitioning || requestVersion != _playRequestVersion) return;

        LogDebug($"AutoRecoverPrematureEnd: Executing ResumePlaybackAsync from {lastPos}s");        _lastPausedPosition = lastPos;
        _isPlaybackEnded = false;
        await ResumePlaybackAsync(CurrentChannel.StreamUrl, false);
    }

    public async Task PlayChannelAsync(Channel channel, double? startPosition = null)
    {
        LogDebug($"PlayChannelAsync: Id={channel.Id}, Name={channel.Name}, Type={channel.Type}, StreamUrl={channel.StreamUrl}, StartPos={startPosition}");

        // Stalker Dizileri için özel kontrol: Stalker dizilerinin ana linki (cmd) yoktur, oynatılamazlar.
        // Kullanıcıya bölümlere gitmesi gerektiğini belirten bir hata fırlatıyoruz.
        if (channel.StreamUrl != null && channel.StreamUrl.StartsWith("stalker-series://"))
        {
            throw new InvalidOperationException("Bu bir dizi klasörüdür. Lütfen bölümleri görmek için dizinin detayına gidin.");
        }

        var requestVersion = Interlocked.Increment(ref _playRequestVersion);

        // Force previous media to stop so stale position events do not leak into the next item.
        _videoPlayerService.Stop();

        _isContentTransitioning = true;
        IsBuffering = true;

        if (IsPiPMode)
        {
            IsPiPControlsForceVisible = true;
            OnPropertyChanged(nameof(IsPiPControlsVisible));
        }

        CurrentChannel = channel;
        CurrentProgram = GetFallbackProgram();
        IsLiveContent = channel.Type == ChannelType.Live;
        IsSeriesContent = channel.Type == ChannelType.Series;
        UpdateOverlaySecondaryText();
        PrepareForContentLoading();
        
        if (startPosition.HasValue && startPosition.Value > 0)
        {
            _lastPausedPosition = startPosition.Value;
            _lastPausedTimeMs = (long)(startPosition.Value * 1000);
            _pendingResumeSeekPosition = startPosition.Value;
        }
        else
        {
            _lastPausedPosition = 0;
            _lastPausedTimeMs = 0;
            _pendingResumeSeekPosition = 0;
        }

        StreamQuality = null;
        StreamInfo = "Kalite tespit ediliyor...";
        try
        {
            var resolvedStreamUrl = await _contentDownloadService.ResolvePlayableUrlAsync(channel.StreamUrl);
            
            if (startPosition.HasValue && startPosition.Value > 0 && !IsDownloadedPlayback && resolvedStreamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // HTTP stream'ler için baştan `startTime` argümanı ile oynat
                LogDebug($"PlayChannelAsync: Starting HTTP stream with start-time={startPosition.Value}");
                await _videoPlayerService.PlayAsync(resolvedStreamUrl, startPosition.Value);
            }
            else
            {
                await _videoPlayerService.PlayAsync(resolvedStreamUrl);
            }
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
            _lastWatchHistoryUpdateUtc = DateTime.UtcNow;
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
        if (channel.Type == ChannelType.Series)
        {
            try
            {
                // Sadece caller (MainViewModel.SelectMedia vb.) tarafından zaten zengin bir seri bağlamı 
                // sağlanmamışsa veya boşsa veritabanından tekrar resolve etmeyi deniyoruz.
                // Aksi takdirde, içinde Episode'lar olan zengin nesneyi eziyor ve Episodes panelini bozuyordu.
                if (_currentSeriesContext == null || _currentSeriesContext.Seasons == null || _currentSeriesContext.Seasons.Count == 0)
                {
                    var allSeries = await _mediaService.GetSeriesAsync(channel.PlaylistId);
                    var matchedSeries = allSeries.FirstOrDefault(s => 
                        s.Name.Equals(channel.Name, StringComparison.OrdinalIgnoreCase) ||
                        (channel.GroupTitle != null && s.Name.Equals(channel.GroupTitle, StringComparison.OrdinalIgnoreCase)));
                    
                    if (matchedSeries != null && requestVersion == _playRequestVersion && CurrentChannel?.Id == channel.Id)
                    {
                        _currentSeriesContext = matchedSeries;
                        RefreshEpisodeBrowserContext(matchedSeries);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to resolve series context: {ex.Message}");
            }
        }

        // Oynatma sağlık kontrolü: 5sn içinde başlamadıysa otomatik yeniden dene.
        _ = EnsurePlaybackHealthAsync(channel, requestVersion);
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

        var nowUtc = DateTime.UtcNow;
        var delta = _lastWatchHistoryUpdateUtc == DateTime.MinValue 
            ? TimeSpan.Zero 
            : nowUtc - _lastWatchHistoryUpdateUtc;
            
        _lastWatchHistoryUpdateUtc = nowUtc;

        await FlushWatchHistoryAsync(force: false, incrementDelta: delta);
    }

    private async Task FlushWatchHistoryAsync(bool force, TimeSpan? incrementDelta = null)
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
            var isEpisodePlayback = CurrentChannel.Type == ChannelType.Series && CurrentEpisode?.Id > 0;
            var channelId = !isEpisodePlayback && CurrentChannel.Id > 0 ? CurrentChannel.Id : (int?)null;
            var livePosition = Math.Max(0, _videoPlayerService.Position);
            var currentPosition = TimeSpan.FromSeconds(Math.Max(Position, livePosition));
            var currentDuration = Duration > 0 ? TimeSpan.FromSeconds(Duration) : (TimeSpan?)null;
            var isCompleted = IsEpisodeCompleted(Duration, Position);

            await _watchHistoryService.TrackWatchAsync(
                CurrentProfileId.Value,
                channelId,
                isEpisodePlayback ? CurrentEpisode!.Id : null,
                currentPosition,
                isCompleted,
                currentDuration,
                incrementDelta
            );

            if (isEpisodePlayback && CurrentEpisode != null)
            {
                var finalCompleted = CurrentEpisode.IsCompleted || isCompleted;
                _dispatcherService.BeginInvoke(() =>
                {
                    CurrentEpisode.LastWatched = DateTime.UtcNow;
                    CurrentEpisode.WatchedPosition = finalCompleted && currentDuration.HasValue
                        ? currentDuration.Value
                        : currentPosition;
                    CurrentEpisode.IsCompleted = finalCompleted;
                    if (currentDuration.HasValue)
                    {
                        CurrentEpisode.Duration = currentDuration.Value;
                    }

                    RefreshEpisodeBrowserContext(_currentSeriesContext);
                    EpisodeProgressUpdated?.Invoke(this, CurrentEpisode);
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

        if (DateTime.UtcNow > CurrentProgram.EndTime || isFallback)
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
        // INTENTIONAL KILLSWITCH: VLC does not reliable fire PositionChanged for live streams,
        // causing this monitor to falsely detect a stall and restart exactly every 5 seconds.
        // We now rely on EndReached/EncounteredError combined with AutoRecoverPrematureEndAsync.
        await Task.CompletedTask;
    }

    private EpgProgram GetFallbackProgram()
    {
        var now = DateTime.UtcNow;
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

        StreamInfo = StreamQuality.ResolutionLabel;
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
        IsVisible = true;
        if (CanAutoHideOverlay())
        {
            _autoHideTimer.Change((int)OverlayAutoHideDelayMs, Timeout.Infinite);
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
        LogDebug("UI Action: OpenAudioSettings clicked");
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
        LogDebug("UI Action: OpenQualitySettings clicked");
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
        LogDebug("UI Action: OpenInfoPanel clicked");
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

        var audioTracks = _videoPlayerService.AudioTracks
            .Where(t => t.Id >= 0 && !IsDisabledTrackLabel(t.Name))
            .Select(t => new TrackOption(t.Id, NormalizeTrackName(t.Name, $"Ses {t.Id}")))
            .ToList();

        var subtitleTracks = _videoPlayerService.SubtitleTracks
            .Select(t => IsDisabledTrackLabel(t.Name)
                ? new TrackOption(t.Id, "Kapalı")
                : new TrackOption(t.Id, NormalizeTrackName(t.Name, $"Altyazı {t.Id}")))
            .ToList();

        if (!subtitleTracks.Any(t => string.Equals(t.Name, "Kapalı", StringComparison.OrdinalIgnoreCase)))
        {
            subtitleTracks.Add(new TrackOption(-1, "Kapalı"));
        }

        AudioTracks = audioTracks;
        SubtitleTracks = subtitleTracks;
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

        // Remove URLs and domain names (e.g. Filmbol.org, example.com)
        name = Regex.Replace(
            name,
            @"https?://\S+|www\.\S+|\b[\w-]+\.(org|com|net|info|tv|io|cc|me|co|xyz)\b",
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
        var delays = new[] { 250, 800, 1600, 3000 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay);
            if (!IsPlaying || CurrentChannel == null)
            {
                return;
            }

            _dispatcherService.Invoke(UpdateMediaInfo);

            // Wait until BOTH audio and at least one real subtitle track are found
            // Or if we already hit the 1600ms delay, we proceed anyway
            if (AudioTracks.Any(t => t.Id >= 0) && (SubtitleTracks.Any(t => t.Id >= 0) || delay >= 1600))
            {
                return;
            }
        }
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        LogDebug($"UI Action: PlayPause clicked (Current IsPlaying={IsPlaying})");
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
            _isIntentionallyPaused = true;
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
            _isIntentionallyPaused = false;
            var mediaPlayer = _videoPlayerService.GetMediaPlayer();
            var state = mediaPlayer?.State ?? LibVLCSharp.Shared.VLCState.NothingSpecial;
            var isStreamDead = state == LibVLCSharp.Shared.VLCState.Stopped || 
                               state == LibVLCSharp.Shared.VLCState.Ended || 
                               state == LibVLCSharp.Shared.VLCState.Error || 
                               state == LibVLCSharp.Shared.VLCState.NothingSpecial;
            
            if (isStreamDead) 
            {
                LogDebug($"PlayPause: Stream is dead (State: {state}), initiating fresh play. _lastKnownValidPosition: {_lastKnownValidPosition}");
                if (_lastPausedPosition <= 1 && _lastKnownValidPosition > 1)
                {
                    _lastPausedPosition = _lastKnownValidPosition;
                }
            }

            var isResumable = mediaPlayer?.Media != null && !isStreamDead;
            await ResumePlaybackAsync(CurrentChannel.StreamUrl, isResumable);
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
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                await _videoPlayerService.PlayAsync(streamUrl);
            }

            return;
        }

        var targetPosition = _lastPausedPosition;
        // Slight forward compensation to offset keyframe-based resume landing behind target.
        var targetTimeMs = (_lastPausedTimeMs > 0 ? _lastPausedTimeMs : (long)(targetPosition * 1000)) + 350;
        
        // Prefer native resume for smoothness if media is loaded
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

            // VLC's internal seek freezes on HTTP VOD streams, so use HardSeek
            if (!IsDownloadedPlayback && streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug($"ResumePlaybackAsync: Significant drift detected, using HardSeekAsync for HTTP stream to {targetPosition}s");
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                await _videoPlayerService.HardSeekAsync(targetPosition);
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
            else
            {
                // Local files can use the standard pending seek loop
                _pendingResumeSeekPosition = targetPosition;
                _pendingResumeSeekAttempts = 0;
            }
        }
        else
        {
            // Oynatıcı henüz yüklenmediyse
            if (!IsDownloadedPlayback && streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug($"ResumePlaybackAsync: Fresh play for HTTP stream, passing startTime={targetPosition}s to PlayAsync");
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                await _videoPlayerService.PlayAsync(streamUrl, targetPosition);
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
            else
            {
                // Local files will seek manually via TryApplyPendingResumeSeek
                _pendingResumeSeekPosition = targetPosition;
                _pendingResumeSeekAttempts = 0;
                await _videoPlayerService.PlayAsync(streamUrl);
            }
        }

        // Only local files reach this 12-attempt loop (HTTP streams return early above)
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
        LogDebug($"TryApplyPendingResumeSeek: Current Position={Position}, PendingResumeSeekPosition={_pendingResumeSeekPosition}, PendingTimeMs={pendingTimeMs}");

        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            if (mediaPlayer.Time + 1000 >= pendingTimeMs)
            {
                LogDebug($"TryApplyPendingResumeSeek: mediaPlayer.Time ({mediaPlayer.Time}) is close enough to pendingTimeMs ({pendingTimeMs}). Resetting pending seek.");
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                return;
            }
        }

        if (Position + 1 >= _pendingResumeSeekPosition)
        {
            LogDebug($"TryApplyPendingResumeSeek: Position ({Position}) is close enough to PendingResumeSeekPosition ({_pendingResumeSeekPosition}). Resetting pending seek.");
            _pendingResumeSeekPosition = 0;
            _pendingResumeSeekAttempts = 0;
            return;
        }

        if (_pendingResumeSeekAttempts >= 20)
        {
            LogDebug($"TryApplyPendingResumeSeek: Max attempts reached ({_pendingResumeSeekAttempts}). Resetting pending seek.");
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
                LogDebug($"TryApplyPendingResumeSeek: Drift ({driftMs}ms) is within tolerance. Resetting pending seek.");
                _pendingResumeSeekPosition = 0;
                _pendingResumeSeekAttempts = 0;
                return;
            }
        }

        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            var duration = _videoPlayerService.Duration;
            if (duration > 0)
            {
                var fraction = (float)(_pendingResumeSeekPosition / duration);
                LogDebug($"TryApplyPendingResumeSeek: Setting mediaPlayer.Position fraction={fraction} for {pendingTimeMs}ms");
                mediaPlayer.Position = Math.Clamp(fraction, 0f, 1f);
            }
            else
            {
                LogDebug($"TryApplyPendingResumeSeek: Duration is 0, falling back to Time={pendingTimeMs}");
                mediaPlayer.Time = pendingTimeMs;
            }
        }
        else
        {
            LogDebug($"TryApplyPendingResumeSeek: Setting Position setter to {_pendingResumeSeekPosition}");
            _videoPlayerService.Position = _pendingResumeSeekPosition;
        }
    }

    [RelayCommand]
    private async Task Stop()
    {
        LogDebug("UI Action: Stop clicked");
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

    [RelayCommand]
    private void ToggleMute()
    {
        LogDebug($"UI Action: ToggleMute clicked (Current IsMuted={IsMuted})");
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
        LogDebug("StartSeeking invoked.");
        if (Interlocked.CompareExchange(ref _recoveryState, 0, 0) != (int)PlaybackRecoveryState.None)
            return;

        _isUserSeeking = true;
    }

    [RelayCommand]
    private void Seek(double position)
    {
        LogDebug($"Seek invoked with value: {position}");
        _isUserSeeking = false;

        if (Interlocked.CompareExchange(ref _recoveryState, 0, 0) != (int)PlaybackRecoveryState.None)
            return;

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
        CurrentEpisode = episode;
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

        if (_settingsService.Settings.AutoPlayNext && !_isUserSeeking)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                if (IsNextEpisodePromptVisible && !_isUserSeeking)
                {
                    _dispatcherService.Invoke(() =>
                    {
                        _ = PlayNextEpisodeCommand.ExecuteAsync(null);
                    });
                }
            });
        }
    }

    /// <summary>
    /// Position bazlı intro/credits tespiti
    /// </summary>
    private void CheckIntroCreditsPosition(double pos)
    {
        if (CurrentEpisode == null || IsLiveContent || NextEpisode == null)
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

        if (_settingsService.Settings.AutoPlayNext && !_isUserSeeking)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                if (IsNextEpisodePromptVisible && !_isUserSeeking)
                {
                    _dispatcherService.Invoke(() =>
                    {
                        _ = PlayNextEpisodeCommand.ExecuteAsync(null);
                    });
                }
            });
        }
    }

    private bool TryGetCreditsTriggerThreshold(out double triggerAt)
    {
        triggerAt = 0;
        if (CurrentEpisode == null)
        {
            return false;
        }

        var hasDuration = Duration > 0;
        var fallbackFiveMinuteTrigger = hasDuration
            ? Math.Max(0, Duration - EpisodeCompletedTailSeconds)
            : double.MaxValue;

        if (CurrentEpisode.CreditsStartSec is double creditsStartSec && creditsStartSec > 0)
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
                CurrentEpisode?.Name ?? channel.Name,
                CurrentEpisode?.StreamUrl ?? channel.StreamUrl,
                poster,
                playlistId,
                channel.Id,
                CurrentEpisode?.Id ?? 0,
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

        if (Interlocked.CompareExchange(ref _recoveryState, (int)PlaybackRecoveryState.EndedSeekRecovering, (int)PlaybackRecoveryState.None) != (int)PlaybackRecoveryState.None)
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
            Interlocked.Exchange(ref _recoveryState, (int)PlaybackRecoveryState.None);
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

    private TaskCompletionSource<bool>? _resumeDialogTcs;

    [ObservableProperty] private bool _isResumeDialogVisible;
    [ObservableProperty] private string _resumePositionText = string.Empty;
    [ObservableProperty] private bool _isPremiumResume;

    public event EventHandler? PremiumUpsellRequested;

    [RelayCommand]
    private void OpenPremiumUpsell()
    {
        PremiumUpsellRequested?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> ShowResumeDialogAsync(double positionSeconds)
    {
        ResumePositionText = TimeSpan.FromSeconds(positionSeconds).ToString(@"hh\:mm\:ss");
        IsPremiumResume = _licenseService.IsFeatureAvailable(LicenseService.Features.ResumePlayback);
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
    }

    [RelayCommand]
    private void ResumeFromPosition()
    {
        IsResumeDialogVisible = false;
        _resumeDialogTcs?.TrySetResult(true);
        _resumeDialogTcs = null;
    }

    [RelayCommand]
    private void StartFromBeginning()
    {
        IsResumeDialogVisible = false;
        _resumeDialogTcs?.TrySetResult(false);
        _resumeDialogTcs = null;
    }

    private void PrepareForContentLoading()
    {
        CancelResumeDialog();
        _isContentTransitioning = true;
        _isPlaybackEnded = false;
        _isUserSeeking = false;
        _pendingResumeSeekPosition = 0;
        _pendingResumeSeekAttempts = 0;
        _lastPausedPosition = 0;
        _lastPausedTimeMs = 0;
        _prematureEndRecoveryCount = 0;
        _isIntentionallyPaused = false;
        _lastPrematureEndRecoveryUtc = DateTime.MinValue;
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
        
        PlayerLoadingWarningMessage = string.Empty;

        OnPropertyChanged(nameof(IsBufferShieldVisible));
    }



    private void RefreshEpisodeBrowserContext(Series? series)
    {
        _currentSeriesContext = series;
        EpisodesPanelTitle = series?.Name ?? CurrentChannel?.Name ?? string.Empty;

        var sourceSeasons = series?.Seasons;

        if (sourceSeasons == null || sourceSeasons.Count == 0)
        {
            EpisodeSeasons = new List<Season>();
            return;
        }

        var newSeasons = sourceSeasons
            .Where(s => s.Episodes != null && s.Episodes.Count > 0)
            .OrderBy(s => s.SeasonNumber)
            .ToList();

        foreach (var season in newSeasons)
        {
            season.IsExpanded = season.Episodes.Any(e => BuildEpisodeIdentity(e) == CurrentEpisodeIdentity);
        }

        EpisodeSeasons = newSeasons;
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

        // Handle file:// URIs
        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri))
            {
                return fileUri.IsFile && File.Exists(fileUri.LocalPath);
            }
        }

        // Handle Windows UNC paths or local drive paths
        var isLocal = normalized.StartsWith(@"\\", StringComparison.Ordinal) ||
                      Regex.IsMatch(normalized, @"^[a-zA-Z]:[\\/]");
        
        if (isLocal)
        {
            return File.Exists(normalized);
        }

        // Handle Unix-style absolute paths
        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return File.Exists(normalized);
        }

        return false;
    }

    private static bool LooksLikeDownloadedPlaybackStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        // If it's a known local file, it's a download candidate
        if (IsDownloadedStreamUrl(streamUrl))
        {
            return true;
        }

        var normalized = streamUrl.Trim().Trim('"', '\'').ToLowerInvariant();
        
        // Even if file doesn't exist yet (e.g. in progress), if it's in our download folder structure
        if (normalized.Contains(@"\noctra\downloads\profile_") || normalized.Contains("/noctra/downloads/profile_"))
        {
            return true;
        }

        return false;
    }

    private void SetPlaybackPosition(double position)
    {
        var clamped = ClampSeekPosition(position);
        
        // Aynı pozisyona çift seek gönderme koruması
        var targetTimeMs = (long)Math.Max(0, clamped * 1000);
        if (targetTimeMs == _lastSeekTargetMs) return;
        _lastSeekTargetMs = targetTimeMs;

        LogDebug($"SetPlaybackPosition: position={position}, clamped={clamped}");

        // 1. Internet yayını ise (Hard Seek)
        // XTream Codes vb. sunucularda HTTP üzerinden Range request (Native Seek) atıldığında
        // sunucu bağlantıyı koparabiliyor (EndReached) veya 20 saniye dondurabiliyor.
        // Bu yüzden ağ yayınlarında bağlantıyı kapatıp açan HardSeekAsync kullanıyoruz.
        if (!IsDownloadedPlayback && _videoPlayerService.CurrentUrl?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
        {
            LogDebug($"SetPlaybackPosition: HTTP stream detected, executing HardSeekAsync to {clamped}s");
            _ = _videoPlayerService.HardSeekAsync(clamped);
            // HardSeekAsync explicitly stops and restarts the stream to avoid freezes on HTTP servers.
            // Volume is preserved by VideoPlayerService through the restart.
            return;
        }

        // 2. Local/Downloaded playback
        var duration = _videoPlayerService.Duration > 0 ? _videoPlayerService.Duration : this.Duration;

        var targetFraction = duration > 0 ? (float)(clamped / duration) : 0f;
        if (targetFraction < 0f) targetFraction = 0f;
        if (targetFraction > 1f) targetFraction = 1f;

        var mediaPlayer = _videoPlayerService.GetMediaPlayer();
        if (mediaPlayer != null)
        {
            if (duration > 0)
            {
                LogDebug($"SetPlaybackPosition: Executing internal Position seek to {targetFraction} (Time: {targetTimeMs}ms)");
                mediaPlayer.Position = targetFraction;
            }
            else
            {
                LogDebug($"SetPlaybackPosition: Fallback executing internal Time seek to {targetTimeMs}ms");
                mediaPlayer.Time = targetTimeMs;
            }
            return;
        }

        _videoPlayerService.Position = clamped;
    }


    private async Task EnsurePlaybackHealthAsync(Channel channel, int requestVersion)
    {
        const int retryCountdownSeconds = 5;
        const int maxAttempts = 4;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Geri sayım göster
            for (int remaining = retryCountdownSeconds; remaining > 0; remaining--)
            {
                await Task.Delay(1000);

                if (IsHealthCheckCancelled(channel, requestVersion))
                    return;

                if (IsPlaying)
                {
                    _dispatcherService.Invoke(() => PlayerLoadingWarningMessage = string.Empty);
                    return;
                }

                // Seek buffer shield aktifse restart yapma - normal buffer bekle
                if (_suppressBufferShieldForSeek)
                {
                    _dispatcherService.Invoke(() => PlayerLoadingWarningMessage = string.Empty);
                    return;
                }

                var msg = $"{remaining} saniye içinde yeniden denenecek...";
                _dispatcherService.Invoke(() => PlayerLoadingWarningMessage = msg);
            }

            // Yeniden deneme öncesi son kontroller
            if (IsHealthCheckCancelled(channel, requestVersion) || IsPlaying || _suppressBufferShieldForSeek)
                return;

            // Yeniden deneniyor
            _dispatcherService.Invoke(() =>
            {
                PlayerLoadingWarningMessage = $"Yeniden bağlanılıyor... ({attempt + 1}/{maxAttempts})";
                ConnectionStatus = "Tekrar bağlanılıyor...";
                IsBuffering = true;
                BufferingProgress = 0;
            });

            _videoPlayerService.Stop();
            await Task.Delay(200);

            if (IsHealthCheckCancelled(channel, requestVersion))
                return;

            try
            {
                var resolvedUrl = await _contentDownloadService.ResolvePlayableUrlAsync(channel.StreamUrl);
                await _videoPlayerService.PlayAsync(resolvedUrl);

                _dispatcherService.Invoke(() => PlayerLoadingWarningMessage = string.Empty);
            }
            catch
            {
                // Bir sonraki denemeye geç
            }
        }

        // Tüm denemeler başarısız olduysa uyarı mesajlarını göster
        if (IsHealthCheckCancelled(channel, requestVersion) || IsPlaying)
            return;

        _dispatcherService.Invoke(() =>
            PlayerLoadingWarningMessage = "Bağlantı normalden uzun sürüyor...");

        await Task.Delay(7_000);

        if (IsHealthCheckCancelled(channel, requestVersion) || IsPlaying)
            return;

        _dispatcherService.Invoke(() =>
            PlayerLoadingWarningMessage = "Yayına erişilemiyor olabilir. Başka bir kanal deneyin.");
    }

    private bool IsHealthCheckCancelled(Channel channel, int requestVersion)
    {
        if (requestVersion != _playRequestVersion || CurrentChannel?.Id != channel.Id)
            return true;
        if (_isIntentionallyPaused || Interlocked.CompareExchange(ref _isPlayPauseInProgress, 0, 0) == 1)
            return true;
        if (_livePauseRequiresHardRestart)
            return true;
        
        return false;
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
        LogDebug("UI Action: OpenEpisodes clicked");
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

        CurrentEpisode = episode;
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

    public event EventHandler? CloseRequested;
    public event EventHandler? NextLiveChannelRequested;
    public event EventHandler? PreviousLiveChannelRequested;

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


    private void OnSettingsChanged()
    {
        // Settings synced directly via VideoPlayerService or components now
    }

    public void Dispose()
    {
        _autoHideTimer?.Dispose();
        _clockTimer?.Dispose();
        _watchHistoryTimer?.Dispose();
        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }
        if (_networkService != null)
        {
            _networkService.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
    }
}



