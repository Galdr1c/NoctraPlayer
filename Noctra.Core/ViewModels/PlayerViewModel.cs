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
public partial class PlayerViewModel : ObservableObject
{
    public sealed record TrackOption(int Id, string Name);

    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IEpgService _epgService;
    private readonly IMetadataService _metadataService;
    private int _playRequestVersion;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isLiveContent;

    [ObservableProperty]
    private bool _isSeriesContent;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _currentTimeStr = "00:00";

    [ObservableProperty]
    private bool _isZappingVisible;

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

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private bool _isAudioSettingsOpen;

    [ObservableProperty]
    private string _networkStatus = "Wi-Fi";

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
    private Models.StreamQualityInfo? _streamQuality;

    public bool HasTopQualityBadgesReady =>
        StreamQuality != null &&
        StreamQuality.Height > 0 &&
        StreamQuality.Fps > 0 &&
        !string.IsNullOrWhiteSpace(StreamQuality.VideoCodecDisplay);

    [ObservableProperty]
    private bool _isInfoPanelOpen;

    [ObservableProperty]
    private Episode? _nextEpisode;

    [ObservableProperty]
    private bool _isNextEpisodePromptVisible;

    [ObservableProperty]
    private bool _isCreditsZone;

    private Episode? _currentEpisode;
    private bool _creditsTriggered;
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
        IDispatcherService dispatcherService,
        IWatchHistoryService? watchHistoryService = null)
    {
        _videoPlayerService = videoPlayerService;
        _epgService = epgService;
        _metadataService = metadataService;
        _dispatcherService = dispatcherService;
        _watchHistoryService = watchHistoryService;

        // Auto-hide timer
        _autoHideTimer = new System.Timers.Timer(4000);
        _autoHideTimer.Elapsed += (s, e) => _dispatcherService.Invoke(() => IsVisible = IsLocked);
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
                    IsBuffering = false;
                    UpdateMediaInfo();
                    _ = RefreshTracksWithRetryAsync();
                    RestartAutoHideTimer(); // Ensure controls stay visible for a few seconds after playback starts
                }
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
                // Some streams never report 100 while playback is already running.
                // Keep loading UI only before playback starts.
                IsBuffering = progress < 100 && !IsPlaying;

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
                IsBuffering = false;
                BufferingProgress = 0;
            });
        };

        _videoPlayerService.PositionChanged += (s, pos) =>
        {
            _dispatcherService.Invoke(() =>
            {
                _lastLivePositionEventAtUtc = DateTime.UtcNow;
                Position = pos;
                PositionText = TimeSpan.FromSeconds(pos).ToString(@"hh\:mm\:ss");
                TryApplyPendingResumeSeek();
                
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
        if (value != null)
        {
            // Canlı TV kontrolü
            IsLiveContent = value.Type == ChannelType.Live;
            _livePauseRequiresHardRestart = false;
            _lastLiveObservedPosition = -1;
            _lastLiveProgressAtUtc = DateTime.UtcNow;
            _lastLivePositionEventAtUtc = DateTime.UtcNow;
            _liveRecoveryWindowStartUtc = DateTime.MinValue;
            _liveRecoveryAttemptsInWindow = 0;
            _liveStallScore = 0;
            // Kanal geçişinde eski timeline değerleri görünmesin.
            Position = 0;
            PositionText = "00:00:00";
            Duration = 0;
            DurationText = "00:00:00";
            RemainingTime = IsLiveContent ? "00:00:00" : "-00:00:00";
        }

        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(HasCurrentProgramInfo));
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
    }

    public async Task PlayChannelAsync(Channel channel)
    {
        var requestVersion = Interlocked.Increment(ref _playRequestVersion);

        CurrentChannel = channel;
        CurrentProgram = GetFallbackProgram();
        IsLiveContent = channel.Type == ChannelType.Live;
        IsSeriesContent = channel.Type == ChannelType.Series;
        UpdateOverlaySecondaryText();
        IsBuffering = true;
        BufferingProgress = 0;
        StreamQuality = null;
        StreamInfo = "Kalite tespit ediliyor...";
        try
        {
            await _videoPlayerService.PlayAsync(channel.StreamUrl);
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

        // Zapping göster
        ShowZapping(channel.Name, channel.LogoUrl, IsLiveContent);

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
            var isCompleted = Duration > 0 && Position >= Duration - 30;

            await _watchHistoryService.TrackWatchAsync(
                CurrentProfileId.Value,
                channelId,
                isEpisodePlayback ? _currentEpisode!.Id : null,
                currentPosition,
                isCompleted, // Completed if within 30 seconds of end
                currentDuration
            );

            if (isEpisodePlayback && _currentEpisode != null)
            {
                _dispatcherService.BeginInvoke(() =>
                {
                    _currentEpisode.LastWatched = DateTime.Now;
                    _currentEpisode.WatchedPosition = isCompleted && currentDuration.HasValue
                        ? currentDuration.Value
                        : currentPosition;
                    _currentEpisode.IsCompleted = isCompleted;
                    if (currentDuration.HasValue)
                    {
                        _currentEpisode.Duration = currentDuration.Value;
                    }
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

    public void ShowZapping(string name, string? logo, bool isLive)
    {
        // Disabled by UX request:
        // "Bağlanıyor / Kalite tespit ediliyor" mini zapping penceresini göstermiyoruz.
        IsZappingVisible = false;
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
        if (!IsLocked) _autoHideTimer.Start();
        IsVisible = true;
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
            IsLocked = true;
            UpdateMediaInfo();
            _ = RefreshTracksWithRetryAsync();
        }
    }

    [RelayCommand]
    private void OpenQualitySettings()
    {
        IsQualitySettingsOpen = !IsQualitySettingsOpen;
        if (IsQualitySettingsOpen) IsLocked = true;
    }

    [RelayCommand]
    private void OpenInfoPanel()
    {
        IsInfoPanelOpen = !IsInfoPanelOpen;
        if (IsInfoPanelOpen) IsLocked = true;
    }

    [RelayCommand]
    private void ClosePanels()
    {
        IsAudioSettingsOpen = false;
        IsQualitySettingsOpen = false;
        IsInfoPanelOpen = false;
        IsLocked = false;
        RestartAutoHideTimer();
    }

    private void UpdateMediaInfo()
    {
        Duration = _videoPlayerService.Duration;
        DurationText = TimeSpan.FromSeconds(Duration).ToString(@"hh\:mm\:ss");
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
        name = Regex.Replace(
            name,
            @"^\s*track\s*\d+\s*([:\-\)\.]|\s)\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        if (Regex.IsMatch(name, @"^\s*track\s*\d+\s*$", RegexOptions.IgnoreCase))
        {
            return fallback;
        }

        // Remove bracket characters while keeping inner text: [English] -> English
        name = name.Replace("[", string.Empty).Replace("]", string.Empty);
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
    }

    partial void OnVolumeChanged(int value)
    {
        _videoPlayerService.Volume = value;
    }

    partial void OnIsMutedChanged(bool value)
    {
        _videoPlayerService.IsMuted = value;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void Seek(double position)
    {
        if (IsLiveContent) return;
        _videoPlayerService.Position = position;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SkipForward(object? parameter)
    {
        if (IsLiveContent) return;

        double seconds = 10;
        if (parameter != null)
        {
            if (parameter is int i) seconds = i;
            else if (parameter is double d) seconds = d;
            else if (parameter is string s && double.TryParse(s, out double parsed)) seconds = parsed;
        }

        var newPos = Math.Min(Position + seconds, Duration);
        _videoPlayerService.Position = newPos;
        RestartAutoHideTimer();
    }

    /// <summary>
    /// Mevcut bölümü ayarlar (dizi oynatma başlatıldığında çağrılır)
    /// </summary>
    public void SetCurrentEpisode(Episode? episode, Episode? nextEpisode = null)
    {
        _currentEpisode = episode;
        NextEpisode = nextEpisode;
        _creditsTriggered = false;
        IsCreditsZone = false;
        IsNextEpisodePromptVisible = false;
    }

    /// <summary>
    /// Position bazlı intro/credits tespiti
    /// </summary>
    private void CheckIntroCreditsPosition(double pos)
    {
        if (_currentEpisode == null || IsLiveContent) return;

        // ── CREDITS DETECTION ──
        if (!_creditsTriggered && _currentEpisode.CreditsStartSec != null)
        {
            if (pos >= _currentEpisode.CreditsStartSec.Value)
            {
                _creditsTriggered = true;
                IsCreditsZone = true;

                // Show next episode prompt if available
                if (NextEpisode != null)
                {
                    IsNextEpisodePromptVisible = true;

                    // Auto-skip to next episode if setting enabled
                    if (GetAutoSkipCreditsSetting())
                    {
                        _ = PlayNextEpisodeCommand.ExecuteAsync(null);
                    }
                }
            }
        }
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

    [RelayCommand(CanExecute = nameof(CanPlayNextEpisode))]
    private async Task PlayNextEpisode()
    {
        if (NextEpisode != null)
        {
            IsNextEpisodePromptVisible = false;
            ChannelName = NextEpisode.Name; 
            // In real app, this would trigger MainViewModel to play the next episode
        }
        await Task.CompletedTask;
    }

    private bool CanPlayNextEpisode()
    {
        return CurrentChannel?.Type == ChannelType.Series && !IsLiveContent;
    }

    [RelayCommand]
    private void SkipBackward(object? parameter)
    {
        if (IsLiveContent) return;

        double seconds = 10;
        if (parameter != null)
        {
            if (parameter is int i) seconds = i;
            else if (parameter is double d) seconds = d;
            else if (parameter is string s && double.TryParse(s, out double parsed)) seconds = parsed;
        }

        var newPos = Math.Max(Position - seconds, 0);
        _videoPlayerService.Position = newPos;
        RestartAutoHideTimer();
    }

    public event EventHandler? OpenEpisodesRequested;

    [RelayCommand]
    private void OpenEpisodes()
    {
        OpenEpisodesRequested?.Invoke(this, EventArgs.Empty);
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

    [RelayCommand]
    private void UserInteraction() => RestartAutoHideTimer();
}



