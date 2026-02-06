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

    private readonly IDispatcherService _dispatcherService;

    public PlayerViewModel(IVideoPlayerService videoPlayerService, IEpgService epgService, IDispatcherService dispatcherService)
    {
        _videoPlayerService = videoPlayerService;
        _epgService = epgService;
        _dispatcherService = dispatcherService;

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
            });
        };
    }

    public async Task PlayChannelAsync(Channel channel)
    {
        CurrentChannel = channel;
        await _videoPlayerService.PlayAsync(channel.StreamUrl);

        // EPG bilgisini al
        if (!string.IsNullOrEmpty(channel.TvgId) && _epgService.IsLoaded)
        {
            CurrentProgram = await _epgService.GetCurrentProgramAsync(channel.TvgId);
        }
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
    }

    [RelayCommand]
    private void Stop()
    {
        _videoPlayerService.Stop();
        CurrentChannel = null;
        CurrentProgram = null;
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
    }

    [RelayCommand]
    private void Seek(double position)
    {
        _videoPlayerService.Position = position;
    }

    [RelayCommand]
    private void SkipForward(double seconds = 10)
    {
        var newPos = Math.Min(Position + seconds, Duration);
        _videoPlayerService.Position = newPos;
    }

    [RelayCommand]
    private void SkipBackward(double seconds = 10)
    {
        var newPos = Math.Max(Position - seconds, 0);
        _videoPlayerService.Position = newPos;
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
    }
}
