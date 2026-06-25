using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Media;
using Android.OS;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidVideoPlayerService : Java.Lang.Object, IVideoPlayerService
{
    private const string DefaultUserAgent = "Noctra.Mobile/1.0";

    private readonly AndroidVideoSurfaceService _videoSurfaceService;
    private readonly Context _applicationContext;
    private readonly ISettingsService _settingsService;
    private readonly INetworkService _networkService;
    private readonly AudioManager _audioManager;
    private readonly AudioFocusListener _audioFocusListener;
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
    private string _lastUserAgent = string.Empty;
    private BufferSize _lastVideoBufferSize = BufferSize.Normal;
    private bool _lastHardwareAcceleration = true;
    private DataUsageLevel _lastDataUsage = DataUsageLevel.Auto;
    private CancellationTokenSource? _reinitializeCts;

    // Audio focus: Android'de başka bir uygulama ses çıkardığında (telefon, alarm, müzik)
    // oynatmayı duraklatmak / sesi kısmak için sistem seviyesinde koordinasyon.
    private enum AudioFocusState { None, Granted, TransientLoss, TransientLossCanDuck, Lost }
    private AudioFocusState _audioFocusState = AudioFocusState.None;
    private bool _pausedByAudioFocus;

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

    public AndroidVideoPlayerService(
        AndroidVideoSurfaceService videoSurfaceService,
        Context applicationContext,
        ISettingsService settingsService,
        INetworkService networkService)
    {
        _videoSurfaceService = videoSurfaceService;
        _applicationContext = applicationContext.ApplicationContext ?? applicationContext;
        _settingsService = settingsService;
        _networkService = networkService;

        _audioManager = (AudioManager)_applicationContext.GetSystemService(Context.AudioService)!;
        _audioFocusListener = new AudioFocusListener();
        _audioFocusListener.FocusChanged += OnAudioFocusChanged;

        ApplySettingsSnapshot(_settingsService.Settings, updateAudioState: false);
        _settingsService.SettingsChanged += OnSettingsChanged;
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
        var playbackSource = BuildPlaybackSource(url);
        ConfigureAndroidMediaPlayer(player);
        EnsureNetworkCanPlay(playbackSource);
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
                RequestAudioFocus();
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

            if (playbackSource.IsNetworkStream)
            {
                player.SetDataSource(_applicationContext, global::Android.Net.Uri.Parse(playbackSource.Url), playbackSource.Headers);
            }
            else
            {
                player.SetDataSource(playbackSource.Url);
            }

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

    public async Task ReinitializeAsync()
    {
        ThrowIfDisposed();

        var url = _currentUrl;
        if (string.IsNullOrWhiteSpace(url) || !_hasLoadedMedia)
        {
            Stop();
            return;
        }

        var currentPositionSeconds = CurrentTimeMilliseconds / 1000d;
        var wasPlaying = IsPlaying;

        await PlayAsync(url, currentPositionSeconds).ConfigureAwait(false);

        if (!wasPlaying)
        {
            Pause();
        }
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
            // Kullanıcı manuel olarak devam ettirince, başka uygulama ses çıkarmıyorsa
            // tekrar audio focus talep et (transient loss sırasında duraklatılmış olabilir).
            RequestAudioFocus();
            _pausedByAudioFocus = false;
            PlayingChanged?.Invoke(this, true);
        }
    }

    public void Stop()
    {
        AbandonAudioFocus();

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
            _settingsService.SettingsChanged -= OnSettingsChanged;
            _audioFocusListener.FocusChanged -= OnAudioFocusChanged;
            AbandonAudioFocus();
            _reinitializeCts?.Cancel();
            _reinitializeCts?.Dispose();
            Stop();
            _isDisposed = true;
        }

        base.Dispose(disposing);
    }

    private void ApplySettingsSnapshot(AppSettings settings, bool updateAudioState)
    {
        var normalizedVolume = Math.Clamp(settings.DefaultVolume, 0, 100);
        var volumeChanged = _volume != normalizedVolume || _isMuted != settings.IsMuted;

        _volume = normalizedVolume;
        _isMuted = settings.IsMuted;
        _lastUserAgent = settings.UserAgent ?? string.Empty;
        _lastVideoBufferSize = settings.VideoBufferSize;
        _lastHardwareAcceleration = settings.HardwareAcceleration;
        _lastDataUsage = settings.DataUsage;

        if (updateAudioState && volumeChanged)
        {
            ApplyVolume();
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    private void OnSettingsChanged()
    {
        var settings = _settingsService.Settings;
        var previousUserAgent = _lastUserAgent;
        var previousBufferSize = _lastVideoBufferSize;
        var previousDataUsage = _lastDataUsage;
        var previousHardwareAcceleration = _lastHardwareAcceleration;

        ApplySettingsSnapshot(settings, updateAudioState: true);

        var requiresDataSourceReopen =
            !string.Equals(previousUserAgent, _lastUserAgent, StringComparison.Ordinal) ||
            previousBufferSize != _lastVideoBufferSize ||
            previousDataUsage != _lastDataUsage;

        // Android MediaPlayer uses the platform decoder/surface path and does not expose a VLC-style
        // per-source hardware acceleration toggle. Keep the value tracked so a future Media3/ExoPlayer
        // service can honor it without changing the settings contract.
        if (previousHardwareAcceleration != _lastHardwareAcceleration)
        {
            System.Diagnostics.Debug.WriteLine(
                "[AndroidVideoPlayerService] HardwareAcceleration changed; Android MediaPlayer has no per-player toggle.");
        }

        if (requiresDataSourceReopen && _hasLoadedMedia && !string.IsNullOrWhiteSpace(_currentUrl))
        {
            ScheduleReinitialize();
        }
    }

    private void ScheduleReinitialize()
    {
        var oldCts = _reinitializeCts;
        var cts = new CancellationTokenSource();
        _reinitializeCts = cts;

        try
        {
            oldCts?.Cancel();
            oldCts?.Dispose();
        }
        catch
        {
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
                if (!cts.Token.IsCancellationRequested)
                {
                    await ReinitializeAsync().ConfigureAwait(false);
                }
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] Reinitialize after settings change failed: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }, cts.Token);
    }

    private void ConfigureAndroidMediaPlayer(global::Android.Media.MediaPlayer player)
    {
        try
        {
            using var builder = new AudioAttributes.Builder();
            builder.SetUsage(AudioUsageKind.Media);
            builder.SetContentType(AudioContentType.Movie);

            using var attributes = builder.Build();
            if (attributes is not null)
            {
                player.SetAudioAttributes(attributes);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetAudioAttributes failed: {ex.Message}");
        }

        try
        {
            player.SetWakeMode(_applicationContext, WakeLockFlags.Partial);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetWakeMode failed: {ex.Message}");
        }

        try
        {
            player.SetScreenOnWhilePlaying(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetScreenOnWhilePlaying failed: {ex.Message}");
        }
    }

    private void EnsureNetworkCanPlay(PlaybackSource playbackSource)
    {
        if (!playbackSource.IsNetworkStream)
        {
            return;
        }

        if (string.Equals(_networkService.CurrentNetworkStatus, "Offline", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ağ bağlantısı yok. Yayın başlatılamadı.");
        }
    }

    private PlaybackSource BuildPlaybackSource(string url)
    {
        var (cleanUrl, inlineHeaders) = SplitInlineHeaders(url);
        var isNetworkStream = IsNetworkStreamUrl(cleanUrl);

        if (!isNetworkStream)
        {
            return new PlaybackSource(cleanUrl, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), false);
        }

        var headers = BuildNetworkHeaders(cleanUrl);
        foreach (var header in inlineHeaders)
        {
            var normalizedName = NormalizeHeaderName(header.Key);
            if (!string.IsNullOrWhiteSpace(normalizedName) && !string.IsNullOrWhiteSpace(header.Value))
            {
                headers[normalizedName] = header.Value.Trim();
            }
        }

        return new PlaybackSource(cleanUrl, headers, true);
    }

    private Dictionary<string, string> BuildNetworkHeaders(string url)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = ResolveUserAgent(),
            ["Accept"] = GetAcceptHeader(url),
            ["Connection"] = "keep-alive"
        };

        if (ShouldRequestReducedData())
        {
            headers["Save-Data"] = "on";
        }

        return headers;
    }

    private string ResolveUserAgent()
    {
        return string.IsNullOrWhiteSpace(_lastUserAgent)
            ? DefaultUserAgent
            : _lastUserAgent.Trim();
    }

    private bool ShouldRequestReducedData()
    {
        if (_lastDataUsage == DataUsageLevel.Low || _lastDataUsage == DataUsageLevel.Medium)
        {
            return true;
        }

        return _lastDataUsage == DataUsageLevel.Auto &&
               string.Equals(_networkService.CurrentNetworkStatus, "Cellular", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAcceptHeader(string url)
    {
        var profile = DetectStreamProfile(url);
        return profile switch
        {
            AndroidStreamProfile.LiveM3u8 => "application/vnd.apple.mpegurl,application/x-mpegURL,*/*",
            AndroidStreamProfile.LiveTs => "video/MP2T,video/*,*/*",
            AndroidStreamProfile.VodMp4 => "video/mp4,video/*,*/*",
            AndroidStreamProfile.VodMkv => "video/x-matroska,video/*,*/*",
            _ => "*/*"
        };
    }

    private static (string CleanUrl, Dictionary<string, string> Headers) SplitInlineHeaders(string url)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pipeIndex = url.IndexOf('|');
        if (pipeIndex < 0)
        {
            return (url, headers);
        }

        var cleanUrl = url[..pipeIndex].Trim();
        var rawOptions = url[(pipeIndex + 1)..].Trim();
        if (rawOptions.Length == 0)
        {
            return (cleanUrl, headers);
        }

        foreach (var part in rawOptions.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= part.Length - 1)
            {
                continue;
            }

            var key = WebUtility.UrlDecode(part[..equalsIndex]).Trim();
            var value = WebUtility.UrlDecode(part[(equalsIndex + 1)..]).Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                headers[key] = value;
            }
        }

        return (cleanUrl, headers);
    }

    private static string NormalizeHeaderName(string rawName)
    {
        var normalized = rawName.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "ua" => "User-Agent",
            "useragent" => "User-Agent",
            "user-agent" => "User-Agent",
            "http-user-agent" => "User-Agent",
            "referer" => "Referer",
            "referrer" => "Referer",
            "http-referrer" => "Referer",
            "http-referer" => "Referer",
            "origin" => "Origin",
            "cookie" => "Cookie",
            "authorization" => "Authorization",
            "x-user-agent" => "X-User-Agent",
            _ => normalized
        };
    }

    private static bool IsNetworkStreamUrl(string url)
    {
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);
    }

    private static AndroidStreamProfile DetectStreamProfile(string url)
    {
        var lowerUrl = url.ToLowerInvariant();
        var path = lowerUrl;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        if (path.EndsWith(".m3u8") || path.EndsWith("/m3u8") || lowerUrl.Contains("format=m3u8") || lowerUrl.Contains("extension=m3u8"))
        {
            return AndroidStreamProfile.LiveM3u8;
        }

        if (path.EndsWith(".ts") || path.EndsWith("/ts") || lowerUrl.Contains("extension=ts"))
        {
            return AndroidStreamProfile.LiveTs;
        }

        if (path.EndsWith(".mp4") || path.Contains("/movie/"))
        {
            return AndroidStreamProfile.VodMp4;
        }

        if (path.EndsWith(".mkv"))
        {
            return AndroidStreamProfile.VodMkv;
        }

        return AndroidStreamProfile.Unknown;
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

        var text = System.Text.Encoding.UTF8.GetString(data).Trim('\0', '\r', '\n', ' ');
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
            var playbackSource = BuildPlaybackSource(url);
            if (playbackSource.IsNetworkStream)
            {
                extractor.SetDataSource(playbackSource.Url, playbackSource.Headers);
            }
            else
            {
                extractor.SetDataSource(playbackSource.Url);
            }

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

    private sealed class PlaybackSource
    {
        public PlaybackSource(string url, IDictionary<string, string> headers, bool isNetworkStream)
        {
            Url = url;
            Headers = headers;
            IsNetworkStream = isNetworkStream;
        }

        public string Url { get; }
        public IDictionary<string, string> Headers { get; }
        public bool IsNetworkStream { get; }
    }

    private enum AndroidStreamProfile
    {
        Unknown,
        LiveTs,
        LiveM3u8,
        VodMp4,
        VodMkv
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AndroidVideoPlayerService));
        }
    }

    // ── Audio Focus ───────────────────────────────────────────────────────────

    private void RequestAudioFocus()
    {
        if (_isDisposed)
        {
            return;
        }

        var result = _audioManager.RequestAudioFocus(_audioFocusListener, Stream.Music, AudioFocus.Gain);
        _audioFocusState = result == AudioFocusRequest.Granted
            ? AudioFocusState.Granted
            : AudioFocusState.None;
    }

    private void AbandonAudioFocus()
    {
        if (_isDisposed)
        {
            return;
        }

        _audioFocusState = AudioFocusState.None;
        _pausedByAudioFocus = false;
        try
        {
            _audioManager.AbandonAudioFocus(_audioFocusListener);
        }
        catch
        {
            // Best-effort; cleanup path.
        }
    }

    private void OnAudioFocusChanged(AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case AudioFocus.Gain:
                // Başka uygulamanın sesi bitti → eski duruma dön.
                if (_pausedByAudioFocus && _mediaPlayer is not null && _hasLoadedMedia)
                {
                    _mediaPlayer.Start();
                    ApplyPlaybackRate();
                    _state = PlaybackState.Playing;
                    PlayingChanged?.Invoke(this, true);
                }
                _pausedByAudioFocus = false;

                // Duck (ses kısma) bırakılmışsa orijinal ses seviyesini geri yükle.
                if (_audioFocusState == AudioFocusState.TransientLossCanDuck)
                {
                    ApplyVolume();
                }

                _audioFocusState = AudioFocusState.Granted;
                break;

            case AudioFocus.LossTransient:
                // Kısa süreli kayıp (telefon zili, bildirim) → duraklat, geri geldiğinde devam et.
                _audioFocusState = AudioFocusState.TransientLoss;
                if (_mediaPlayer?.IsPlaying == true)
                {
                    _mediaPlayer.Pause();
                    _state = PlaybackState.Paused;
                    _pausedByAudioFocus = true;
                    PlayingChanged?.Invoke(this, false);
                }
                break;

            case AudioFocus.LossTransientCanDuck:
                // Geçici duck (navigasyon sesi, kısa bildirim) → sesi kıs.
                _audioFocusState = AudioFocusState.TransientLossCanDuck;
                try
                {
                    _mediaPlayer?.SetVolume(0.2f, 0.2f);
                }
                catch
                {
                    // MediaPlayer hazır değilse sessizce yoksay.
                }
                break;

            case AudioFocus.Loss:
                // Kalıcı kayıp (başka medya uygulaması oynatmaya başladı) → oynatmayı bırak.
                _audioFocusState = AudioFocusState.Lost;
                Pause();
                break;
        }
    }

    /// <summary>
    /// Android AudioManager'ın audio focus değişikliklerini bildirdiği callback sarmalayıcısı.
    /// .NET event olarak kullanılıyor ki servis Dispose edildiğinde abonelik temizlenebilsin.
    /// </summary>
    private sealed class AudioFocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public event Action<AudioFocus>? FocusChanged;

        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            FocusChanged?.Invoke(focusChange);
        }
    }
}
