using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    private const int PositionUpdateIntervalMs = 500;

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
    private FrameFpsListener? _fpsListener;
    private string? _currentUrl;
    private PlaybackMediaMetadata _mediaMetadata = new("Noctra");
    private bool _isDisposed;
    private bool _hasLoadedMedia;
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 100;
    private bool _isMuted;
    private float _playbackRate = 1f;
    private int _selectedAudioTrack = -1;
    private int _selectedSubtitleTrack = -1;
    private string _lastUserAgent = string.Empty;
    private BufferSize _lastVideoBufferSize = BufferSize.Normal;
    private bool _lastHardwareAcceleration = true;
    private DataUsageLevel _lastDataUsage = DataUsageLevel.Auto;
    private CancellationTokenSource? _reinitializeCts;
    private bool _requiresPlayerRebuild;
    private readonly Timer _positionUpdateTimer;
    private int _positionUpdateQueued;
    private int _playbackGeneration;
    private readonly AudioBecomingNoisyReceiver _audioBecomingNoisyReceiver;
    private bool _isAudioBecomingNoisyReceiverRegistered;
    private static readonly Handler MainHandler = new(Looper.MainLooper!);

    // Cached values to avoid cross-thread calls when queried outside main thread
    private bool _isPlaying;
    private long _currentTimeMs;
    private double _duration;
    private double _bufferedPosition;

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
    public double BufferedPosition => _bufferedPosition;

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
    // Android's ExoPlayer is released internally; no external release hook
    // is emitted by this implementation.
    public event EventHandler? MediaPlayerReleasing
    {
        add { }
        remove { }
    }
    public event EventHandler? PlaybackEnded;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<IReadOnlyList<SubtitleCueData>>? SubtitleCuesChanged;
    public event EventHandler<StreamQualityInfo>? QualityDetected;
    internal event Action<IExoPlayer>? PlayerChanged;

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
        _audioBecomingNoisyReceiver = new AudioBecomingNoisyReceiver(this);
        _positionUpdateTimer = new Timer(
            _ => QueuePositionUpdate(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        ApplySettingsSnapshot(_settingsService.Settings, updateAudioState: false);
        _settingsService.SettingsChanged += OnSettingsChanged;
        _videoSurfaceService.SurfaceAvailable += VideoSurfaceService_SurfaceAvailable;
        _videoSurfaceService.SurfaceDestroyed += VideoSurfaceService_SurfaceDestroyed;
    }

    internal IExoPlayer AttachPlaybackHost()
    {
        if (Looper.MyLooper() != Looper.MainLooper)
        {
            throw new InvalidOperationException("Playback host must attach on the Android main thread.");
        }

        InitializePlayer();
        return _exoPlayer
            ?? throw new InvalidOperationException("ExoPlayer could not be initialized.");
    }

    internal void DetachPlaybackHost()
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            ReleasePlayer();
            return;
        }

        RunOnMainThread(ReleasePlayer);
    }

    private void InitializePlayer()
    {
        if (_exoPlayer is not null) return;

        // Enable extension renderers (e.g. the bundled FFmpeg audio extension)
        // so audio codecs without a platform MediaCodec decoder (such as MP2
        // on some devices) can still be decoded in software. Extension renderers
        // are only instantiated when a platform decoder cannot handle the format.
        var renderersFactoryBuilder = new DefaultRenderersFactory(_applicationContext);
        var renderersFactory = renderersFactoryBuilder
            .SetExtensionRendererMode(DefaultRenderersFactory.ExtensionRendererModeOn)
            ?? throw new InvalidOperationException("ExoPlayer renderer factory could not be configured.");

        var playerBuilder = new ExoPlayerBuilder(_applicationContext, renderersFactory);
        var builder = playerBuilder
            .SetLoadControl(CreateLoadControl(_lastVideoBufferSize))
            ?? throw new InvalidOperationException("ExoPlayer builder could not be configured.");
        
        // Configure AudioAttributes for automatic audio focus handling
        var audioAttributesBuilder = new AndroidX.Media3.Common.AudioAttributes.Builder();
        var audioUsageBuilder = audioAttributesBuilder.SetUsage(C.UsageMedia);
        if (audioUsageBuilder is null)
        {
            throw new InvalidOperationException("Audio usage could not be configured.");
        }

        var audioContentBuilder = audioUsageBuilder
#pragma warning disable CS0618 // Media3 1.4 binding exposes movie content type under this legacy name.
            .SetContentType(C.ContentTypeMovie)
#pragma warning restore CS0618
            ?? throw new InvalidOperationException("Audio content type could not be configured.");
        var audioAttributes = audioContentBuilder.Build()
            ?? throw new InvalidOperationException("Audio attributes could not be built.");
        
        var configuredPlayerBuilder = builder.SetAudioAttributes(audioAttributes, true);
        if (configuredPlayerBuilder is null)
        {
            throw new InvalidOperationException("Audio attributes could not be applied to ExoPlayer.");
        }
        
        _exoPlayer = configuredPlayerBuilder.Build();
        var player = _exoPlayer ?? throw new InvalidOperationException("ExoPlayer could not be built.");
        _fpsListener = new FrameFpsListener(this);
        player.SetVideoFrameMetadataListener(_fpsListener);
        _playerListener = new PlayerListener(this);
        player.AddListener(_playerListener);
        PlayerChanged?.Invoke(player);

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
        get => CurrentTimeMilliseconds / 1000d;
        set
        {
            var seconds = Duration > 0
                ? Math.Clamp(value, 0, Duration)
                : Math.Max(0, value);

            SeekToTime((long)(seconds * 1000d));
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

    public void UpdateMediaMetadata(PlaybackMediaMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _mediaMetadata = metadata;
    }

    public async Task PlayAsync(string url, double startTimeSeconds = 0)
    {
        ThrowIfDisposed();
        var playbackGeneration = Interlocked.Increment(ref _playbackGeneration);

        await NoctraPlaybackService.EnsureStartedAsync(_applicationContext).ConfigureAwait(false);
        if (!IsPlaybackGenerationCurrent(playbackGeneration))
        {
            return;
        }

        RebuildPlayerIfNeeded();

        _currentUrl = url;
        _bufferedPosition = 0;
        _selectedAudioTrack = -1;
        _selectedSubtitleTrack = -1;
        ClearTrackCache();
        ClearCues();
        
        _state = PlaybackState.Buffering;
        BufferingChanged?.Invoke(this, 0);

        var playbackSource = BuildPlaybackSource(url);
        EnsureNetworkCanPlay(playbackSource);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnMainThread(async () =>
        {
            try
            {
                if (!IsPlaybackGenerationCurrent(playbackGeneration))
                {
                    completion.TrySetCanceled();
                    return;
                }

                if (_exoPlayer is null)
                {
                    throw new InvalidOperationException("ExoPlayer is not initialized.");
                }

                var listenerDetached = false;
                try
                {
                    if (_playerListener is not null)
                    {
                        // Media3 can synchronously dispatch timeline/track callbacks from
                        // Stop/ClearMediaItems. Detach the managed listener so those callbacks
                        // cannot block the Android UI thread while replacing a source.
                        _exoPlayer.RemoveListener(_playerListener);
                        listenerDetached = true;
                    }

                    _exoPlayer.Stop();
                    _exoPlayer.ClearVideoSurface();
                    _exoPlayer.ClearMediaItems();
                }
                finally
                {
                    if (listenerDetached && _playerListener is not null)
                    {
                        _exoPlayer.AddListener(_playerListener);
                    }
                }

                // Setup DataSource.Factory with headers
                var httpDataSourceFactory = new DefaultHttpDataSource.Factory();
                httpDataSourceFactory.SetUserAgent(ResolveUserAgent());
                if (playbackSource.Headers.Count > 0)
                {
                    httpDataSourceFactory.SetDefaultRequestProperties(playbackSource.Headers);
                }

                global::Android.Net.Uri? uri;
                if (playbackSource.IsNetworkStream)
                {
                    uri = global::Android.Net.Uri.Parse(playbackSource.Url);
                }
                else
                {
                    uri = global::Android.Net.Uri.FromFile(new Java.IO.File(playbackSource.Url));
                }

                if (uri is null)
                {
                    throw new InvalidOperationException("Playback URL could not be converted to an Android URI.");
                }

                var metadataBuilder = new MediaMetadata.Builder();
                if (metadataBuilder is null)
                {
                    throw new InvalidOperationException("Media metadata builder could not be created.");
                }

                metadataBuilder.SetTitle(_mediaMetadata.Title);

                var subtitle = _mediaMetadata.Subtitle;
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    metadataBuilder.SetArtist(subtitle);
                }

                var artworkUrl = _mediaMetadata.ArtworkUrl;
                if (!string.IsNullOrWhiteSpace(artworkUrl))
                {
                    var artworkUri = global::Android.Net.Uri.Parse(artworkUrl);
                    if (artworkUri is not null)
                    {
                        metadataBuilder.SetArtworkUri(artworkUri);
                    }
                }

                var mediaItemBuilder = new MediaItem.Builder();
                if (mediaItemBuilder is null)
                {
                    throw new InvalidOperationException("Media item builder could not be created.");
                }

                mediaItemBuilder.SetUri(uri);
                var metadata = metadataBuilder.Build()
                    ?? throw new InvalidOperationException("Media metadata could not be built.");
                mediaItemBuilder.SetMediaMetadata(metadata);
                var mediaItem = mediaItemBuilder.Build()
                    ?? throw new InvalidOperationException("Media item could not be built.");

                // Create the appropriate MediaSource using DefaultMediaSourceFactory.
                // Media3 optional modules (DASH/SmoothStreaming/HLS/RTSP) live in separate
                // AndroidX packages. If an APK is built without one of those packages,
                // DefaultMediaSourceFactory can throw a Java ClassNotFoundException on
                // the UI thread. Convert that into a normal player error so it never
                // bubbles out as a terminating Avalonia/Android crash report.
                //
                // DefaultDataSource.Factory wraps httpDataSourceFactory and automatically
                // routes local file:// and content:// URIs to the appropriate platform
                // data source, while network URIs still go through httpDataSourceFactory.
                // This is required for downloaded media files (/data/.../file.mkv) to play.
                var dataSourceFactory = new DefaultDataSource.Factory(
                    _applicationContext,
                    httpDataSourceFactory);
                var mediaSourceFactory = new DefaultMediaSourceFactory(dataSourceFactory);
                try
                {
                    var mediaSource = mediaSourceFactory.CreateMediaSource(mediaItem);
                    var startPositionMs = (long)(startTimeSeconds * 1000);
                    if (startPositionMs > 0)
                    {
                        _exoPlayer.SetMediaSource(mediaSource, startPositionMs);
                    }
                    else
                    {
                        _exoPlayer.SetMediaSource(mediaSource);
                    }
                    _fpsListener?.Reset();
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
                if (!IsPlaybackGenerationCurrent(playbackGeneration))
                {
                    completion.TrySetCanceled();
                    return;
                }

                var surface = await _videoSurfaceService.WaitForSurfaceAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                if (!IsPlaybackGenerationCurrent(playbackGeneration))
                {
                    completion.TrySetCanceled();
                    return;
                }

                if (surface is null)
                {
                    throw new InvalidOperationException(
                        _localizationService.GetString("Player.Error.SurfaceTimeout"));
                }
                _exoPlayer.SetVideoSurface(surface);

                _exoPlayer.Prepare();
                _exoPlayer.PlayWhenReady = true;
                ApplyPlaybackRate();

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _state = PlaybackState.Error;
                ClearCues();
                LogDebug($"LoadAsync failed: {ex}");
                ErrorOccurred?.Invoke(this, _localizationService.GetString("VideoPlayer.Error.PlaybackStartFailed"));
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
        Interlocked.Increment(ref _playbackGeneration);

        RunOnMainThread(() =>
        {
            StopPositionUpdates();

            if (_exoPlayer is null)
            {
                _state = PlaybackState.Stopped;
                _hasLoadedMedia = false;
                _isPlaying = false;
                _currentTimeMs = 0;
                _duration = 0;
                _bufferedPosition = 0;
                ClearTrackCache();
                ClearCues();
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
                ClearCues();
                _state = PlaybackState.Stopped;
                _isPlaying = false;
                _currentTimeMs = 0;
                _duration = 0;
                _bufferedPosition = 0;
                PlayingChanged?.Invoke(this, false);
            }
        });
    }

    public async Task EndSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _reinitializeCts?.Cancel();
        Interlocked.Increment(ref _playbackGeneration);

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnMainThread(() =>
        {
            try
            {
                StopPositionUpdates();

                if (_exoPlayer is not null)
                {
                    var listenerDetached = false;
                    try
                    {
                        // Media3 dispatches timeline/track callbacks synchronously while
                        // Stop/ClearMediaItems runs.  Detaching our managed listener keeps
                        // that native teardown out of the Android input-dispatch path.
                        if (_playerListener is not null)
                        {
                            _exoPlayer.RemoveListener(_playerListener);
                            listenerDetached = true;
                        }

                        _exoPlayer.PlayWhenReady = false;
                        _exoPlayer.Stop();
                        _exoPlayer.ClearVideoSurface();
                        _exoPlayer.ClearMediaItems();
                    }
                    finally
                    {
                        if (listenerDetached && _playerListener is not null)
                        {
                            _exoPlayer.AddListener(_playerListener);
                        }
                    }
                }

                _currentUrl = null;
                _hasLoadedMedia = false;
                _isPlaying = false;
                _currentTimeMs = 0;
                _duration = 0;
                _bufferedPosition = 0;
                _state = PlaybackState.Stopped;
                StreamQuality = null;

                _selectedAudioTrack = -1;
                _selectedSubtitleTrack = -1;

                ClearTrackCache();
                ClearCues();
                PlayingChanged?.Invoke(this, false);

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

    try
    {
        await completion.Task
            .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
            .ConfigureAwait(false);

        await NoctraPlaybackService
            .StopPlaybackServiceAsync(_applicationContext, cancellationToken)
            .ConfigureAwait(false);
    }
    finally
    {
        _videoSurfaceService.ResetInteractionTransform();
        _videoSurfaceService.Hide();
    }
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || !_hasLoadedMedia || trackId < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetAudioTrack({trackId}): skipped (exoPlayer={_exoPlayer != null}, loaded={_hasLoadedMedia}, trackId={trackId})");
                return;
            }

            int groupIndex = trackId / 1000;
            int trackIndex = trackId % 1000;

            var player = _exoPlayer;
            if (player is null)
            {
                return;
            }

            var currentTracks = player.CurrentTracks;
            if (currentTracks is null)
            {
                return;
            }
            var groups = GetTrackGroups(currentTracks);
            if (groupIndex >= groups.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetAudioTrack({trackId}): skipped (groupIndex {groupIndex} >= {groups.Length})");
                return;
            }

            var group = groups[groupIndex];
            var mediaTrackGroup = group.MediaTrackGroup;

            var newOverride = new TrackSelectionOverride(mediaTrackGroup, trackIndex);

            var trackSelectionParameters = player.TrackSelectionParameters;
            if (trackSelectionParameters is null)
            {
                return;
            }

            var audioSelectionBuilder = trackSelectionParameters.BuildUpon();
            if (audioSelectionBuilder is null)
            {
                return;
            }

            audioSelectionBuilder.ClearOverridesOfType(C.TrackTypeAudio);
            audioSelectionBuilder.AddOverride(newOverride);
            player.TrackSelectionParameters = audioSelectionBuilder.Build()
                ?? throw new InvalidOperationException("Audio track selection parameters could not be built.");

            _selectedAudioTrack = trackId;
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetAudioTrack({trackId}): took {sw.ElapsedMilliseconds}ms");
        });
    }

    public void SetSubtitleTrack(int trackId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || !_hasLoadedMedia)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetSubtitleTrack({trackId}): skipped (exoPlayer={_exoPlayer != null}, loaded={_hasLoadedMedia})");
                return;
            }

            if (trackId < 0)
            {
                var disableTrackSelectionParameters = _exoPlayer.TrackSelectionParameters;
                if (disableTrackSelectionParameters is null)
                {
                    return;
                }

                var disableSelectionBuilder = disableTrackSelectionParameters.BuildUpon();
                if (disableSelectionBuilder is null)
                {
                    return;
                }

                disableSelectionBuilder.SetTrackTypeDisabled(C.TrackTypeText, true);
                _exoPlayer.TrackSelectionParameters = disableSelectionBuilder.Build()
                    ?? throw new InvalidOperationException("Subtitle track selection parameters could not be built.");
                _selectedSubtitleTrack = -1;
                ClearCues();
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetSubtitleTrack({trackId}): took {sw.ElapsedMilliseconds}ms (disabled)");
                return;
            }

            int groupIndex = trackId / 1000;
            int trackIndex = trackId % 1000;

            var player = _exoPlayer;
            if (player is null)
            {
                return;
            }

            var currentTracks = player.CurrentTracks;
            if (currentTracks is null)
            {
                return;
            }
            var groups = GetTrackGroups(currentTracks);
            if (groupIndex >= groups.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetSubtitleTrack({trackId}): skipped (groupIndex {groupIndex} >= {groups.Length})");
                return;
            }

            var group = groups[groupIndex];
            var mediaTrackGroup = group.MediaTrackGroup;

            var newOverride = new TrackSelectionOverride(mediaTrackGroup, trackIndex);

            var trackSelectionParameters = player.TrackSelectionParameters;
            if (trackSelectionParameters is null)
            {
                return;
            }

            var subtitleSelectionBuilder = trackSelectionParameters.BuildUpon();
            if (subtitleSelectionBuilder is null)
            {
                return;
            }

            subtitleSelectionBuilder.SetTrackTypeDisabled(C.TrackTypeText, false);
            subtitleSelectionBuilder.ClearOverridesOfType(C.TrackTypeText);
            subtitleSelectionBuilder.AddOverride(newOverride);
            player.TrackSelectionParameters = subtitleSelectionBuilder.Build()
                ?? throw new InvalidOperationException("Subtitle track selection parameters could not be built.");

            _selectedSubtitleTrack = trackId;
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] SetSubtitleTrack({trackId}): took {sw.ElapsedMilliseconds}ms");
        });
    }

    public void SetVideoLayout(Noctra.Models.VideoScaleMode scaleMode)
    {
        _videoSurfaceService.SetVideoLayout(scaleMode);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Interlocked.Increment(ref _playbackGeneration);
            StopPositionUpdates();
            _positionUpdateTimer.Dispose();
            _settingsService.SettingsChanged -= OnSettingsChanged;
            _videoSurfaceService.SurfaceAvailable -= VideoSurfaceService_SurfaceAvailable;
            _videoSurfaceService.SurfaceDestroyed -= VideoSurfaceService_SurfaceDestroyed;
            _reinitializeCts?.Cancel();
            _reinitializeCts?.Dispose();
            
            RunOnMainThread(() =>
            {
                ReleasePlayer();
            });
        }

        base.Dispose(disposing);
    }

    private bool IsPlaybackGenerationCurrent(int playbackGeneration)
        => !_isDisposed &&
           playbackGeneration == Volatile.Read(ref _playbackGeneration);

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
                LogDebug($"Reinitialize after settings change failed: {ex}");
                ErrorOccurred?.Invoke(this, _localizationService.GetString("VideoPlayer.Error.PlaybackGeneric"));
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
        UpdateAudioBecomingNoisyReceiver(false);
        StopPositionUpdates();

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

            _exoPlayer.SetVideoFrameMetadataListener(null);
            _fpsListener?.Dispose();
            _fpsListener = null;

            _exoPlayer.ClearVideoSurface();
            _exoPlayer.Release();
            _exoPlayer.Dispose();
        }
        finally
        {
            _exoPlayer = null;
        }
    }

    private void StartPositionUpdates()
    {
        if (_isDisposed)
        {
            return;
        }

        _positionUpdateTimer.Change(0, PositionUpdateIntervalMs);
    }

    private void StopPositionUpdates()
    {
        try
        {
            _positionUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // A late player callback can race with service disposal.
        }
    }

    private void UpdatePositionPollingForLoadedMedia()
    {
        if (_hasLoadedMedia && _isPlaying && _exoPlayer is not null)
        {
            StartPositionUpdates();
        }
        else
        {
            StopPositionUpdates();
        }
    }

    private void QueuePositionUpdate()
    {
        if (_isDisposed || !_hasLoadedMedia || _exoPlayer is null ||
            Interlocked.Exchange(ref _positionUpdateQueued, 1) != 0)
        {
            return;
        }

        RunOnMainThread(() =>
        {
            try
            {
                PublishPlaybackPosition();
            }
            finally
            {
                Volatile.Write(ref _positionUpdateQueued, 0);
            }
        });
    }

    private void PublishPlaybackPosition()
    {
        if (_isDisposed || !_hasLoadedMedia || _exoPlayer is null)
        {
            return;
        }

        try
        {
            _currentTimeMs = Math.Max(0, _exoPlayer.CurrentPosition);
            var durationMs = _exoPlayer.Duration;
            _duration = NormalizeDurationSeconds(durationMs);

            var bufferedMs = Math.Max(_currentTimeMs, _exoPlayer.BufferedPosition);
            if (durationMs != C.TimeUnset && durationMs > 0)
            {
                bufferedMs = Math.Min(bufferedMs, durationMs);
            }

            _bufferedPosition = Math.Max(0, bufferedMs) / 1000d;
            PositionChanged?.Invoke(this, _currentTimeMs / 1000d);
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to publish playback position: {ex.Message}");
        }
    }

    private static DefaultLoadControl CreateLoadControl(BufferSize bufferSize)
    {
        var (minBufferMs, maxBufferMs, playbackMs, rebufferMs) = bufferSize switch
        {
            BufferSize.Small => (6_000, 24_000, 750, 1_500),
            BufferSize.Large => (30_000, 180_000, 1_500, 5_000),
            _ => (15_000, 90_000, 1_000, 2_500)
        };

        var builder = new DefaultLoadControl.Builder();
        var configuredBuilder = builder.SetBufferDurationsMs(
            minBufferMs,
            maxBufferMs,
            playbackMs,
            rebufferMs);
        if (configuredBuilder is null)
        {
            throw new InvalidOperationException("ExoPlayer buffer durations could not be configured.");
        }

        configuredBuilder.SetPrioritizeTimeOverSizeThresholds(true);
        return configuredBuilder.Build()
            ?? throw new InvalidOperationException("ExoPlayer load control could not be built.");
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
                var player = _exoPlayer;
                if (player is null)
                {
                    return;
                }

                var trackSelectionParameters = player.TrackSelectionParameters;
                if (trackSelectionParameters is null)
                {
                    return;
                }

                var selectionBuilder = trackSelectionParameters.BuildUpon();
                if (selectionBuilder is null)
                {
                    return;
                }

                selectionBuilder.SetMaxVideoSize(maxWidth, maxHeight);
                selectionBuilder.SetMaxVideoBitrate(maxBitrate);
                selectionBuilder.SetForceLowestBitrate(forceLowestBitrate);
                player.TrackSelectionParameters = selectionBuilder.Build()
                    ?? throw new InvalidOperationException("Data usage track selection parameters could not be built.");
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
        // RTMP excluded: Media3/ExoPlayer has no RTMP extension bundled.
        // RTSP included: ExoPlayer has built-in RTSP support.
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
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

    private void ClearCues()
    {
        SubtitleCuesChanged?.Invoke(this, SubtitleCueData.Empty);
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
            quality.VideoCodec = videoFormat.SampleMimeType is { } mime
                ? MimeToCodecName(mime)
                : string.Empty;
            if (videoFormat.Bitrate > 0)
            {
                quality.VideoBitrate = videoFormat.Bitrate / 1000;
            }
            if (videoFormat.FrameRate > 0)
            {
                quality.Fps = (int)Math.Round(videoFormat.FrameRate);
            }
            else if (_fpsListener?.MeasuredFps > 0)
            {
                quality.Fps = _fpsListener.MeasuredFps;
            }
        }

        if (audioFormat is not null)
        {
            quality.AudioCodec = audioFormat.SampleMimeType is { } mime
                ? MimeToCodecName(mime)
                : string.Empty;
            if (audioFormat.Bitrate > 0)
            {
                quality.AudioBitrate = audioFormat.Bitrate / 1000;
            }
            quality.AudioChannels = audioFormat.ChannelCount;
        }

        StreamQuality = quality;
        QualityDetected?.Invoke(this, quality);
    }

    /// <summary>
    /// Runtime FPS ölçümü güncellenince kalite bildirimini yeniden yayınlar.
    /// </summary>
    private void OnMeasuredFpsChanged(int fps)
    {
        RunOnMainThread(() =>
        {
            if (_exoPlayer is null || _isDisposed)
            {
                return;
            }

            LogDebug($"Measured runtime FPS: {fps}");
            UpdateStreamQuality();
        });
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
            MainHandler.Post(action);
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
            // JNIEnv.FindClass returns a global reference in .NET for Android.
            JNIEnv.DeleteGlobalRef(classPtr);

            var listPtr = JNIEnv.CallObjectMethod(tracks.Handle, methodId);
            if (listPtr == IntPtr.Zero) return [];

            try
            {
                var listClassPtr = JNIEnv.FindClass("java/util/List");
                var sizeId = JNIEnv.GetMethodID(listClassPtr, "size", "()I");
                var getId  = JNIEnv.GetMethodID(listClassPtr, "get", "(I)Ljava/lang/Object;");
                JNIEnv.DeleteGlobalRef(listClassPtr);

                int count = JNIEnv.CallIntMethod(listPtr, sizeId);
                var result = new Tracks.Group[count];
                for (int i = 0; i < count; i++)
                {
                    var itemPtr = JNIEnv.CallObjectMethod(listPtr, getId, new JValue(i));
                    // Local references are thread-local and die with the JNI
                    // frame, but the wrapper outlives this callback and is
                    // finalized on another thread. Promote to a global ref so
                    // ownership can be released safely from any thread.
                    var globalPtr = JNIEnv.NewGlobalRef(itemPtr);
                    JNIEnv.DeleteLocalRef(itemPtr);
                    result[i] = Java.Lang.Object.GetObject<Tracks.Group>(globalPtr, JniHandleOwnership.TransferGlobalRef)!;
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
            // JNIEnv.FindClass returns a global reference in .NET for Android.
            JNIEnv.DeleteGlobalRef(classPtr);

            var listPtr = JNIEnv.GetObjectField(cueGroup.Handle, fieldId);
            if (listPtr == IntPtr.Zero) return [];

            try
            {
                var listClassPtr = JNIEnv.FindClass("java/util/List");
                var sizeId = JNIEnv.GetMethodID(listClassPtr, "size", "()I");
                var getId  = JNIEnv.GetMethodID(listClassPtr, "get", "(I)Ljava/lang/Object;");
                JNIEnv.DeleteGlobalRef(listClassPtr);

                int count = JNIEnv.CallIntMethod(listPtr, sizeId);
                var result = new Cue[count];
                for (int i = 0; i < count; i++)
                {
                    var itemPtr = JNIEnv.CallObjectMethod(listPtr, getId, new JValue(i));
                    // See GetTrackGroups: promote to a global ref so finalization
                    // on another thread never deletes a thread-local reference.
                    var globalPtr = JNIEnv.NewGlobalRef(itemPtr);
                    JNIEnv.DeleteLocalRef(itemPtr);
                    result[i] = Java.Lang.Object.GetObject<Cue>(globalPtr, JniHandleOwnership.TransferGlobalRef)!;
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

    // ── Audio Focus & Output Management ──────────────────────────────────────
    private void UpdateAudioBecomingNoisyReceiver(bool register)
    {
        if (register && !_isAudioBecomingNoisyReceiverRegistered)
        {
            var filter = new IntentFilter(global::Android.Media.AudioManager.ActionAudioBecomingNoisy);
            _applicationContext.RegisterReceiver(_audioBecomingNoisyReceiver, filter);
            _isAudioBecomingNoisyReceiverRegistered = true;
        }
        else if (!register && _isAudioBecomingNoisyReceiverRegistered)
        {
            try
            {
                _applicationContext.UnregisterReceiver(_audioBecomingNoisyReceiver);
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to unregister AudioBecomingNoisyReceiver: {ex.Message}");
            }
            _isAudioBecomingNoisyReceiverRegistered = false;
        }
    }

    private sealed class AudioBecomingNoisyReceiver : BroadcastReceiver
    {
        private readonly AndroidVideoPlayerService _playerService;

        public AudioBecomingNoisyReceiver(AndroidVideoPlayerService playerService)
        {
            _playerService = playerService;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == global::Android.Media.AudioManager.ActionAudioBecomingNoisy)
            {
                _playerService.Pause();
            }
        }
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
                    _service.StopPositionUpdates();
                    _service._state = PlaybackState.Stopped;
                    _service._hasLoadedMedia = false;
                    _service._isPlaying = false;
                    _service.PlayingChanged?.Invoke(_service, false);
                    _service.ClearCues();
                    break;
                case BasePlayer.InterfaceConsts.StateBuffering:
                    _service._state = PlaybackState.Buffering;
                    _service.UpdatePositionPollingForLoadedMedia();
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
                            var durationMs = _service._exoPlayer.Duration;
                            _service._duration = NormalizeDurationSeconds(durationMs);
                            _service._currentTimeMs = Math.Max(0, _service._exoPlayer.CurrentPosition);

                            var bufferedMs = Math.Max(
                                _service._currentTimeMs,
                                _service._exoPlayer.BufferedPosition);

                            if (durationMs != C.TimeUnset && durationMs > 0)
                            {
                                bufferedMs = Math.Min(bufferedMs, durationMs);
                            }

                            _service._bufferedPosition = Math.Max(0, bufferedMs) / 1000d;
                        }
                    }
                    catch (Exception ex)
                    {
                        AndroidVideoPlayerService.LogDebug($"Failed to read player position on ready: {ex.Message}");
                    }
                    _service.BufferingChanged?.Invoke(_service, 100f);
                    _service.PlayerReady?.Invoke(_service, EventArgs.Empty);
                    _service.PlayingChanged?.Invoke(_service, _service._isPlaying);
                    _service.UpdatePositionPollingForLoadedMedia();
                    _service.UpdateStreamQuality();
                    break;
                case BasePlayer.InterfaceConsts.StateEnded:
                    _service.StopPositionUpdates();
                    _service._state = PlaybackState.Stopped;
                    _service._isPlaying = false;
                    _service.PlayingChanged?.Invoke(_service, false);
                    _service.PlaybackEnded?.Invoke(_service, EventArgs.Empty);
                    _service.ClearCues();
                    break;
            }
        }

        public void OnIsPlayingChanged(bool isPlaying)
        {
            _service._isPlaying = isPlaying;
            if (!isPlaying)
            {
                _service._fpsListener?.CompletePartialMeasurement();
            }
            if (_service._state != PlaybackState.Buffering)
            {
                _service._state = isPlaying ? PlaybackState.Playing : PlaybackState.Paused;
            }
            _service.UpdatePositionPollingForLoadedMedia();
            _service.UpdateAudioBecomingNoisyReceiver(isPlaying);
            _service.PlayingChanged?.Invoke(_service, isPlaying);
        }

        public void OnPlayerError(PlaybackException? error)
        {
            _service.StopPositionUpdates();
            _service._state = PlaybackState.Error;
            _service._isPlaying = false;
            _service.PlayingChanged?.Invoke(_service, false);
            AndroidVideoPlayerService.LogDebug($"ExoPlayer playback error: {error?.Message}");
            _service.ErrorOccurred?.Invoke(_service, _service._localizationService.GetString("VideoPlayer.Error.PlaybackGeneric"));
            _service.ClearCues();
        }

        public void OnCues(CueGroup? cueGroup)
        {
            if (cueGroup is null)
            {
                _service.ClearCues();
                return;
            }

            var cues = AndroidVideoPlayerService.GetCues(cueGroup);
            if (cues.Length == 0)
            {
                _service.ClearCues();
                return;
            }

            var list = new List<SubtitleCueData>(cues.Length);

            for (int i = 0; i < cues.Length; i++)
            {
                var rawText = cues[i].Text?.ToString();
                if (string.IsNullOrWhiteSpace(rawText)) continue;

                var normalized = Regex.Replace(
                    rawText.Replace("\r\n", "\n").Replace('\r', '\n'),
                    @"[ \t]+", " ").Trim();

                if (normalized.Length == 0) continue;
                list.Add(new SubtitleCueData(normalized));
            }

            _service.SubtitleCuesChanged?.Invoke(
                _service,
                list.Count == 0 ? SubtitleCueData.Empty : list.ToArray());
        }

        public void OnTracksChanged(Tracks? tracks)
        {
            if (tracks is null)
            {
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
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
                    if (format is null)
                    {
                        continue;
                    }
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
            System.Diagnostics.Debug.WriteLine($"[AndroidVideoPlayerService] OnTracksChanged: took {sw.ElapsedMilliseconds}ms (audio={audio.Count}, subtitles={subtitles.Count})");
        }

        public void OnVideoSizeChanged(AndroidX.Media3.Common.VideoSize? videoSize)
        {
            if (videoSize is not null && videoSize.Width > 0 && videoSize.Height > 0)
            {
                // Anamorphic içerikte piksel oranı 1'den farklıdır; display aspect
                // ratio = (width × ratio) / height olarak hesaplanır.
                _service._videoSurfaceService.SetVideoSize(
                    videoSize.Width,
                    videoSize.Height,
                    videoSize.PixelWidthHeightRatio);
            }
        }
    }

    // ── Runtime FPS Measurement ─────────────────────────────────────────────
    /// <summary>
    /// ExoPlayer metadata'da kare hızı bildirmediğinde (HLS/TS akışlarında yaygın)
    /// gerçek kare hızını ölçmek için her işlenen karede sayaç tutar ve saniyelik
    /// pencere dolunca tahmini FPS'i hesaplar.
    /// </summary>
    private sealed class FrameFpsListener : Java.Lang.Object, AndroidX.Media3.ExoPlayer.Video.IVideoFrameMetadataListener
    {
        private readonly AndroidVideoPlayerService _service;
        private const int RequiredStableWindows = 3;
        private long _frameCount;
        private long _windowStartNs;
        private int _measuredFps;
        private int _lastWindowFps;
        private int _stableWindowCount;
        private bool _isMeasured;

        public int MeasuredFps => _measuredFps;

        public FrameFpsListener(AndroidVideoPlayerService service)
        {
            _service = service;
            Reset();
        }

        public void OnVideoFrameAboutToBeRendered(long presentationTimeUs, long releaseTimeNs, AndroidX.Media3.Common.Format? format, global::Android.Media.MediaFormat? mediaFormat)
        {
            if (_isMeasured)
            {
                return;
            }

            _frameCount++;

            var nowNs = SystemClock.ElapsedRealtimeNanos();
            var elapsedNs = nowNs - _windowStartNs;
            if (elapsedNs < 1_000_000_000L)
            {
                return;
            }

            var fps = (int)Math.Round(_frameCount * 1_000_000_000.0 / elapsedNs);
            _frameCount = 0;
            _windowStartNs = nowNs;

            if (fps <= 0)
            {
                _measuredFps = 0;
                _lastWindowFps = 0;
                _stableWindowCount = 0;
                return;
            }

            if (fps != _lastWindowFps)
            {
                _lastWindowFps = fps;
                _stableWindowCount = 1;
                return;
            }

            _stableWindowCount++;
            if (_stableWindowCount < RequiredStableWindows)
            {
                return;
            }

            _measuredFps = fps;
            _isMeasured = true;
            _service.OnMeasuredFpsChanged(fps);
        }

        /// <summary>
        /// Oynatma durdurulduğunda henüz tam pencere dolmamışsa, o ana kadar
        /// işlenen karelerden kısmi bir tahmin üretir. Video hiç oynamadıysa
        /// (hiç kare işlenmediyse) ölçüm yapılamaz ve mevcut değer korunur.
        /// </summary>
        public void CompletePartialMeasurement()
        {
            if (_isMeasured || _frameCount <= 0)
            {
                return;
            }

            var nowNs = SystemClock.ElapsedRealtimeNanos();
            var elapsedNs = nowNs - _windowStartNs;
            if (elapsedNs < 300_000_000L)
            {
                return;
            }

            var fps = (int)Math.Round(_frameCount * 1_000_000_000.0 / elapsedNs);
            if (fps <= 0)
            {
                return;
            }

            _measuredFps = fps;
            _isMeasured = true;
            _service.OnMeasuredFpsChanged(fps);
        }

        public void Reset()
        {
            _frameCount = 0;
            _windowStartNs = SystemClock.ElapsedRealtimeNanos();
            _measuredFps = 0;
            _lastWindowFps = 0;
            _stableWindowCount = 0;
            _isMeasured = false;
        }
    }
}
