using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    public void SetVideoLayout(string? aspectRatio, string? cropGeometry) { }

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

        StreamQuality = new StreamQualityInfo
        {
            Width = width,
            Height = height
        };
        QualityDetected?.Invoke(this, StreamQuality);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AndroidVideoPlayerService));
        }
    }
}
