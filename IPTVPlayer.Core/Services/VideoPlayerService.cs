using LibVLCSharp.Shared;
using IPTVPlayer.Models;
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
    public event EventHandler<StreamQualityInfo>? QualityDetected;

    public StreamQualityInfo? StreamQuality { get; private set; }

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

        _mediaPlayer.Playing += (s, e) =>
        {
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, true));
            // Detect quality 1 second after playback starts (tracks need time to populate)
            _ = DetectStreamQualityAsync();
        };
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

    private async Task DetectStreamQualityAsync()
    {
        try
        {
            // Wait for media tracks to populate
            await Task.Delay(1500);

            if (_mediaPlayer?.Media == null) return;

            // Parse the media to get track info
            await _mediaPlayer.Media.Parse(MediaParseOptions.ParseNetwork, timeout: 5000);

            var quality = new StreamQualityInfo();

            foreach (var track in _mediaPlayer.Media.Tracks)
            {
                if (track.TrackType == TrackType.Video)
                {
                    var videoTrack = track.Data.Video;
                    quality.Width = (int)videoTrack.Width;
                    quality.Height = (int)videoTrack.Height;
                    quality.Fps = videoTrack.FrameRateNum > 0 && videoTrack.FrameRateDen > 0
                        ? (int)(videoTrack.FrameRateNum / videoTrack.FrameRateDen)
                        : 0;
                    quality.VideoCodec = track.Codec > 0 
                        ? FourCCToString(track.Codec) 
                        : track.Description ?? "";
                    quality.VideoBitrate = (int)track.Bitrate;
                }
                else if (track.TrackType == TrackType.Audio)
                {
                    var audioTrack = track.Data.Audio;
                    quality.AudioChannels = (int)audioTrack.Channels;
                    quality.AudioCodec = track.Codec > 0 
                        ? FourCCToString(track.Codec)
                        : track.Description ?? "";
                    quality.AudioBitrate = (int)(track.Bitrate / 1000); // bps → kbps
                }
            }

            StreamQuality = quality;

            System.Diagnostics.Debug.WriteLine(
                $"[VideoPlayerService] Quality detected: {quality.Width}x{quality.Height} " +
                $"@{quality.Fps}fps, {quality.VideoCodec}, {quality.VideoBitrate}bps | " +
                $"Audio: {quality.AudioCodec} {quality.AudioChannels}ch {quality.AudioBitrate}kbps");

            _dispatcherService.BeginInvoke(() => QualityDetected?.Invoke(this, quality));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] Quality detection error: {ex.Message}");
        }
    }

    /// <summary>
    /// FourCC codec code → human-readable string
    /// </summary>
    private static string FourCCToString(uint fourcc)
    {
        if (fourcc == 0) return "";
        var bytes = BitConverter.GetBytes(fourcc);
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = bytes[i] >= 32 && bytes[i] < 127 ? (char)bytes[i] : '?';
        }
        return new string(chars).TrimEnd('?', '\0').Trim();
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

