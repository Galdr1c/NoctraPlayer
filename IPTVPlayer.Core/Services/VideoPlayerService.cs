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
    private readonly IDispatcherService _dispatcherService;
    private bool _disposed;
    
    private int _retryCount = 0;
    private const int MaxRetries = 3;

    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<string>? ErrorOccurred;

    public VideoPlayerService(IDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
        LibVLCSharp.Shared.Core.Initialize();
        
        // IPTV optimized options
        var options = new string[]
        {
            "--avcodec-hw=any",
            "--network-caching=3000",
            "--file-caching=3000",
            "--live-caching=1000",        // Low latency
            "--clock-jitter=0",
            "--clock-synchro=0",
            "--rtsp-tcp",                 // Stable connection
            "--no-audio-time-stretch",    // Sync
            "--drop-late-frames",         // Frame drop
            "--skip-frames"               // Performance
        };
        
        _libVLC = new LibVLC(options);
        _mediaPlayer = new MediaPlayer(_libVLC);
        
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.Playing += (s, e) => _dispatcherService.Invoke(() => PlayingChanged?.Invoke(this, true));
        _mediaPlayer.Paused += (s, e) => _dispatcherService.Invoke(() => PlayingChanged?.Invoke(this, false));
        _mediaPlayer.Stopped += (s, e) => _dispatcherService.Invoke(() => PlayingChanged?.Invoke(this, false));
        _mediaPlayer.EndReached += (s, e) => _dispatcherService.Invoke(() => PlayingChanged?.Invoke(this, false));
        
        _mediaPlayer.PositionChanged += (s, e) => 
            _dispatcherService.Invoke(() => PositionChanged?.Invoke(this, e.Position * Duration));
        
        _mediaPlayer.EncounteredError += (s, e) => 
            _dispatcherService.Invoke(() => ErrorOccurred?.Invoke(this, "Video oynatma hatası oluştu"));
    }

    public MediaPlayer? GetMediaPlayer() => _mediaPlayer;

    public async Task PlayAsync(string url)
    {
        if (_mediaPlayer == null) return;
        
        _retryCount = 0;
        await PlayWithRetryAsync(url);
    }

    private async Task PlayWithRetryAsync(string url)
    {
        try
        {
            var media = new Media(_libVLC, new Uri(url));
            
            // Stream ayarları
            media.AddOption(":network-caching=3000");
            media.AddOption(":live-caching=1000");
            
            _mediaPlayer.Media = media;
            
            // Hata event'ini dinle
            bool errorOccurred = false;
            void OnError(object? s, EventArgs e)
            {
                errorOccurred = true;
            }
            
            _mediaPlayer.EncounteredError += OnError;
            _mediaPlayer.Play();
            
            // 5 saniye bekle - başarılı başladı mı?
            await Task.Delay(5000);
            
            _mediaPlayer.EncounteredError -= OnError;
            
            if (errorOccurred && _retryCount < MaxRetries)
            {
                _retryCount++;
                await Task.Delay(2000); // 2 saniye bekle
                await PlayWithRetryAsync(url);
            }
            else if (errorOccurred)
            {
                _dispatcherService.Invoke(() => ErrorOccurred?.Invoke(this, "Stream bağlantısı kurulamadı. URL'yi kontrol edin."));
            }
        }
        catch (UriFormatException)
        {
            _dispatcherService.Invoke(() => ErrorOccurred?.Invoke(this, "Geçersiz stream URL'si."));
        }
        catch (Exception ex)
        {
            _dispatcherService.Invoke(() => ErrorOccurred?.Invoke(this, $"Oynatma başlatılamadı: {ex.Message}"));
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
