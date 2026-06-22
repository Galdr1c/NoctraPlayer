using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.Media;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidVideoPlayerService : Java.Lang.Object, IVideoPlayerService
{
    private readonly AndroidVideoSurfaceService _videoSurfaceService;
    private global::Android.Media.MediaPlayer? _mediaPlayer;
    private string? _currentUrl;
    private bool _isDisposed;
    private bool _hasLoadedMedia;
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 100;
    private bool _isMuted;
    private float _playbackRate = 1f;

    public string? CurrentUrl => _currentUrl;
    public bool IsPlaying => _mediaPlayer?.IsPlaying == true;
    public PlaybackState State => _state;
    public bool HasLoadedMedia => _hasLoadedMedia;
    public long CurrentTimeMilliseconds => _mediaPlayer is null ? 0 : _mediaPlayer.CurrentPosition;
    public double Duration => _mediaPlayer is null ? 0 : _mediaPlayer.Duration / 1000d;
    public IReadOnlyList<(int Id, string? Name)> AudioTracks { get; } = Array.Empty<(int Id, string? Name)>();
    public IReadOnlyList<(int Id, string? Name)> SubtitleTracks { get; } = Array.Empty<(int Id, string? Name)>();
    public StreamQualityInfo? StreamQuality { get; private set; }

    public event EventHandler<int>? VolumeChanged;
    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler? PlayerReady;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<StreamQualityInfo>? QualityDetected;

    public AndroidVideoPlayerService(AndroidVideoSurfaceService videoSurfaceService)
    {
        _videoSurfaceService = videoSurfaceService;
    }

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            ApplyVolume();
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            ApplyVolume();
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    public double Position
    {
        get
        {
            if (_mediaPlayer is null || _mediaPlayer.Duration <= 0)
            {
                return 0;
            }

            return Math.Clamp((double)_mediaPlayer.CurrentPosition / _mediaPlayer.Duration, 0, 1);
        }
        set
        {
            if (_mediaPlayer is null || _mediaPlayer.Duration <= 0)
            {
                return;
            }

            var target = Math.Clamp(value, 0, 1) * _mediaPlayer.Duration;
            SeekToTime((long)target);
        }
    }

    public float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = value <= 0 ? 1f : value;
            ApplyPlaybackRate();
        }
    }

    public async Task PlayAsync(string url, double startTimeSeconds = 0)
    {
        ThrowIfDisposed();
        Stop();

        _currentUrl = url;
        _state = PlaybackState.Buffering;
        BufferingChanged?.Invoke(this, 0);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new global::Android.Media.MediaPlayer();
        _mediaPlayer = player;

        player.Prepared += (_, _) =>
        {
            try
            {
                _hasLoadedMedia = true;
                PlayerReady?.Invoke(this, EventArgs.Empty);
                UpdateStreamQualityFromPreparedPlayer(player);
                if (startTimeSeconds > 0)
                {
                    player.SeekTo((int)(startTimeSeconds * 1000));
                }

                ApplyVolume();
                player.Start();
                ApplyPlaybackRate();
                _state = PlaybackState.Playing;
                PlayingChanged?.Invoke(this, true);
                BufferingChanged?.Invoke(this, 100);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _state = PlaybackState.Error;
                ErrorOccurred?.Invoke(this, ex.Message);
                completion.TrySetException(ex);
            }
        };
        player.Completion += (_, _) =>
        {
            _state = PlaybackState.Stopped;
            PlayingChanged?.Invoke(this, false);
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };
        player.Error += (_, args) =>
        {
            _state = PlaybackState.Error;
            var message = $"Android.Media.MediaPlayer error: {args.What}/{args.Extra}";
            ErrorOccurred?.Invoke(this, message);
            completion.TrySetException(new InvalidOperationException(message));
            args.Handled = true;
        };
        player.BufferingUpdate += (_, args) => BufferingChanged?.Invoke(this, args.Percent);

        try
        {
            await _videoSurfaceService.ShowAsync().ConfigureAwait(false);
            var surface = await _videoSurfaceService.WaitForSurfaceAsync().ConfigureAwait(false);
            if (surface is not null)
            {
                player.SetSurface(surface);
            }

            player.SetDataSource(url);
            player.PrepareAsync();
            await completion.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _state = PlaybackState.Error;
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public Task HardSeekAsync(double seconds)
    {
        SeekToTime((long)(seconds * 1000));
        return Task.CompletedTask;
    }

    public Task ReinitializeAsync()
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Pause()
    {
        if (_mediaPlayer?.IsPlaying == true)
        {
            _mediaPlayer.Pause();
            _state = PlaybackState.Paused;
            PlayingChanged?.Invoke(this, false);
        }
    }

    public void Resume()
    {
        if (_mediaPlayer is not null && _hasLoadedMedia)
        {
            _mediaPlayer.Start();
            ApplyPlaybackRate();
            _state = PlaybackState.Playing;
            PlayingChanged?.Invoke(this, true);
        }
    }

    public void Stop()
    {
        if (_mediaPlayer is null)
        {
            _state = PlaybackState.Stopped;
            _hasLoadedMedia = false;
            return;
        }

        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }
        }
        catch
        {
        }
        finally
        {
            _mediaPlayer.Release();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
            _hasLoadedMedia = false;
            _state = PlaybackState.Stopped;
            PlayingChanged?.Invoke(this, false);
        }
    }

    public void SeekToTime(long milliseconds)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.SeekTo((int)Math.Clamp(milliseconds, 0, int.MaxValue));
        PositionChanged?.Invoke(this, milliseconds / 1000d);
    }

    public void PlayLoadedMedia() => Resume();
    public void SetAudioTrack(int trackId) { }
    public void SetSubtitleTrack(int trackId) { }

    public void SetVideoLayout(string? aspectRatio, string? cropGeometry)
    {
        // Video boyutunu surface servise aktar ki transform matrisi doğru hesaplansın.
        if (StreamQuality is { Width: > 0, Height: > 0 })
        {
            _videoSurfaceService.SetVideoSize(StreamQuality.Width, StreamQuality.Height);
        }
        _videoSurfaceService.SetVideoLayout(aspectRatio, cropGeometry);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            Stop();
            _isDisposed = true;
        }

        base.Dispose(disposing);
    }

    private void ApplyVolume()
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        var level = _isMuted ? 0f : _volume / 100f;
        _mediaPlayer.SetVolume(level, level);
    }

    private void ApplyPlaybackRate()
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        try
        {
            var playbackParams = _mediaPlayer.PlaybackParams?.SetSpeed(_playbackRate);
            if (playbackParams is not null)
            {
                _mediaPlayer.PlaybackParams = playbackParams;
            }
        }
        catch
        {
            // Android versions/devices may reject speed changes for a source.
        }
    }

    private void UpdateStreamQualityFromPreparedPlayer(global::Android.Media.MediaPlayer player)
    {
        var width = player.VideoWidth;
        var height = player.VideoHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var quality = new StreamQualityInfo
        {
            Width = width,
            Height = height
        };

        StreamQuality = quality;

        // MediaExtractor API'si (özellikle network stream'lerde) UI thread'i bloke edebilir.
        // Bu yüzden arka planda çalıştırıyoruz — QualityDetected event'i UI'ı bilgilendirir.
        var url = _currentUrl;
        _ = Task.Run(() =>
        {
            try
            {
                var extracted = new StreamQualityInfo { Width = width, Height = height };
                ExtractTrackMetadata(extracted, url);

                // Codec/bitrate/fps bilgilerini mevcut quality nesnesine aktar.
                if (!string.IsNullOrEmpty(extracted.VideoCodec))
                    quality.VideoCodec = extracted.VideoCodec;
                if (extracted.VideoBitrate > 0)
                    quality.VideoBitrate = extracted.VideoBitrate;
                if (extracted.Fps > 0)
                    quality.Fps = extracted.Fps;
                if (!string.IsNullOrEmpty(extracted.AudioCodec))
                    quality.AudioCodec = extracted.AudioCodec;
                if (extracted.AudioBitrate > 0)
                    quality.AudioBitrate = extracted.AudioBitrate;
                if (extracted.AudioChannels > 0)
                    quality.AudioChannels = extracted.AudioChannels;

                QualityDetected?.Invoke(this, quality);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] Background quality extraction failed: {ex.Message}");
            }
        });

        // Video boyutunu surface servise aktar ki mevcut transform doğru uygulansın.
        _videoSurfaceService.SetVideoSize(width, height);

        QualityDetected?.Invoke(this, quality);
    }

    /// <summary>
    /// MediaExtractor kullanarak stream'in video ve ses track metadata'sını çıkarır.
    /// Codec (MIME → insan-okunabilir), bitrate, FPS ve kanal bilgilerini doldurur.
    /// Bu metodun arka plan thread'inde çağrılması önerilir.
    /// </summary>
    private void ExtractTrackMetadata(StreamQualityInfo quality, string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        MediaExtractor? extractor = null;
        try
        {
            extractor = new MediaExtractor();
            extractor.SetDataSource(url);

            for (int i = 0; i < extractor.TrackCount; i++)
            {
                var format = extractor.GetTrackFormat(i);
                var mime = format.GetString(MediaFormat.KeyMime) ?? string.Empty;

                if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    // Video codec
                    quality.VideoCodec = MimeToCodecName(mime);

                    // Video bitrate (bps → kbps)
                    if (format.ContainsKey(MediaFormat.KeyBitRate))
                    {
                        quality.VideoBitrate = format.GetInteger(MediaFormat.KeyBitRate) / 1000;
                    }

                    // FPS
                    if (format.ContainsKey(MediaFormat.KeyFrameRate))
                    {
                        quality.Fps = format.GetInteger(MediaFormat.KeyFrameRate);
                    }

                    // Width/Height fallback (MediaExtractor daha doğru olabilir)
                    if (format.ContainsKey(MediaFormat.KeyWidth) && quality.Width <= 0)
                    {
                        quality.Width = format.GetInteger(MediaFormat.KeyWidth);
                    }
                    if (format.ContainsKey(MediaFormat.KeyHeight) && quality.Height <= 0)
                    {
                        quality.Height = format.GetInteger(MediaFormat.KeyHeight);
                    }
                }
                else if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    // Audio codec
                    quality.AudioCodec = MimeToCodecName(mime);

                    // Audio bitrate (bps → kbps)
                    if (format.ContainsKey(MediaFormat.KeyBitRate))
                    {
                        quality.AudioBitrate = format.GetInteger(MediaFormat.KeyBitRate) / 1000;
                    }

                    // Kanal sayısı
                    if (format.ContainsKey(MediaFormat.KeyChannelCount))
                    {
                        quality.AudioChannels = format.GetInteger(MediaFormat.KeyChannelCount);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] ExtractTrackMetadata failed: {ex.Message}");
        }
        finally
        {
            extractor?.Release();
            extractor?.Dispose();
        }
    }

    /// <summary>
    /// MIME type'ı insan-okunabilir codec adına dönüştürür.
    /// Örnek: "video/avc" → "H.264", "audio/mp4a-latm" → "AAC"
    /// </summary>
    private static string MimeToCodecName(string mime)
    {
        return mime.ToLowerInvariant() switch
        {
            // Video codecs
            "video/avc" => "H.264",
            "video/hevc" or "video/h265" => "H.265",
            "video/mp4v-es" or "video/mpeg4" => "MPEG-4",
            "video/3gpp" => "H.263",
            "video/vp8" => "VP8",
            "video/vp9" => "VP9",
            "video/av01" => "AV1",
            "video/mpeg2" => "MPEG-2",
            // Audio codecs
            "audio/mp4a-latm" or "audio/mpeg" => "AAC",
            "audio/opus" => "Opus",
            "audio/vorbis" => "Vorbis",
            "audio/flac" => "FLAC",
            "audio/ac3" => "AC3",
            "audio/eac3" => "E-AC3",
            "audio/mp3" => "MP3",
            "audio/aac" => "AAC",
            // Fallback: MIME type'ın kendisini döndür
            _ => mime.Contains('/') ? mime.Split('/')[^1].ToUpperInvariant() : mime.ToUpperInvariant()
        };
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AndroidVideoPlayerService));
        }
    }
}
