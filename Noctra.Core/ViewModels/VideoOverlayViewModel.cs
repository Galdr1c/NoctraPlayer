using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;
using System.Timers;

namespace Noctra.ViewModels;

public partial class VideoOverlayViewModel : ObservableObject, IDisposable
{
    private readonly IVideoPlayerService _playerService;
    private readonly INetworkService _networkService;
    private readonly IDispatcherService _dispatcherService;
    private readonly System.Timers.Timer _autoHideTimer;
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _currentTime = "00:00";

    [ObservableProperty]
    private string _totalTime = "00:00";

    [ObservableProperty]
    private string _remainingTime = "-00:00";

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private double _volume = 100;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isBuffering;

    [ObservableProperty]
    private string _channelLogo = string.Empty;

    [ObservableProperty]
    private string _channelName = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "Bağlanıyor...";

    [ObservableProperty]
    private string _networkStatus = "Offline";

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private double _bufferingProgress;

    [ObservableProperty]
    private string _streamInfo = string.Empty; // e.g., "1080p | 4500 kbps"

    [ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<(int Id, string? Name)> _audioTracks = new System.Collections.Generic.List<(int, string?)>();

    [ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<(int Id, string? Name)> _subtitleTracks = new System.Collections.Generic.List<(int, string?)>();

    [ObservableProperty]
    private bool _isVolumeToastVisible;

    private bool _isUpdatingFromService;
    private readonly System.Timers.Timer _volumeToastTimer;

    public VideoOverlayViewModel(IVideoPlayerService playerService, INetworkService networkService, IDispatcherService dispatcherService)
    {
        _playerService = playerService;
        _networkService = networkService;
        _dispatcherService = dispatcherService;
        
        // Timer for auto-hide
        _autoHideTimer = new System.Timers.Timer(3000); // 3 seconds
        _autoHideTimer.Elapsed += AutoHideTimer_Elapsed;
        _autoHideTimer.AutoReset = false;

        // Timer for volume toast
        _volumeToastTimer = new System.Timers.Timer(2000); // 2 seconds
        _volumeToastTimer.Elapsed += (s, e) => _dispatcherService.Invoke(() => IsVolumeToastVisible = false);
        _volumeToastTimer.AutoReset = false;

        IsVisible = true;
        RestartAutoHideTimer();
        InitializeClock();

        // Initialize network status
        NetworkStatus = _networkService.CurrentNetworkStatus;
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;
        
        // Subscribe to player events
        _playerService.PlayingChanged += PlayerService_PlayingChanged;
        _playerService.PositionChanged += PlayerService_PositionChanged;
    }

    private void OnNetworkStatusChanged(object? sender, string status)
    {
        NetworkStatus = status;
    }

    private void PlayerService_PlayingChanged(object? sender, bool isPlaying)
    {
        IsPlaying = isPlaying;
        if (isPlaying)
        {
            Duration = _playerService.Duration;
            TotalTime = TimeSpan.FromSeconds(Duration).ToString(@"hh\:mm\:ss");
        }
    }

    private void PlayerService_PositionChanged(object? sender, double position)
    {
        _isUpdatingFromService = true;
        Position = position;
        CurrentTime = TimeSpan.FromSeconds(position).ToString(@"hh\:mm\:ss");
        if (Duration > 0)
        {
            var remaining = Duration - position;
            RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
        }
        _isUpdatingFromService = false;
    }

    partial void OnPositionChanged(double value)
    {
        if (!_isUpdatingFromService)
        {
            _playerService.Position = value;
            RestartAutoHideTimer();
        }
    }

    partial void OnVolumeChanged(double value)
    {
        _playerService.Volume = (int)value;
        IsVolumeToastVisible = true;
        _volumeToastTimer?.Stop();
        _volumeToastTimer?.Start();
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        _playerService.Pause();
        IsPlaying = !IsPlaying; // Optimistic update
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void ShowOverlay()
    {
        IsVisible = true;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void HideOverlay()
    {
        if (!IsLocked)
        {
            IsVisible = false;
        }
    }

    [RelayCommand]
    private void LockOverlay()
    {
        IsLocked = true;
        IsVisible = true;
        _autoHideTimer.Stop();
    }

    [RelayCommand]
    private void UnlockOverlay()
    {
        IsLocked = false;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void UserInteraction()
    {
        ShowOverlay();
    }

    [ObservableProperty]
    private string _currentTimeStr = "00:00"; // Current clock time

    [ObservableProperty]
    private bool _isAudioSettingsOpen;

    [RelayCommand]
    private void Back()
    {
        // Close overlay or navigate back
        HideOverlay();
        // Trigger back navigation in main view model if needed
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _playerService.IsMuted = !_playerService.IsMuted;
        Volume = _playerService.Volume; // Update VM
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void Rewind()
    {
        _playerService.Position = Math.Max(0, Position - 10000); // -10s
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void Forward()
    {
        _playerService.Position = Math.Min(Duration, Position + 10000); // +10s
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SeekNextEpisode()
    {
        // Placeholder for next episode logic
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void OpenAudioSettings()
    {
        IsAudioSettingsOpen = !IsAudioSettingsOpen;
        if (IsAudioSettingsOpen) 
        {
            UpdateTracks();
            LockOverlay();
        }
        else UnlockOverlay();
    }

    [ObservableProperty]
    private bool _isQualitySettingsOpen;

    [RelayCommand]
    private void OpenQualitySettings()
    {
        IsQualitySettingsOpen = !IsQualitySettingsOpen;
        if (IsQualitySettingsOpen) LockOverlay();
        else UnlockOverlay();
    }

    [RelayCommand]
    public void ClosePanels()
    {
        IsAudioSettingsOpen = false;
        IsQualitySettingsOpen = false;
        UnlockOverlay();
    }

    [ObservableProperty]
    private float _playbackRate = 1.0f;

    [RelayCommand]
    private void SetPlaybackSpeed(float speed)
    {
        PlaybackRate = speed;
        _playerService.PlaybackRate = speed;
        RestartAutoHideTimer();
    }

    [ObservableProperty]
    private bool _isFullScreen;

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullScreen = !IsFullScreen;
        RestartAutoHideTimer();
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer.Stop();
        if (!IsLocked)
        {
            _autoHideTimer.Start();
        }
    }

    private void AutoHideTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (!IsLocked)
        {
            _dispatcherService.Invoke(() => IsVisible = false);
        }
    }

    public void UpdateTracks()
    {
        AudioTracks = _playerService.AudioTracks;
        SubtitleTracks = _playerService.SubtitleTracks;
    }

    [RelayCommand]
    private void SetAudioTrack(int id)
    {
        _playerService.SetAudioTrack(id);
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetSubtitleTrack(int id)
    {
        _playerService.SetSubtitleTrack(id);
        RestartAutoHideTimer();
    }

    // Timer to update clock
    private System.Timers.Timer? _clockTimer;

    public void InitializeClock()
    {
        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += (s, e) => 
        {
            var now = DateTime.Now.ToString("HH:mm");
            _dispatcherService.Invoke(() => CurrentTimeStr = now);
        };
        _clockTimer.Start();
        CurrentTimeStr = DateTime.Now.ToString("HH:mm");
    }

    public void Dispose()
    {
        _autoHideTimer?.Dispose();
        _clockTimer?.Dispose();
        _volumeToastTimer?.Dispose();
        if (_networkService != null)
        {
            _networkService.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
    }
}
