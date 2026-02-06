using LibVLCSharp.Shared;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

/// <summary>
/// LibVLCSharp tabanlı video player servisi
/// </summary>
public class VideoPlayerService : IVideoPlayerService
{
    private readonly LibVLC _libVLC;
    private MediaPlayer? _mediaPlayer;
    private bool _disposed;

    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<string>? ErrorOccurred;

    public VideoPlayerService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        
        // Donanım hızlandırma ve buffer ayarları
        var options = new string[]
        {
            "--avcodec-hw=any",       // Donanım hızlandırma (GPU)
            "--network-caching=3000", // 3 saniye ağ önbelleği
            "--file-caching=3000",    // 3 saniye dosya önbelleği
            "--clock-jitter=0",       // Jitter kontrolü
            "--clock-synchro=0"       // Senkronizasyon
        };
        
        _libVLC = new LibVLC(options);
        _mediaPlayer = new MediaPlayer(_libVLC);
        
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.Playing += (s, e) => PlayingChanged?.Invoke(this, true);
        _mediaPlayer.Paused += (s, e) => PlayingChanged?.Invoke(this, false);
        _mediaPlayer.Stopped += (s, e) => PlayingChanged?.Invoke(this, false);
        _mediaPlayer.EndReached += (s, e) => PlayingChanged?.Invoke(this, false);
        
        _mediaPlayer.PositionChanged += (s, e) => 
            PositionChanged?.Invoke(this, e.Position * Duration);
        
        _mediaPlayer.EncounteredError += (s, e) => 
            ErrorOccurred?.Invoke(this, "Video oynatma hatası oluştu");
    }

    public MediaPlayer? GetMediaPlayer() => _mediaPlayer;

    public async Task PlayAsync(string url)
    {
        if (_mediaPlayer == null) return;

        try
        {
            var media = new Media(_libVLC, new Uri(url));
            _mediaPlayer.Media = media;
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Oynatma başlatılamadı: {ex.Message}");
        }
    }

    public void Pause()
    {
        _mediaPlayer?.Pause();
    }

    public void Stop()
    {
        _mediaPlayer?.Stop();
    }

    public int Volume
    {
        get => _mediaPlayer?.Volume ?? 0;
        set
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
        }
    }

    public bool IsMuted
    {
        get => _mediaPlayer?.Mute ?? false;
        set
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Mute = value;
        }
    }

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    public double Position
    {
        get => (_mediaPlayer?.Position ?? 0) * Duration;
        set
        {
            if (_mediaPlayer != null && Duration > 0)
                _mediaPlayer.Position = (float)(value / Duration);
        }
    }

    public double Duration
    {
        get
        {
            var length = _mediaPlayer?.Length ?? 0;
            return length > 0 ? length / 1000.0 : 0;
        }
    }

    public IReadOnlyList<(int Id, string? Name)> AudioTracks
    {
        get
        {
            if (_mediaPlayer == null) return Array.Empty<(int, string?)>();
            
            var tracks = new List<(int, string?)>();
            var description = _mediaPlayer.AudioTrackDescription;
            
            foreach (var track in description)
            {
                tracks.Add((track.Id, track.Name));
            }
            
            return tracks;
        }
    }

    public IReadOnlyList<(int Id, string? Name)> SubtitleTracks
    {
        get
        {
            if (_mediaPlayer == null) return Array.Empty<(int, string?)>();
            
            var tracks = new List<(int, string?)>();
            var description = _mediaPlayer.SpuDescription;
            
            foreach (var track in description)
            {
                tracks.Add((track.Id, track.Name));
            }
            
            return tracks;
        }
    }

    public void SetAudioTrack(int trackId)
    {
        if (_mediaPlayer != null)
            _mediaPlayer.SetAudioTrack(trackId);
    }

    public void SetSubtitleTrack(int trackId)
    {
        if (_mediaPlayer != null)
            _mediaPlayer.SetSpu(trackId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
        _libVLC.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
