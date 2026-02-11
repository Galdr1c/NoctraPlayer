using LibVLCSharp.Shared;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

/// <summary>
/// LibVLCSharp tabanlı video player servisi
/// </summary>
public class VideoPlayerService : IVideoPlayerService
{
    private const int NetworkCachingMs = 500;
    private const int LiveCachingMs = 500;

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
            // Hardware Acceleration
            "--avcodec-hw=dxva2",
            "--ffmpeg-hw",
            "--vout=direct3d11",           // DirectX 11 renderer
            "--avcodec-skip-frame=0",
            "--avcodec-skip-idct=0",
            "--avcodec-fast",
            
            // Network Options
            $"--network-caching={NetworkCachingMs}",
            $"--live-caching={LiveCachingMs}",         // Canlı TV için daha az buffer
            "--file-caching=1000",
            
            // RTSP Options
            "--rtsp-tcp",                  // TCP kullan (UDP yerine, daha stabil)
            "--rtsp-frame-buffer-size=500000",
            
            // Sync Options
            "--clock-jitter=0",
            "--clock-synchro=0",
            "--no-audio-time-stretch",
            
            // Performance
            "--drop-late-frames",          // Geciken frame'leri at
            "--skip-frames",               // FPS drop'ta frame atla
            "--avcodec-threads=4",         // Multi-threading
            
            // Logging
            "--verbose=0",
            "--quiet"
        };
        
        _libVLC = new LibVLC(options);
        _mediaPlayer = new MediaPlayer(_libVLC);
        
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.Playing += (s, e) => _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, true));
        _mediaPlayer.Paused += (s, e) => _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        _mediaPlayer.Stopped += (s, e) => _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        _mediaPlayer.EndReached += (s, e) => _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        
        _mediaPlayer.PositionChanged += (s, e) => 
            _dispatcherService.BeginInvoke(() => PositionChanged?.Invoke(this, e.Position * Duration));
        
        _mediaPlayer.EncounteredError += (s, e) => 
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Video oynatma hatası oluştu"));
            
        _mediaPlayer.Buffering += (s, e) =>
            _dispatcherService.BeginInvoke(() => BufferingChanged?.Invoke(this, e.Cache));
    }

    public event EventHandler<float>? BufferingChanged;

    public MediaPlayer? GetMediaPlayer() => _mediaPlayer;

    public async Task PlayAsync(string url)
    {
        System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] PlayAsync called with URL: {url}");
        
        if (_mediaPlayer == null)
        {
            System.Diagnostics.Debug.WriteLine("[VideoPlayerService] ERROR: _mediaPlayer is null!");
            return;
        }
        
        _retryCount = 0;
        await PlayWithRetryAsync(url);
    }

    private async Task PlayWithRetryAsync(string url)
    {
        if (_mediaPlayer == null)
        {
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Media player hazır değil."));
            return;
        }

        try
        {
            var media = new Media(_libVLC, new Uri(url));
            
            // Stream ayarları
            media.AddOption($":network-caching={NetworkCachingMs}");
            media.AddOption($":live-caching={LiveCachingMs}");
            
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
                _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Stream bağlantısı kurulamadı. URL'yi kontrol edin."));
            }
        }
        catch (UriFormatException)
        {
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Geçersiz stream URL'si."));
        }
        catch (Exception ex)
        {
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, $"Oynatma başlatılamadı: {ex.Message}"));
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

    public float PlaybackRate
    {
        get => _mediaPlayer?.Rate ?? 1.0f;
        set
        {
            if (_mediaPlayer != null)
                _mediaPlayer.SetRate(value);
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

