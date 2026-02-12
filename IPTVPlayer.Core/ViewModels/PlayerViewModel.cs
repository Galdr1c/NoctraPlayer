using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

/// <summary>
/// Video player view model
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
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
    private List<(int Id, string? Name)> _audioTracks = new();

    [ObservableProperty]
    private List<(int Id, string? Name)> _subtitleTracks = new();

    [ObservableProperty]
    private int _selectedAudioTrack = -1;

    [ObservableProperty]
    private int _selectedSubtitleTrack = -1;

    [ObservableProperty]
    private bool _isQualitySettingsOpen;

    [ObservableProperty]
    private Models.StreamQualityInfo? _streamQuality;

    [ObservableProperty]
    private bool _isInfoPanelOpen;

    [ObservableProperty]
    private bool _isIntroDetected;

    [ObservableProperty]
    private Episode? _nextEpisode;

    [ObservableProperty]
    private bool _isNextEpisodePromptVisible;

    [ObservableProperty]
    private bool _isCreditsZone;

    private Episode? _currentEpisode;
    private bool _introSkipped;
    private bool _creditsTriggered;

    private readonly IDispatcherService _dispatcherService;
    private readonly IWatchHistoryService? _watchHistoryService;
    private readonly System.Timers.Timer _autoHideTimer;
    private readonly System.Timers.Timer _clockTimer;
    private readonly System.Timers.Timer _watchHistoryTimer;
    private System.Timers.Timer? _zappingTimer;

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
            _dispatcherService.Invoke(() => CurrentTimeStr = DateTime.Now.ToString("HH:mm"));
            await CheckForEpgUpdateAsync();
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
                    RestartAutoHideTimer(); // Ensure controls stay visible for a few seconds after playback starts
                }
            });
        };

        _videoPlayerService.QualityDetected += (s, quality) =>
        {
            _dispatcherService.Invoke(() => StreamQuality = quality);
        };

        _videoPlayerService.BufferingChanged += (s, progress) =>
        {
            _dispatcherService.Invoke(() =>
            {
                BufferingProgress = progress;
                IsBuffering = progress < 100;
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
                Position = pos;
                PositionText = TimeSpan.FromSeconds(pos).ToString(@"hh\:mm\:ss");
                
                if (Duration > 0)
                {
                    var remaining = Math.Max(0, Duration - pos);
                    RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");

                    // Intro/Credits detection from episode timestamps
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

            // VOD/Series için zamanlayıcıyı başlat
            if (!IsLiveContent)
            {
                // VOD işlemleri
            }
            else
            {
                // Live için position sıfırla
                Position = 0;
                PositionText = "00:00:00";
                DurationText = "00:00:00";
                RemainingTime = "00:00:00";
            }
        }

        UpdateOverlaySecondaryText();
    }

    partial void OnCurrentProgramChanged(EpgProgram? value)
    {
        UpdateOverlaySecondaryText();
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

        try
        {
            await _watchHistoryService.TrackWatchAsync(
                CurrentProfileId.Value,
                CurrentChannel.Id,
                null, // EpisodeId - null for channels
                TimeSpan.FromSeconds(Position),
                Position >= Duration - 30 // Completed if within 30 seconds of end
            );
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
        ChannelName = name;
        ChannelLogo = logo ?? string.Empty;
        IsLive = isLive;
        ConnectionStatus = "Bağlanıyor...";
        BufferingProgress = 0;
        StreamInfo = isLive ? "1080p | 60fps" : "4K | HDR | 24fps";
        IsZappingVisible = true;

        _zappingTimer?.Stop();
        _zappingTimer ??= new System.Timers.Timer(4000);
        _zappingTimer.AutoReset = false;
        _zappingTimer.AutoReset = false;
        _zappingTimer.Elapsed += (s, e) => _dispatcherService.Invoke(() => IsZappingVisible = false);
        
        // Simüle progress
        var progressTimer = new System.Timers.Timer(100);
        progressTimer.Elapsed += (s, e) => _dispatcherService.Invoke(() => {
            if (BufferingProgress < 100) BufferingProgress += 5;
            else progressTimer.Stop();
        });
        progressTimer.Start();
        _zappingTimer.Start();
        
        RestartAutoHideTimer();
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
        if (IsAudioSettingsOpen) IsLocked = true;
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
        
        AudioTracks = _videoPlayerService.AudioTracks.ToList();
        SubtitleTracks = _videoPlayerService.SubtitleTracks.ToList();
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (IsPlaying)
            _videoPlayerService.Pause();
        else if (CurrentChannel != null)
            _ = _videoPlayerService.PlayAsync(CurrentChannel.StreamUrl);
        
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void Stop()
    {
        _videoPlayerService.Stop();
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

    [RelayCommand]
    private void SkipIntro()
    {
        if (IsLiveContent) return;
        
        if (_currentEpisode?.IntroEndSec != null)
        {
            // Jump to intro end timestamp
            var newPos = Math.Min(_currentEpisode.IntroEndSec.Value, Duration);
            _videoPlayerService.Position = newPos;
        }
        else
        {
            // Fallback: skip 85 seconds
            var newPos = Math.Min(Position + 85, Duration);
            _videoPlayerService.Position = newPos;
        }
        
        IsIntroDetected = false;
        _introSkipped = true;
    }

    /// <summary>
    /// Mevcut bölümü ayarlar (dizi oynatma başlatıldığında çağrılır)
    /// </summary>
    public void SetCurrentEpisode(Episode? episode, Episode? nextEpisode = null)
    {
        _currentEpisode = episode;
        NextEpisode = nextEpisode;
        _introSkipped = false;
        _creditsTriggered = false;
        IsIntroDetected = false;
        IsCreditsZone = false;
        IsNextEpisodePromptVisible = false;
    }

    /// <summary>
    /// Position bazlı intro/credits tespiti
    /// </summary>
    private void CheckIntroCreditsPosition(double pos)
    {
        if (_currentEpisode == null || IsLiveContent) return;

        // ── INTRO DETECTION ──
        if (!_introSkipped && _currentEpisode.IntroStartSec != null && _currentEpisode.IntroEndSec != null)
        {
            var inIntro = pos >= _currentEpisode.IntroStartSec.Value && pos < _currentEpisode.IntroEndSec.Value;
            
            if (inIntro && !IsIntroDetected)
            {
                IsIntroDetected = true;
                
                // Auto-skip if setting enabled
                if (GetAutoSkipIntroSetting())
                {
                    SkipIntro();
                    return;
                }
            }
            else if (!inIntro && IsIntroDetected)
            {
                IsIntroDetected = false;
            }
        }

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

    private bool GetAutoSkipIntroSetting()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IPTVPlayer", "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                return settings?.AutoSkipIntro ?? false;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private bool GetAutoSkipCreditsSetting()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IPTVPlayer", "settings.json");
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
        if (value >= 0)
            _videoPlayerService.SetAudioTrack(value);
    }

    partial void OnSelectedSubtitleTrackChanged(int value)
    {
        if (value >= 0)
            _videoPlayerService.SetSubtitleTrack(value);
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
        SelectedAudioTrack = id;
        // _videoPlayerService.SetAudioTrack(id); // Handled by OnSelectedAudioTrackChanged
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetSubtitleTrack(int id)
    {
        SelectedSubtitleTrack = id;
        // _videoPlayerService.SetSubtitleTrack(id); // Handled by OnSelectedSubtitleTrackChanged
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetPlaybackSpeed(float speed)
    {
        _videoPlayerService.PlaybackRate = speed;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void ClosePlayer()
    {
        Stop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void UserInteraction() => RestartAutoHideTimer();
}
