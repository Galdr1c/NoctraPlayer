using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

/// <summary>
/// Video player view model
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IEpgService _epgService;
    private Timer? _positionTimer;

    [ObservableProperty]
    private bool _isVisible = true;

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

    private readonly IDispatcherService _dispatcherService;
    private readonly System.Timers.Timer _autoHideTimer;
    private readonly System.Timers.Timer _clockTimer;
    private System.Timers.Timer? _zappingTimer;

    public PlayerViewModel(IVideoPlayerService videoPlayerService, IEpgService epgService, IDispatcherService dispatcherService)
    {
        _videoPlayerService = videoPlayerService;
        _epgService = epgService;
        _dispatcherService = dispatcherService;

        // Auto-hide timer
        _autoHideTimer = new System.Timers.Timer(4000);
        _autoHideTimer.Elapsed += (s, e) => IsVisible = IsLocked;
        _autoHideTimer.AutoReset = false;

        // Clock timer
        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += (s, e) => CurrentTimeStr = DateTime.Now.ToString("HH:mm");
        _clockTimer.Start();
        CurrentTimeStr = DateTime.Now.ToString("HH:mm");

        _videoPlayerService.PlayingChanged += (s, playing) => 
        {
            _dispatcherService.Invoke(() =>
            {
                IsPlaying = playing;
                if (playing) UpdateMediaInfo();
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
                }
            });
        };
    }

    public async Task PlayChannelAsync(Channel channel)
    {
        CurrentChannel = channel;
        await _videoPlayerService.PlayAsync(channel.StreamUrl);

        // Zapping göster
        ShowZapping(channel.Name, channel.LogoUrl, channel.Type == ChannelType.Live);

        // EPG bilgisini al
        if (!string.IsNullOrEmpty(channel.TvgId) && _epgService.IsLoaded)
        {
            CurrentProgram = await _epgService.GetCurrentProgramAsync(channel.TvgId);
        }
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
        _zappingTimer.Elapsed += (s, e) => IsZappingVisible = false;
        
        // Simüle progress
        var progressTimer = new System.Timers.Timer(100);
        progressTimer.Elapsed += (s, e) => {
            if (BufferingProgress < 100) BufferingProgress += 5;
            else progressTimer.Stop();
        };
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
    private void ClosePanels()
    {
        IsAudioSettingsOpen = false;
        IsQualitySettingsOpen = false;
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
        _videoPlayerService.Position = position;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SkipForward(double seconds = 10)
    {
        var newPos = Math.Min(Position + seconds, Duration);
        _videoPlayerService.Position = newPos;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SkipBackward(double seconds = 10)
    {
        var newPos = Math.Max(Position - seconds, 0);
        _videoPlayerService.Position = newPos;
        RestartAutoHideTimer();
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
    private void ClosePlayer()
    {
        Stop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void UserInteraction() => RestartAutoHideTimer();
}
