using LibVLCSharp.Shared;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// LibVLCSharp tabanlı video player servisi
/// </summary>
public class VideoPlayerService : IVideoPlayerService
{
    private const int NetworkCachingMs = 500;
    private const int LiveCachingMs = 500;

    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private readonly IDispatcherService _dispatcherService;
    private bool _disposed;
    
    private int _retryCount = 0;
    private const int MaxRetries = 3;
    private readonly object _qualitySync = new();
    private CancellationTokenSource? _qualityMonitorCts;
    private CancellationTokenSource? _playCts;
    private long _playGeneration;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public event EventHandler<MediaPlayer?>? MediaPlayerReady;
    public event EventHandler<bool>? PlayingChanged;

    public event EventHandler<double>? PositionChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<StreamQualityInfo>? QualityDetected;

    public string? CurrentUrl { get; private set; }
    public StreamQualityInfo? StreamQuality { get; private set; }

    public VideoPlayerService(IDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
        
        // Start initialization in the background so we don't block the UI thread
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            await Task.Run(() => 
            {
                LibVLCSharp.Shared.Core.Initialize();
                
                var options = new string[]
                {
                    "--avcodec-hw=dxva2",
                    "--vout=direct3d11",
                    $"--network-caching={NetworkCachingMs}",
                    $"--live-caching={LiveCachingMs}",
                    "--file-caching=1000",
                    "--rtsp-tcp",
                    "--drop-late-frames",
                    "--skip-frames",
                    "--verbose=0",
                    "--quiet"
                };
                
                _libVLC = new LibVLC(options);
                _mediaPlayer = new MediaPlayer(_libVLC);
            });

            SetupEventHandlers();
            _isInitialized = true;
            
            _dispatcherService.BeginInvoke(() => MediaPlayerReady?.Invoke(this, _mediaPlayer));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] VLC Init failed: {ex.Message}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void SetupEventHandlers()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.Playing += (s, e) =>
        {
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, true));
            StartQualityMonitoring();
        };
        _mediaPlayer.Paused += (s, e) =>
        {
            StopQualityMonitoring();
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        };
        _mediaPlayer.Stopped += (s, e) =>
        {
            StopQualityMonitoring();
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        };
        _mediaPlayer.EndReached += (s, e) =>
        {
            StopQualityMonitoring();
            _dispatcherService.BeginInvoke(() =>
            {
                PlayingChanged?.Invoke(this, false);
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            });
        };
        
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
        CurrentUrl = url;
        System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] PlayAsync called with URL: {url}");
        
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (_mediaPlayer == null)
        {
            System.Diagnostics.Debug.WriteLine("[VideoPlayerService] ERROR: _mediaPlayer is null!");
            return;
        }

        
        _retryCount = 0;
        var generation = Interlocked.Increment(ref _playGeneration);
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = new CancellationTokenSource();
        var playToken = _playCts.Token;
        StopQualityMonitoring();
        lock (_qualitySync)
        {
            StreamQuality = null;
        }
        await PlayWithRetryAsync(url, playToken, generation);
    }

    private async Task PlayWithRetryAsync(string url, CancellationToken cancellationToken, long generation)
    {
        if (_mediaPlayer == null)
        {
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Media player hazır değil."));
            return;
        }

        if (cancellationToken.IsCancellationRequested || generation != Interlocked.Read(ref _playGeneration))
        {
            return;
        }

        try
        {
            if (_libVLC == null) return;
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
            try
            {
                await Task.Delay(5000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _mediaPlayer.EncounteredError -= OnError;
                return;
            }
            
            _mediaPlayer.EncounteredError -= OnError;

            if (cancellationToken.IsCancellationRequested || generation != Interlocked.Read(ref _playGeneration))
            {
                return;
            }
            
            if (errorOccurred && _retryCount < MaxRetries)
            {
                _retryCount++;
                try
                {
                    await Task.Delay(2000, cancellationToken); // 2 saniye bekle
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (cancellationToken.IsCancellationRequested || generation != Interlocked.Read(ref _playGeneration))
                {
                    return;
                }

                await PlayWithRetryAsync(url, cancellationToken, generation);
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
            var message = UserFriendlyErrorMessage.WithPrefix("Oynatma baslatilamadi", ex);
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, message));
        }
    }

    public void Pause()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.SetPause(true);
        }
    }

    public void Resume()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.SetPause(false);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _playGeneration);
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;
        StopQualityMonitoring();
        if (_mediaPlayer == null)
        {
            return;
        }

        _mediaPlayer.Stop();

        // Clear previous frame so failed loads do not leave stale video content visible.
        var currentMedia = _mediaPlayer.Media;
        _mediaPlayer.Media = null;
        currentMedia?.Dispose();
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
        if (_mediaPlayer == null)
        {
            return;
        }

        if (trackId >= 0)
        {
            _mediaPlayer.SetSpu(trackId);
            return;
        }

        // Disable subtitle for streams that require explicit OFF track ids.
        // Try common LibVLC OFF id first, then fallback to 0 when needed.
        _mediaPlayer.SetSpu(-1);
        if (_mediaPlayer.Spu != -1)
        {
            _mediaPlayer.SetSpu(0);
        }
    }

    private void StartQualityMonitoring()
    {
        StopQualityMonitoring();
        var generation = Interlocked.Read(ref _playGeneration);
        if (_mediaPlayer?.Media == null)
        {
            return;
        }
        _qualityMonitorCts = new CancellationTokenSource();
        _ = MonitorStreamQualityAsync(_qualityMonitorCts.Token, generation);
    }

    private void StopQualityMonitoring()
    {
        if (_qualityMonitorCts == null) return;
        try
        {
            _qualityMonitorCts.Cancel();
            _qualityMonitorCts.Dispose();
        }
        catch
        {
            // no-op
        }
        finally
        {
            _qualityMonitorCts = null;
        }
    }

    private async Task MonitorStreamQualityAsync(CancellationToken cancellationToken, long generation)
    {
        // Adaptive streamlerde ilk kalite yanlış/eksik gelebilir; birkaç kez yeniden ölç.
        var delaysMs = new[] { 1500, 2500, 3000, 5000, 7000 };
        for (var i = 0; i < delaysMs.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested || _mediaPlayer == null || !_mediaPlayer.IsPlaying)
            {
                return;
            }

            if (generation != Interlocked.Read(ref _playGeneration))
            {
                return;
            }

            try
            {
                await Task.Delay(delaysMs[i], cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await DetectStreamQualitySnapshotAsync(cancellationToken, generation);
        }
    }

    private async Task DetectStreamQualitySnapshotAsync(CancellationToken cancellationToken, long generation)
    {
        try
        {
            if (_mediaPlayer?.Media == null || cancellationToken.IsCancellationRequested) return;
            if (generation != Interlocked.Read(ref _playGeneration)) return;

            // Parse the media to get track info
            await _mediaPlayer.Media.Parse(MediaParseOptions.ParseNetwork, timeout: 5000);
            if (generation != Interlocked.Read(ref _playGeneration)) return;

            var measured = new StreamQualityInfo();

            foreach (var track in _mediaPlayer.Media.Tracks)
            {
                if (track.TrackType == TrackType.Video)
                {
                    var videoTrack = track.Data.Video;
                    measured.Width = Math.Max(measured.Width, (int)videoTrack.Width);
                    measured.Height = Math.Max(measured.Height, (int)videoTrack.Height);
                    var trackFps = videoTrack.FrameRateNum > 0 && videoTrack.FrameRateDen > 0
                        ? (int)Math.Round((double)videoTrack.FrameRateNum / videoTrack.FrameRateDen, MidpointRounding.AwayFromZero)
                        : 0;
                    measured.Fps = Math.Max(measured.Fps, trackFps);
                    measured.VideoCodec = track.Codec > 0 
                        ? FourCCToString(track.Codec) 
                        : track.Description ?? "";
                    measured.VideoBitrate = Math.Max(measured.VideoBitrate, (int)track.Bitrate);
                }
                else if (track.TrackType == TrackType.Audio)
                {
                    var audioTrack = track.Data.Audio;
                    measured.AudioChannels = Math.Max(measured.AudioChannels, (int)audioTrack.Channels);
                    measured.AudioCodec = track.Codec > 0 
                        ? FourCCToString(track.Codec)
                        : track.Description ?? "";
                    measured.AudioBitrate = Math.Max(measured.AudioBitrate, (int)(track.Bitrate / 1000)); // bps → kbps
                }
            }

            var runtimeFps = _mediaPlayer.Fps;
            if (runtimeFps > 0)
            {
                measured.Fps = Math.Max(measured.Fps, (int)Math.Round(runtimeFps, MidpointRounding.AwayFromZero));
            }

            StreamQualityInfo merged;
            lock (_qualitySync)
            {
                merged = MergeQuality(StreamQuality, measured);
                StreamQuality = merged;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[VideoPlayerService] Quality detected: {merged.Width}x{merged.Height} " +
                $"@{merged.Fps}fps, {merged.VideoCodec}, {merged.VideoBitrate}bps | " +
                $"Audio: {merged.AudioCodec} {merged.AudioChannels}ch {merged.AudioBitrate}kbps");

            _dispatcherService.BeginInvoke(() => QualityDetected?.Invoke(this, merged));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] Quality detection error: {ex.Message}");
        }
    }

    private static StreamQualityInfo MergeQuality(StreamQualityInfo? current, StreamQualityInfo measured)
    {
        if (current == null) return measured;

        return new StreamQualityInfo
        {
            Width = Math.Max(current.Width, measured.Width),
            Height = Math.Max(current.Height, measured.Height),
            Fps = Math.Max(current.Fps, measured.Fps),
            VideoBitrate = Math.Max(current.VideoBitrate, measured.VideoBitrate),
            VideoCodec = !string.IsNullOrWhiteSpace(measured.VideoCodec) ? measured.VideoCodec : current.VideoCodec,
            AudioBitrate = Math.Max(current.AudioBitrate, measured.AudioBitrate),
            AudioChannels = Math.Max(current.AudioChannels, measured.AudioChannels),
            AudioCodec = !string.IsNullOrWhiteSpace(measured.AudioCodec) ? measured.AudioCodec : current.AudioCodec
        };
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
        
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;
        StopQualityMonitoring();
        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
        _libVLC?.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}



