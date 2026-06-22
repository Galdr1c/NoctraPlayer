using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Android.Media;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidVideoPlayerService : Java.Lang.Object, IVideoPlayerService
{
    private readonly AndroidVideoSurfaceService _videoSurfaceService;
    private readonly object _trackSync = new();
    private readonly List<(int Id, string? Name)> _audioTracks = new();
    private readonly List<(int Id, string? Name)> _subtitleTracks = new();
    private global::Android.Media.MediaPlayer? _mediaPlayer;
    private string? _currentUrl;
    private bool _isDisposed;
    private bool _hasLoadedMedia;
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 100;
    private bool _isMuted;
    private float _playbackRate = 1f;
    private int _selectedAudioTrack = -1;
    private int _selectedSubtitleTrack = -1;
    private int _subtitleSequence;

    public string? CurrentUrl => _currentUrl;
    public bool IsPlaying => _mediaPlayer?.IsPlaying == true;
    public PlaybackState State => _state;
    public bool HasLoadedMedia => _hasLoadedMedia;
    public long CurrentTimeMilliseconds => _mediaPlayer is null ? 0 : _mediaPlayer.CurrentPosition;
    public double Duration => _mediaPlayer is null ? 0 : _mediaPlayer.Duration / 1000d;

    public IReadOnlyList<(int Id, string? Name)> AudioTracks
    {
        get
        {
            lock (_trackSync)
            {
                return _audioTracks.ToArray();
            }
        }
    }

    public IReadOnlyList<(int Id, string? Name)> SubtitleTracks
    {
        get
        {
            lock (_trackSync)
            {
                return _subtitleTracks.ToArray();
            }
        }
    }

    public StreamQualityInfo? StreamQuality { get; private set; }

    public event EventHandler<int>? VolumeChanged;
    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler? PlayerReady;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string?>? SubtitleTextChanged;
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
        _selectedAudioTrack = -1;
        _selectedSubtitleTrack = -1;
        ClearTrackCache();
        RaiseSubtitleTextChanged(null);
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
                RefreshTrackCache(player);
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
            RaiseSubtitleTextChanged(null);
            PlayingChanged?.Invoke(this, false);
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };
        player.Error += (_, args) =>
        {
            _state = PlaybackState.Error;
            RaiseSubtitleTextChanged(null);
            var message = $"Android.Media.MediaPlayer error: {args.What}/{args.Extra}";
            ErrorOccurred?.Invoke(this, message);
            completion.TrySetException(new InvalidOperationException(message));
            args.Handled = true;
        };
        player.BufferingUpdate += (_, args) => BufferingChanged?.Invoke(this, args.Percent);
        player.TimedText += (_, args) => RaiseSubtitleTextChanged(args.Text?.Text);
        player.SubtitleData += (_, args) => HandleSubtitleData(args.Data);

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
            RaiseSubtitleTextChanged(null);
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
            ClearTrackCache();
            RaiseSubtitleTextChanged(null);
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
            _selectedAudioTrack = -1;
            _selectedSubtitleTrack = -1;
            ClearTrackCache();
            RaiseSubtitleTextChanged(null);
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

    public void SetAudioTrack(int trackId)
    {
        var player = _mediaPlayer;
        if (player is null || !_hasLoadedMedia || trackId < 0)
        {
            return;
        }

        try
        {
            player.SelectTrack(trackId);
            _selectedAudioTrack = trackId;
            RefreshTrackCache(player);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetAudioTrack({trackId}) failed: {ex.Message}");
        }
    }

    public void SetSubtitleTrack(int trackId)
    {
        var player = _mediaPlayer;
        if (player is null || !_hasLoadedMedia)
        {
            return;
        }

        try
        {
            if (trackId < 0)
            {
                DeselectSubtitleTracks(player);
                _selectedSubtitleTrack = -1;
                RaiseSubtitleTextChanged(null);
                return;
            }

            if (_selectedSubtitleTrack >= 0 && _selectedSubtitleTrack != trackId)
            {
                TryDeselectTrack(player, _selectedSubtitleTrack);
            }

            player.SelectTrack(trackId);
            _selectedSubtitleTrack = trackId;
            RefreshTrackCache(player);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetSubtitleTrack({trackId}) failed: {ex.Message}");
        }
    }

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

    private void RefreshTrackCache(global::Android.Media.MediaPlayer player)
    {
        try
        {
            var trackInfo = player.GetTrackInfo();
            var audio = new List<(int Id, string? Name)>();
            var subtitles = new List<(int Id, string? Name)>();
            var audioOrdinal = 1;
            var subtitleOrdinal = 1;

            for (var i = 0; i < trackInfo.Length; i++)
            {
                var info = trackInfo[i];
                switch (info.TrackType)
                {
                    case MediaTrackType.Audio:
                        audio.Add((i, BuildTrackLabel(info, "Audio", audioOrdinal++)));
                        break;
                    case MediaTrackType.Timedtext:
                    case MediaTrackType.Subtitle:
                        subtitles.Add((i, BuildTrackLabel(info, "Subtitle", subtitleOrdinal++)));
                        break;
                }
            }

            lock (_trackSync)
            {
                _audioTracks.Clear();
                _audioTracks.AddRange(audio);
                _subtitleTracks.Clear();
                _subtitleTracks.AddRange(subtitles);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] RefreshTrackCache failed: {ex.Message}");
        }
    }

    private void ClearTrackCache()
    {
        lock (_trackSync)
        {
            _audioTracks.Clear();
            _subtitleTracks.Clear();
        }
    }

    private void DeselectSubtitleTracks(global::Android.Media.MediaPlayer player)
    {
        if (_selectedSubtitleTrack >= 0)
        {
            TryDeselectTrack(player, _selectedSubtitleTrack);
            return;
        }

        // Fallback: bazı cihazlarda seçili text track bilgisi güvenilir dönmeyebilir; tüm text/subtitle trackleri kapatmayı dene.
        foreach (var (id, _) in SubtitleTracks)
        {
            TryDeselectTrack(player, id);
        }
    }

    private static void TryDeselectTrack(global::Android.Media.MediaPlayer player, int trackId)
    {
        try
        {
            player.DeselectTrack(trackId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] DeselectTrack({trackId}) failed: {ex.Message}");
        }
    }

    private void RaiseSubtitleTextChanged(string? text, long clearAfterMilliseconds = 0)
    {
        var normalizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        var sequence = Interlocked.Increment(ref _subtitleSequence);
        SubtitleTextChanged?.Invoke(this, normalizedText);

        if (normalizedText.Length == 0 || clearAfterMilliseconds <= 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay((int)Math.Clamp(clearAfterMilliseconds, 1, int.MaxValue)).ConfigureAwait(false);
                if (sequence == Volatile.Read(ref _subtitleSequence))
                {
                    RaiseSubtitleTextChanged(null);
                }
            }
            catch
            {
                // Best-effort subtitle cue cleanup only.
            }
        });
    }

    private void HandleSubtitleData(SubtitleData? data)
    {
        if (data is null)
        {
            return;
        }

        if (_selectedSubtitleTrack >= 0 && data.TrackIndex != _selectedSubtitleTrack)
        {
            return;
        }

        try
        {
            var text = DecodeSubtitlePayload(data.GetData());
            if (!string.IsNullOrWhiteSpace(text))
            {
                RaiseSubtitleTextChanged(text, data.DurationUs / 1000);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SubtitleData decode failed: {ex.Message}");
        }
    }

    private static string DecodeSubtitlePayload(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var text = Encoding.UTF8.GetString(data).Trim('\0', '\r', '\n', ' ');
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("-->", StringComparison.Ordinal))
            .Where(line => !line.All(char.IsDigit));

        var cleaned = string.Join("\n", lines);
        cleaned = Regex.Replace(cleaned, "<[^>]+>", string.Empty);
        return cleaned.Trim();
    }

    private static string BuildTrackLabel(global::Android.Media.MediaPlayer.TrackInfo info, string fallbackPrefix, int ordinal)
    {
        var label = $"{fallbackPrefix} {ordinal}";

        var language = info.Language;
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "und", StringComparison.OrdinalIgnoreCase))
        {
            label += $" ({language})";
        }

        try
        {
            var format = info.Format;
            if (format is not null && format.ContainsKey(MediaFormat.KeyMime))
            {
                var mime = format.GetString(MediaFormat.KeyMime);
                if (!string.IsNullOrWhiteSpace(mime))
                {
                    label += $" • {MimeToCodecName(mime)}";
                }
            }
        }
        catch
        {
            // Format metadata can be unavailable on some streams/devices.
        }

        return label;
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
            // Subtitle/text codecs
            "text/vtt" => "WebVTT",
            "application/x-subrip" => "SRT",
            "application/cea-608" => "CEA-608",
            "application/cea-708" => "CEA-708",
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
