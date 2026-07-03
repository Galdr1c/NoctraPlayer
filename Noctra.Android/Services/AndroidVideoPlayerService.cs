using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using AndroidX.Media3.Common;
using AndroidX.Media3.Common.Text;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.DataSource;
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
    private readonly ILocalizationService _localizationService;
    private readonly object _trackLock = new();
    private readonly List<(int Id, string? Name)> _audioTracks = new();
    private readonly List<(int Id, string? Name)> _subtitleTracks = new();

    private IExoPlayer? _exoPlayer;
    private PlayerListener? _playerListener;
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
    private bool _requiresPlayerRebuild;

    // Cached values to avoid cross-thread calls when queried outside main thread
    private bool _isPlaying;
    private long _currentTimeMs;
    private double _duration;

    public string? CurrentUrl => _currentUrl;
    public bool IsPlaying => _isPlaying;
    public PlaybackState State => _state;
    public bool HasLoadedMedia => _hasLoadedMedia;

    public long CurrentTimeMilliseconds
    {
        get
        {
            if (_exoPlayer is null) return 0;
            if (Looper.MyLooper() == Looper.MainLooper)
            {
                try
                {
                    _currentTimeMs = _exoPlayer.CurrentPosition;
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to read current position: {ex.Message}");
                }
            }
            return _currentTimeMs;
        }
    }

    public double Duration => _duration;

    public IReadOnlyList<(int Id, string? Name)> AudioTracks
    {
        get
        {
            lock (_trackLock)
            {
                return _audioTracks.ToArray();
            }
        }
    }

    public IReadOnlyList<(int Id, string? Name)> SubtitleTracks
    {
        get
        {
            lock (_trackLock)
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

    private static void LogDebug(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] {message}");
    }

    public AndroidVideoPlayerService(
        AndroidVideoSurfaceService videoSurfaceService,
        Context applicationContext,
        ISettingsService settingsService,
        INetworkService networkService,
        ILocalizationService localizationService)
    {
        _videoSurfaceService = videoSurfaceService;
        _applicationContext = applicationContext.ApplicationContext ?? applicationContext;
        _settingsService = settingsService;
        _networkService = networkService;
        _localizationService = localizationService;

        ApplySettingsSnapshot(_settingsService.Settings, updateAudioState: false);
        _settingsService.SettingsChanged += OnSettingsChanged;
        _videoSurfaceService.SurfaceAvailable += VideoSurfaceService_SurfaceAvailable;
        _videoSurfaceService.SurfaceDestroyed += VideoSurfaceService_SurfaceDestroyed;

        // Initialize ExoPlayer on the Main Thread
        RunOnMainThread(() =>
        {
            InitializePlayer();
        });
    }

    private void InitializePlayer()
    {
        if (_exoPlayer is not null) return;

        var builder = new ExoPlayerBuilder(_applicationContext)
            .SetLoadControl(CreateLoadControl(_lastVideoBufferSize));
        
        // Configure AudioAttributes for automatic audio focus handling
        var audioAttributes = new AndroidX.Media3.Common.AudioAttributes.Builder()
            .SetUsage(C.UsageMedia)
            .SetContentType(C.ContentTypeMovie)
            .Build();
        
        builder.SetAudioAttributes(audioAttributes, true);
        
        _exoPlayer = builder.Build();
        _playerListener = new PlayerListener(this);
        _exoPlayer.AddListener(_playerListener);

        ApplyVolume();
        ApplyPlaybackRate();
        ApplyDataUsageConstraints();
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
            var duration = Duration;
            if (duration <= 0) return 0;
            return Math.Clamp((CurrentTimeMilliseconds / 1000d) / duration, 0, 1);
        }
        set
        {
            var duration = Duration;
            if (duration <= 0) return;
            var targetMs = (long)(Math.Clamp(value, 0, 1) * duration * 1000);
            SeekToTime(targetMs);
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
        RebuildPlayerIfNeeded();
        
        // Ensure player is initialized on Main Thread
        var initTcs = new TaskCompletionSource();
        RunOnMainThread(() =>
        {
            try
            {
                InitializePlayer();
                initTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                initTcs.TrySetException(ex);
            }
        });
        await initTcs.Task;

        _currentUrl = url;
        _selectedAudioTrack = -1;
        _selectedSubtitleTrack = -1;
        ClearTrackCache();
        RaiseSubtitleTextChanged(null);
        
        _state = PlaybackState.Buffering;
        BufferingChanged?.Invoke(this, 0);

        var playbackSource = BuildPlaybackSource(url);
        EnsureNetworkCanPlay(playbackSource);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnMainThread(async () =>
        {
            try
            {
                if (_exoPlayer is null)
                {
                    throw new InvalidOperationException("ExoPlayer is not initialized.");
                }

                _exoPlayer.Stop();
                _exoPlayer.ClearVideoSurface();
                _exoPlayer.ClearMediaItems();

                // Setup DataSource.Factory with headers
                var httpDataSourceFactory = new DefaultHttpDataSource.Factory();
                httpDataSourceFactory.SetUserAgent(ResolveUserAgent());
                if (playbackSource.Headers.Count > 0)
                {
                    httpDataSourceFactory.SetDefaultRequestProperties(playbackSource.Headers);
                }

                global::Android.Net.Uri uri;
                if (playbackSource.IsNetworkStream)
                {
                    uri = global::Android.Net.Uri.Parse(playbackSource.Url);
                }
                else
                {
                    uri = global::Android.Net.Uri.FromFile(new Java.IO.File(playbackSource.Url));
                }

                var mediaItem = MediaItem.FromUri(uri);

                // Create the appropriate MediaSource using DefaultMediaSourceFactory.
                // Media3 optional modules (DASH/SmoothStreaming/HLS/RTSP) live in separate
                // AndroidX packages. If an APK is built without one of those packages,
                // DefaultMediaSourceFactory can throw a Java ClassNotFoundException on
                // the UI thread. Convert that into a normal player error so it never
                // bubbles out as a terminating Avalonia/Android crash report.
                var mediaSourceFactory = new DefaultMediaSourceFactory(httpDataSourceFactory);
                try
                {
                    var mediaSource = mediaSourceFactory.CreateMediaSource(mediaItem);
                    _exoPlayer.SetMediaSource(mediaSource);
                }
                catch (Exception ex) when (IsMissingMedia3SourceModuleException(ex))
                {
                    throw new InvalidOperationException(
                        _localizationService.GetString("Player.Error.UnsupportedStreamFormat"),
                        ex);
                }
                ApplyDataUsageConstraints();

                // Setup Video Surface
                await _videoSurfaceService.ShowAsync().ConfigureAwait(true);
                var surface = await _videoSurfaceService.WaitForSurfaceAsync().ConfigureAwait(true);
                if (surface is not null)
                {
                    _exoPlayer.SetVideoSurface(surface);
                }
                
                if (startTimeSeconds > 0)
                {
                    _exoPlayer.SeekTo((long)(startTimeSeconds * 1000));
                }

                _exoPlayer.Prepare();
                _exoPlayer.PlayWhenReady = true;
                ApplyPlaybackRate();

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _state = PlaybackState.Error;
                RaiseSubtitleTextChanged(null);
                ErrorOccurred?.Invoke(this, ex.Message);
                completion.TrySetException(ex);
            }
        });

        await completion.Task;
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
        RebuildPlayerIfNeeded();

        await PlayAsync(url, currentPositionSeconds).ConfigureAwait(false);

        if (!wasPlaying)
        {
            Pause();
        }
    }

    public void Pause()
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is not null)
            {
                _exoPlayer.PlayWhenReady = false;
            }
        });
    }

    public void Resume()
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is not null)
            {
                _exoPlayer.PlayWhenReady = true;
                ApplyPlaybackRate();
            }
        });
    }

    public void Stop()
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null)
            {
                _state = PlaybackState.Stopped;
                _hasLoadedMedia = false;
                ClearTrackCache();
                RaiseSubtitleTextChanged(null);
                return;
            }

            try
            {
                _exoPlayer.Stop();
                _exoPlayer.ClearVideoSurface();
                _exoPlayer.ClearMediaItems();
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to stop player cleanly: {ex.Message}");
            }
            finally
            {
                _hasLoadedMedia = false;
                _selectedAudioTrack = -1;
                _selectedSubtitleTrack = -1;
                ClearTrackCache();
                RaiseSubtitleTextChanged(null);
                _state = PlaybackState.Stopped;
                _isPlaying = false;
                PlayingChanged?.Invoke(this, false);
            }
        });
    }

    public void SeekToTime(long milliseconds)
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null) return;
            _exoPlayer.SeekTo(Math.Max(0, milliseconds));
            PositionChanged?.Invoke(this, milliseconds / 1000d);
        });
    }

    public void PlayLoadedMedia() => Resume();

    public void SetAudioTrack(int trackId)
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || !_hasLoadedMedia || trackId < 0) return;

            int groupIndex = trackId / 1000;
            int trackIndex = trackId % 1000;

            var currentTracks = _exoPlayer.CurrentTracks;
            var groups = GetTrackGroups(currentTracks);
            if (groupIndex >= groups.Length) return;

            var group = groups[groupIndex];
            var mediaTrackGroup = group.MediaTrackGroup;

            var newOverride = new TrackSelectionOverride(mediaTrackGroup, trackIndex);

            _exoPlayer.TrackSelectionParameters = _exoPlayer.TrackSelectionParameters.BuildUpon()
                .ClearOverridesOfType(C.TrackTypeAudio)
                .AddOverride(newOverride)
                .Build();

            _selectedAudioTrack = trackId;
        });
    }

    public void SetSubtitleTrack(int trackId)
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || !_hasLoadedMedia) return;

            if (trackId < 0)
            {
                _exoPlayer.TrackSelectionParameters = _exoPlayer.TrackSelectionParameters.BuildUpon()
                    .SetTrackTypeDisabled(C.TrackTypeText, true)
                    .Build();
                _selectedSubtitleTrack = -1;
                RaiseSubtitleTextChanged(null);
                return;
            }

            int groupIndex = trackId / 1000;
            int trackIndex = trackId % 1000;

            var currentTracks = _exoPlayer.CurrentTracks;
            var groups = GetTrackGroups(currentTracks);
            if (groupIndex >= groups.Length) return;

            var group = groups[groupIndex];
            var mediaTrackGroup = group.MediaTrackGroup;

            var newOverride = new TrackSelectionOverride(mediaTrackGroup, trackIndex);

            _exoPlayer.TrackSelectionParameters = _exoPlayer.TrackSelectionParameters.BuildUpon()
                .SetTrackTypeDisabled(C.TrackTypeText, false)
                .ClearOverridesOfType(C.TrackTypeText)
                .AddOverride(newOverride)
                .Build();

            _selectedSubtitleTrack = trackId;
        });
    }

    public void SetVideoLayout(string? aspectRatio, string? cropGeometry)
    {
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
            _videoSurfaceService.SurfaceAvailable -= VideoSurfaceService_SurfaceAvailable;
            _videoSurfaceService.SurfaceDestroyed -= VideoSurfaceService_SurfaceDestroyed;
            _reinitializeCts?.Cancel();
            _reinitializeCts?.Dispose();
            
            RunOnMainThread(() =>
            {
                ReleasePlayer();
            });

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

        if (previousBufferSize != _lastVideoBufferSize)
        {
            _requiresPlayerRebuild = true;
        }

        if (previousDataUsage != _lastDataUsage)
        {
            ApplyDataUsageConstraints();
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
        catch (Exception ex)
        {
            LogDebug($"Failed to replace reinitialize token: {ex.Message}");
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
            catch (System.OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                LogDebug($"Reinitialize after settings change failed: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }, cts.Token);
    }

    private void VideoSurfaceService_SurfaceAvailable(object? sender, Surface surface)
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || _isDisposed)
            {
                return;
            }

            try
            {
                _exoPlayer.SetVideoSurface(surface);
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to attach recreated surface: {ex.Message}");
            }
        });
    }

    private void VideoSurfaceService_SurfaceDestroyed(object? sender, EventArgs e)
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || _isDisposed)
            {
                return;
            }

            try
            {
                _exoPlayer.ClearVideoSurface();
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to clear destroyed surface: {ex.Message}");
            }
        });
    }

    private void RebuildPlayerIfNeeded()
    {
        if (!_requiresPlayerRebuild)
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnMainThread(() =>
        {
            try
            {
                ReleasePlayer();
                _requiresPlayerRebuild = false;
                InitializePlayer();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        completion.Task.GetAwaiter().GetResult();
    }

    private void ReleasePlayer()
    {
        if (_exoPlayer is null)
        {
            return;
        }

        try
        {
            if (_playerListener is not null)
            {
                _exoPlayer.RemoveListener(_playerListener);
                _playerListener.Dispose();
                _playerListener = null;
            }

            _exoPlayer.ClearVideoSurface();
            _exoPlayer.Release();
            _exoPlayer.Dispose();
        }
        finally
        {
            _exoPlayer = null;
        }
    }

    private static DefaultLoadControl CreateLoadControl(BufferSize bufferSize)
    {
        var (minBufferMs, maxBufferMs, playbackMs, rebufferMs) = bufferSize switch
        {
            BufferSize.Small => (2_000, 8_000, 750, 1_500),
            BufferSize.Large => (15_000, 60_000, 1_500, 5_000),
            _ => (5_000, 30_000, 1_000, 2_500)
        };

        return new DefaultLoadControl.Builder()
            .SetBufferDurationsMs(minBufferMs, maxBufferMs, playbackMs, rebufferMs)
            .Build();
    }

    private void ApplyDataUsageConstraints()
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || _isDisposed)
            {
                return;
            }

            var (maxWidth, maxHeight, maxBitrate, forceLowestBitrate) = GetDataUsageConstraints(_lastDataUsage);

            try
            {
                _exoPlayer.TrackSelectionParameters = _exoPlayer.TrackSelectionParameters.BuildUpon()
                    .SetMaxVideoSize(maxWidth, maxHeight)
                    .SetMaxVideoBitrate(maxBitrate)
                    .SetForceLowestBitrate(forceLowestBitrate)
                    .Build();
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to apply data usage constraints: {ex.Message}");
            }
        });
    }

    private static (int MaxWidth, int MaxHeight, int MaxBitrate, bool ForceLowestBitrate) GetDataUsageConstraints(DataUsageLevel dataUsage)
    {
        return dataUsage switch
        {
            DataUsageLevel.Low => (854, 480, 1_200_000, true),
            DataUsageLevel.Medium => (1280, 720, 3_000_000, false),
            DataUsageLevel.High => (1920, 1080, 6_000_000, false),
            _ => (int.MaxValue, int.MaxValue, int.MaxValue, false)
        };
    }

    private static double NormalizeDurationSeconds(long durationMs)
    {
        return durationMs == C.TimeUnset || durationMs <= 0
            ? 0d
            : durationMs / 1000d;
    }

    private void EnsureNetworkCanPlay(PlaybackSource playbackSource)
    {
        if (!playbackSource.IsNetworkStream) return;

        // Android connectivity APIs can report Offline/Unknown incorrectly on some
        // vendor builds even when the stream is reachable. Treat this as a warning
        // and let ExoPlayer attempt the request. If the network is truly unavailable,
        // ExoPlayer will emit a normal playback error that the UI can display without
        // terminating the app. Local files are bypassed above.
        if (string.Equals(_networkService.CurrentNetworkStatus, "Offline", StringComparison.OrdinalIgnoreCase))
        {
            LogDebug("Network service reported Offline before playback; attempting stream anyway.");
            ErrorOccurred?.Invoke(this, _localizationService.GetString("Player.Warning.NetworkOfflineTrying"));
        }
    }

    private static bool IsMissingMedia3SourceModuleException(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("ClassNotFoundException", StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("DashMediaSource", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("SsMediaSource", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("HlsMediaSource", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("RtspMediaSource", StringComparison.OrdinalIgnoreCase));
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
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null) return;
            var level = _isMuted ? 0f : _volume / 100f;
            _exoPlayer.Volume = level;
        });
    }

    private void ApplyPlaybackRate()
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null) return;
            try
            {
                var playbackParameters = new PlaybackParameters(_playbackRate);
                _exoPlayer.PlaybackParameters = playbackParameters;
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to apply playback rate: {ex.Message}");
            }
        });
    }

    private void ClearTrackCache()
    {
        lock (_trackLock)
        {
            _audioTracks.Clear();
            _subtitleTracks.Clear();
        }
    }

    private void RaiseSubtitleTextChanged(string? text, long clearAfterMilliseconds = 0)
    {
        var normalizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        var sequence = Interlocked.Increment(ref _subtitleSequence);
        SubtitleTextChanged?.Invoke(this, normalizedText);

        if (normalizedText.Length == 0 || clearAfterMilliseconds <= 0) return;

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
            catch (Exception ex)
            {
                LogDebug($"Failed to clear subtitle text after cue timeout: {ex.Message}");
            }
        });
    }

    private void UpdateStreamQuality()
    {
        if (_exoPlayer is null) return;

        var videoFormat = _exoPlayer.VideoFormat;
        var audioFormat = _exoPlayer.AudioFormat;

        var quality = new StreamQualityInfo();

        if (videoFormat is not null)
        {
            quality.Width = videoFormat.Width;
            quality.Height = videoFormat.Height;
            quality.VideoCodec = videoFormat.SampleMimeType is { } mime ? MimeToCodecName(mime) : null;
            if (videoFormat.Bitrate > 0)
            {
                quality.VideoBitrate = videoFormat.Bitrate / 1000;
            }
            if (videoFormat.FrameRate > 0)
            {
                quality.Fps = (int)videoFormat.FrameRate;
            }
        }

        if (audioFormat is not null)
        {
            quality.AudioCodec = audioFormat.SampleMimeType is { } mime ? MimeToCodecName(mime) : null;
            if (audioFormat.Bitrate > 0)
            {
                quality.AudioBitrate = audioFormat.Bitrate / 1000;
            }
            quality.AudioChannels = audioFormat.ChannelCount;
        }

        StreamQuality = quality;

        if (quality.Width > 0 && quality.Height > 0)
        {
            _videoSurfaceService.SetVideoSize(quality.Width, quality.Height);
        }

        QualityDetected?.Invoke(this, quality);
    }

    private static string MimeToCodecName(string mime)
    {
        return mime.ToLowerInvariant() switch
        {
            "video/avc" => "H.264",
            "video/hevc" or "video/h265" => "H.265",
            "video/mp4v-es" or "video/mpeg4" => "MPEG-4",
            "video/3gpp" => "H.263",
            "video/vp8" => "VP8",
            "video/vp9" => "VP9",
            "video/av01" => "AV1",
            "video/mpeg2" => "MPEG-2",
            "audio/mp4a-latm" or "audio/mpeg" => "AAC",
            "audio/opus" => "Opus",
            "audio/vorbis" => "Vorbis",
            "audio/flac" => "FLAC",
            "audio/ac3" => "AC3",
            "audio/eac3" => "E-AC3",
            "audio/mp3" => "MP3",
            "audio/aac" => "AAC",
            "text/vtt" => "WebVTT",
            "application/x-subrip" => "SRT",
            "application/cea-608" => "CEA-608",
            "application/cea-708" => "CEA-708",
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

    private void RunOnMainThread(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
        }
        else
        {
            new Handler(Looper.MainLooper!).Post(action);
        }
    }

    // ── JNI Helpers ──────────────────────────────────────────────────────────
    // Xamarin.AndroidX.Media3 v1.4.x bindings do not expose Tracks.getGroups()
    // or CueGroup.cues as C# properties, so we call the Java methods via JNI.

    private static Tracks.Group[] GetTrackGroups(Tracks tracks)
    {
        try
        {
            var classPtr = JNIEnv.FindClass("androidx/media3/common/Tracks");
            var methodId = JNIEnv.GetMethodID(classPtr, "getGroups", "()Lcom/google/common/collect/ImmutableList;");
            JNIEnv.DeleteLocalRef(classPtr);

            var listPtr = JNIEnv.CallObjectMethod(tracks.Handle, methodId);
            if (listPtr == IntPtr.Zero) return [];

            try
            {
                var listClassPtr = JNIEnv.FindClass("java/util/List");
                var sizeId = JNIEnv.GetMethodID(listClassPtr, "size", "()I");
                var getId  = JNIEnv.GetMethodID(listClassPtr, "get", "(I)Ljava/lang/Object;");
                JNIEnv.DeleteLocalRef(listClassPtr);

                int count = JNIEnv.CallIntMethod(listPtr, sizeId);
                var result = new Tracks.Group[count];
                for (int i = 0; i < count; i++)
                {
                    var itemPtr = JNIEnv.CallObjectMethod(listPtr, getId, new JValue(i));
                    result[i] = Java.Lang.Object.GetObject<Tracks.Group>(itemPtr, JniHandleOwnership.TransferLocalRef)!;
                }
                return result;
            }
            finally
            {
                JNIEnv.DeleteLocalRef(listPtr);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] GetTrackGroups JNI error: {ex.Message}");
            return [];
        }
    }

    private static Cue[] GetCues(CueGroup cueGroup)
    {
        try
        {
            var classPtr = JNIEnv.FindClass("androidx/media3/common/text/CueGroup");
            var fieldId  = JNIEnv.GetFieldID(classPtr, "cues", "Lcom/google/common/collect/ImmutableList;");
            JNIEnv.DeleteLocalRef(classPtr);

            var listPtr = JNIEnv.GetObjectField(cueGroup.Handle, fieldId);
            if (listPtr == IntPtr.Zero) return [];

            try
            {
                var listClassPtr = JNIEnv.FindClass("java/util/List");
                var sizeId = JNIEnv.GetMethodID(listClassPtr, "size", "()I");
                var getId  = JNIEnv.GetMethodID(listClassPtr, "get", "(I)Ljava/lang/Object;");
                JNIEnv.DeleteLocalRef(listClassPtr);

                int count = JNIEnv.CallIntMethod(listPtr, sizeId);
                var result = new Cue[count];
                for (int i = 0; i < count; i++)
                {
                    var itemPtr = JNIEnv.CallObjectMethod(listPtr, getId, new JValue(i));
                    result[i] = Java.Lang.Object.GetObject<Cue>(itemPtr, JniHandleOwnership.TransferLocalRef)!;
                }
                return result;
            }
            finally
            {
                JNIEnv.DeleteLocalRef(listPtr);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] GetCues JNI error: {ex.Message}");
            return [];
        }
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

    // ── ExoPlayer Listener ───────────────────────────────────────────────────
    private sealed class PlayerListener : Java.Lang.Object, IPlayerListener
    {
        private readonly AndroidVideoPlayerService _service;

        public PlayerListener(AndroidVideoPlayerService service)
        {
            _service = service;
        }

        public void OnPlaybackStateChanged(int playbackState)
        {
            switch (playbackState)
            {
                case BasePlayer.InterfaceConsts.StateIdle:
                    _service._state = PlaybackState.Stopped;
                    _service._hasLoadedMedia = false;
                    _service._isPlaying = false;
                    _service.PlayingChanged?.Invoke(_service, false);
                    break;
                case BasePlayer.InterfaceConsts.StateBuffering:
                    _service._state = PlaybackState.Buffering;
                    _service.BufferingChanged?.Invoke(_service, 0);
                    break;
                case BasePlayer.InterfaceConsts.StateReady:
                    _service._state = _service._exoPlayer?.PlayWhenReady == true ? PlaybackState.Playing : PlaybackState.Paused;
                    _service._hasLoadedMedia = true;
                    _service._isPlaying = _service._exoPlayer?.PlayWhenReady == true;
                    try
                    {
                        if (_service._exoPlayer is not null)
                        {
                            _service._duration = NormalizeDurationSeconds(_service._exoPlayer.Duration);
                            _service._currentTimeMs = _service._exoPlayer.CurrentPosition;
                        }
                    }
                    catch (Exception ex)
                    {
                        AndroidVideoPlayerService.LogDebug($"Failed to read player position on ready: {ex.Message}");
                    }
                    _service.PlayerReady?.Invoke(_service, EventArgs.Empty);
                    _service.PlayingChanged?.Invoke(_service, _service._isPlaying);
                    _service.UpdateStreamQuality();
                    break;
                case BasePlayer.InterfaceConsts.StateEnded:
                    _service._state = PlaybackState.Stopped;
                    _service._isPlaying = false;
                    _service.PlayingChanged?.Invoke(_service, false);
                    _service.PlaybackEnded?.Invoke(_service, EventArgs.Empty);
                    break;
            }
        }

        public void OnIsPlayingChanged(bool isPlaying)
        {
            _service._isPlaying = isPlaying;
            if (_service._state != PlaybackState.Buffering)
            {
                _service._state = isPlaying ? PlaybackState.Playing : PlaybackState.Paused;
            }
            _service.PlayingChanged?.Invoke(_service, isPlaying);
        }

        public void OnPlayerError(PlaybackException error)
        {
            _service._state = PlaybackState.Error;
            _service._isPlaying = false;
            _service.ErrorOccurred?.Invoke(_service, error.Message ?? "ExoPlayer error");
        }

        public void OnCues(CueGroup cueGroup)
        {
            var sb = new StringBuilder();
            var cues = AndroidVideoPlayerService.GetCues(cueGroup);
            for (int i = 0; i < cues.Length; i++)
            {
                var cue = cues[i];
                if (cue.Text is { } txt)
                {
                    sb.AppendLine(txt.ToString());
                }
            }
            _service.RaiseSubtitleTextChanged(sb.ToString().Trim());
        }

        public void OnTracksChanged(Tracks tracks)
        {
            var audio = new List<(int Id, string? Name)>();
            var subtitles = new List<(int Id, string? Name)>();
            var audioOrdinal = 1;
            var subtitleOrdinal = 1;

            var groups = AndroidVideoPlayerService.GetTrackGroups(tracks);
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                var groupType = group.Type;

                for (int j = 0; j < group.Length; j++)
                {
                    var format = group.GetTrackFormat(j);
                    var lang = string.IsNullOrWhiteSpace(format.Language) || format.Language == "und"
                        ? ""
                        : $" ({format.Language})";
                    
                    var mime = format.SampleMimeType ?? "";
                    var codec = string.IsNullOrEmpty(mime) ? "" : $" • {MimeToCodecName(mime)}";

                    // Unique track ID encoding groupIndex and trackIndex
                    int trackId = i * 1000 + j;

                    if (groupType == C.TrackTypeAudio)
                    {
                        audio.Add((trackId, $"Audio {audioOrdinal++}{lang}{codec}"));
                    }
                    else if (groupType == C.TrackTypeText)
                    {
                        subtitles.Add((trackId, $"Subtitle {subtitleOrdinal++}{lang}{codec}"));
                    }
                }
            }

            lock (_service._trackLock)
            {
                _service._audioTracks.Clear();
                _service._audioTracks.AddRange(audio);
                _service._subtitleTracks.Clear();
                _service._subtitleTracks.AddRange(subtitles);
            }
        }

        public void OnVideoSizeChanged(AndroidX.Media3.Common.VideoSize videoSize)
        {
            if (videoSize.Width > 0 && videoSize.Height > 0)
            {
                _service._videoSurfaceService.SetVideoSize(videoSize.Width, videoSize.Height);
            }
        }
    }
}
