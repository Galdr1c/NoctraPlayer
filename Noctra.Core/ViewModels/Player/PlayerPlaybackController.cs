using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Noctra.Models;

namespace Noctra.ViewModels;

public class PlayerPlaybackController
{
    private readonly PlayerViewModel _vm;

    public PlayerPlaybackController(PlayerViewModel vm)
    {
        _vm = vm;
    }

    public void OnVideoPlayerServicePlayingChanged(object? s, bool playing)
    {
        _vm.DispatcherService.Invoke(() =>
        {
            _vm.IsPlaying = playing;
            if (playing) 
            {
                _vm._isContentTransitioning = false;
                _vm._isPlaybackEnded = false;
                if (_vm.BufferingProgress >= 99f)
                {
                    _vm.IsBuffering = false;
                }
                _vm.UpdateMediaInfo();
                _ = _vm.RefreshTracksWithRetryAsync();
                _vm.RestartAutoHideTimer();
            }
        });
    }

    public void OnVideoPlayerServiceBufferingChanged(object? s, float progress)
    {
        _vm.DispatcherService.Invoke(() =>
        {
            _vm.BufferingProgress = progress;

            // Ignore stale buffering callbacks while switching content.
            if (_vm._isContentTransitioning)
            {
                _vm.IsBuffering = true;
                return;
            }

            // VLC fires negative progress (-1) when it encounters an error or loses connection.
            if (progress < 0f)
            {
                _vm.IsBuffering = true;
                return;
            }

            _vm.IsBuffering = progress < 100f;
        });
    }

    public void OnVideoPlayerServicePositionChanged(object? s, double pos)
    {
        _vm.DispatcherService.BeginInvoke(() =>
        {
            // Bu event çok sık tetiklendiği için lock/resizing anında arayüzü kasmamak adına skip ediyoruz.
            if (_vm.IsDragging || _vm.IsResizing || _vm._isContentTransitioning || _vm._isUserSeeking) return;

            // Live içerik ise VLC Position değişse bile biz hep 0 veya canlı uca kilitli kalalım.
            if (_vm.IsLiveContent)
            {
                _vm.Position = 0;
                _vm.PositionText = "00:00:00";
                _vm.RemainingTime = "00:00:00";
                
                // Canlı akışlarda ara sıra Position gelmesi yayının canlı olduğunu doğrular.
                _vm._lastLivePositionEventAtUtc = DateTime.UtcNow;
                return;
            }

            var nowUtc = DateTime.UtcNow;
            ReleaseSkipSeekCarryIfSettled(nowUtc, pos);

            // Eğer bir seek komutu bekliyorsa, VLC'den gelen eski pozisyonları yoksayalım
            if (_vm._hasPendingSkipSeekTarget && nowUtc <= _vm._pendingSkipSeekExpiresUtc)
            {
                return;
            }

            if (_vm._pendingResumeSeekPosition > 1)
            {
                // Resume seek bekleniyor, eski pozisyonları yoksay
                return;
            }

            _vm.Position = pos;
            _vm.PositionText = TimeSpan.FromSeconds(pos).ToString(@"hh\:mm\:ss");

            _vm.UpdateDurationFromService();
            if (_vm.Duration > 0)
            {
                var remaining = Math.Max(0, _vm.Duration - pos);
                _vm.RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
            }

            if (pos > 1)
            {
                _vm._lastKnownValidPosition = pos;
            }

            _vm.CheckIntroCreditsPosition(pos);
        });
    }

    public void OnVideoPlayerServiceVolumeChanged(object? s, int vol)
    {
        _vm.DispatcherService.Invoke(() =>
        {
            _vm._isUpdatingFromService = true;
            try
            {
                _vm.Volume = vol;
            }
            finally
            {
                _vm._isUpdatingFromService = false;
            }
        });
    }

    public async Task PlayChannelAsync(Channel channel, double? startPosition = null)
    {
        _vm.LogDebug($"PlayChannelAsync: Id={channel.Id}, Name={channel.Name}, Type={channel.Type}, StreamUrl={channel.StreamUrl}, StartPos={startPosition}");

        if (channel.StreamUrl != null && channel.StreamUrl.StartsWith("stalker-series://"))
        {
            throw new InvalidOperationException(_vm.LocalizationService.GetString("Player.Error.SeriesFolder"));
        }

        var requestVersion = Interlocked.Increment(ref _vm._playRequestVersion);

        _vm.VideoPlayerService.Stop();

        _vm._isContentTransitioning = true;
        _vm._isPreferenceApplied = false;
        _vm.IsBuffering = true;

        if (_vm.IsPiPMode)
        {
            _vm.IsPiPControlsForceVisible = true;
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.IsPiPControlsVisible));
        }

        _vm.CurrentChannel = channel;
        _vm.CurrentProgram = _vm.GetFallbackProgram();
        _vm.IsLiveContent = channel.Type == ChannelType.Live;
        _vm.IsSeriesContent = channel.Type == ChannelType.Series;
        _vm.UpdateOverlaySecondaryText();
        _vm.PrepareForContentLoading();
        
        if (startPosition.HasValue && startPosition.Value > 0)
        {
            _vm._lastPausedPosition = startPosition.Value;
            _vm._lastPausedTimeMs = (long)(startPosition.Value * 1000);
            _vm._pendingResumeSeekPosition = startPosition.Value;
            _vm._lastKnownValidPosition = startPosition.Value;
        }
        else
        {
            _vm._lastPausedPosition = 0;
            _vm._lastPausedTimeMs = 0;
            _vm._pendingResumeSeekPosition = 0;
            _vm._lastKnownValidPosition = 0;
        }

        _vm.StreamQuality = null;
        _vm.StreamInfo = _vm.LocalizationService.GetString("Player.Status.QualityDetecting");
        try
        {
            string resolvedStreamUrl;

            if (channel.StreamUrl != null && channel.StreamUrl.StartsWith("stalker-series-ep://", StringComparison.OrdinalIgnoreCase))
            {
                var raw = channel.StreamUrl.Substring("stalker-series-ep://".Length);
                string cmd = string.Empty;
                string epNum = "0";

                int queryIndex = raw.IndexOf('?');
                if (queryIndex >= 0)
                {
                    var query = raw.Substring(queryIndex + 1);
                    var queryParams = System.Web.HttpUtility.ParseQueryString(query);
                    cmd = System.Net.WebUtility.UrlDecode(queryParams["cmd"] ?? string.Empty);
                    epNum = queryParams["ep"] ?? "0";
                }
                else
                {
                    cmd = raw;
                }

                var portalUrl = _vm.MainViewModel?.CurrentProfile?.ProviderAccount?.Url ?? string.Empty;
                var macAddress = _vm.MainViewModel?.CurrentProfile?.ProviderAccount?.Username ?? string.Empty;

                _vm.StreamInfo = _vm.LocalizationService.GetString("Player.Status.FetchingVideoUrl");
                
                var stalkerResolvedUrl = await _vm.StalkerPortalService.CreateLinkAsync(
                    portalUrl,
                    macAddress,
                    "vod",
                    cmd,
                    epNum);

                if (string.IsNullOrEmpty(stalkerResolvedUrl))
                    throw new InvalidOperationException(_vm.LocalizationService.GetString("Player.Error.StalkerVideoUrl"));

                resolvedStreamUrl = stalkerResolvedUrl;
            }
            else
            {
                if (channel.StreamUrl == null)
                    throw new InvalidOperationException("Kanal akış adresi bulunamadı.");
                resolvedStreamUrl = await _vm.ContentDownloadService.ResolvePlayableUrlAsync(channel.StreamUrl);
            }
            
            if (startPosition.HasValue && startPosition.Value > 0 && !_vm.IsDownloadedPlayback && resolvedStreamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _vm.LogDebug($"PlayChannelAsync: Starting HTTP stream with start-time={startPosition.Value}");
                await _vm.VideoPlayerService.PlayAsync(resolvedStreamUrl, startPosition.Value);
            }
            else
            {
                await _vm.VideoPlayerService.PlayAsync(resolvedStreamUrl);
            }
        }
        catch
        {
            if (requestVersion == _vm._playRequestVersion && _vm.CurrentChannel?.Id == channel.Id)
            {
                _vm.IsBuffering = false;
                _vm.BufferingProgress = 0;
            }

            throw;
        }

        if (requestVersion != _vm._playRequestVersion || _vm.CurrentChannel?.Id != channel.Id)
        {
            return;
        }

        _vm._lastWatchHistoryUpdateUtc = DateTime.UtcNow;
        _vm._sessionPlaybackStartTimeUtc = DateTime.UtcNow;
        _vm._watchHistoryTimer.Start();

        var program = await _vm.EpgService.GetCurrentProgramAsync(channel);
        if (requestVersion == _vm._playRequestVersion && _vm.CurrentChannel?.Id == channel.Id)
        {
            _vm.CurrentProgram = program ?? _vm.GetFallbackProgram();
            _vm.UpdateOverlaySecondaryText();
        }

        if (channel.Type == ChannelType.Series)
        {
            try
            {
                if (_vm._currentSeriesContext == null || _vm._currentSeriesContext.Seasons == null || _vm._currentSeriesContext.Seasons.Count == 0)
                {
                    var allSeries = await _vm.MediaService.GetSeriesAsync(channel.PlaylistId);
                    var matchedSeries = allSeries.FirstOrDefault(s => 
                        s.Name.Equals(channel.Name, StringComparison.OrdinalIgnoreCase) ||
                        (channel.GroupTitle != null && s.Name.Equals(channel.GroupTitle, StringComparison.OrdinalIgnoreCase)));
                    
                    if (matchedSeries != null && requestVersion == _vm._playRequestVersion && _vm.CurrentChannel?.Id == channel.Id)
                    {
                        _vm._currentSeriesContext = matchedSeries;
                        _vm.RefreshEpisodeBrowserContext(matchedSeries);
                    }
                }
            }
            catch (Exception ex)
            {
                _vm.LogDebug($"Failed to resolve series context: {ex.Message}");
            }
        }

        _ = _vm.EnsurePlaybackHealthAsync(channel, requestVersion);
    }

    public async Task PlayPause()
    {
        _vm.LogDebug($"UI Action: PlayPause clicked (Current IsPlaying={_vm.IsPlaying})");
        if (Interlocked.Exchange(ref _vm._isPlayPauseInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var treatAsLivePlayback =
                _vm.IsLiveContent ||
                _vm.CurrentChannel?.Type == ChannelType.Live ||
                LooksLikeLiveStreamUrl(_vm.CurrentChannel?.StreamUrl);

            if (_vm.IsPlaying)
            {
                _vm._isIntentionallyPaused = true;
                if (treatAsLivePlayback)
                {
                    _vm.VideoPlayerService.Pause();
                    _vm._livePauseRequiresHardRestart = true;
                    return;
                }

                var mediaPlayer = _vm.VideoPlayerService.GetMediaPlayer();
                _vm._lastPausedTimeMs = mediaPlayer?.Time ?? 0;
                _vm._lastPausedPosition = _vm._lastPausedTimeMs > 0 ? _vm._lastPausedTimeMs / 1000.0 : _vm.Position;
                _vm.VideoPlayerService.Pause();
            }
            else if (_vm.CurrentChannel != null)
            {
                _vm._isIntentionallyPaused = false;
                var mediaPlayer = _vm.VideoPlayerService.GetMediaPlayer();
                var state = mediaPlayer?.State ?? LibVLCSharp.Shared.VLCState.NothingSpecial;
                var isStreamDead = state == LibVLCSharp.Shared.VLCState.Stopped || 
                                   state == LibVLCSharp.Shared.VLCState.Ended || 
                                   state == LibVLCSharp.Shared.VLCState.Error || 
                                   state == LibVLCSharp.Shared.VLCState.NothingSpecial;
                
                if (isStreamDead) 
                {
                    _vm.LogDebug($"PlayPause: Stream is dead (State: {state}), initiating fresh play. _lastKnownValidPosition: {_vm._lastKnownValidPosition}");
                    if (_vm._lastPausedPosition <= 1 && _vm._lastKnownValidPosition > 1)
                    {
                        _vm._lastPausedPosition = _vm._lastKnownValidPosition;
                    }
                }

                var isResumable = mediaPlayer?.Media != null && !isStreamDead;
                await ResumePlaybackAsync(_vm.CurrentChannel.StreamUrl, isResumable);
            }
            
            _vm.RestartAutoHideTimer();
        }
        finally
        {
            Interlocked.Exchange(ref _vm._isPlayPauseInProgress, 0);
        }
    }

    public async Task ResumePlaybackAsync(string streamUrl, bool hasLoadedMedia)
    {
        var treatAsLivePlayback =
            _vm.IsLiveContent ||
            _vm.CurrentChannel?.Type == ChannelType.Live ||
            LooksLikeLiveStreamUrl(streamUrl);

        if (treatAsLivePlayback)
        {
            _vm._pendingResumeSeekPosition = 0;
            _vm._pendingResumeSeekAttempts = 0;
            _vm._lastPausedPosition = 0;
            _vm._lastPausedTimeMs = 0;
            if (_vm._livePauseRequiresHardRestart)
            {
                _vm.VideoPlayerService.Stop();
                await Task.Delay(120);
                _vm.IsBuffering = true;
                _vm.BufferingProgress = 0;
                await _vm.VideoPlayerService.PlayAsync(streamUrl);
                _vm._livePauseRequiresHardRestart = false;
            }
            else if (hasLoadedMedia)
            {
                _vm.VideoPlayerService.Resume();
            }
            else
            {
                _vm.IsBuffering = true;
                _vm.BufferingProgress = 0;
                await _vm.VideoPlayerService.PlayAsync(streamUrl);
            }
            return;
        }

        if (_vm._lastPausedPosition <= 1)
        {
            if (hasLoadedMedia)
            {
                _vm.VideoPlayerService.Resume();
            }
            else
            {
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                await _vm.VideoPlayerService.PlayAsync(streamUrl);
            }

            return;
        }

        var targetPosition = _vm._lastPausedPosition;
        var targetTimeMs = (_vm._lastPausedTimeMs > 0 ? _vm._lastPausedTimeMs : (long)(targetPosition * 1000)) + 350;
        
        if (hasLoadedMedia)
        {
            _vm.VideoPlayerService.Resume();
            await Task.Delay(220);

            var resumePlayer = _vm.VideoPlayerService.GetMediaPlayer();
            var resumedMs = resumePlayer?.Time ?? 0;
            if (resumedMs > 0)
            {
                var driftMs = targetTimeMs - resumedMs;
                if (driftMs <= 1000)
                {
                    _vm._pendingResumeSeekPosition = 0;
                    _vm._pendingResumeSeekAttempts = 0;
                    await EnsurePlaybackStartedAsync(streamUrl);
                    return;
                }
            }
            else if (_vm.Position + 1 >= targetPosition)
            {
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }

            if (!_vm.IsDownloadedPlayback && streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _vm.LogDebug($"ResumePlaybackAsync: Significant drift detected, using HardSeekAsync for HTTP stream to {targetPosition}s");
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                await _vm.VideoPlayerService.HardSeekAsync(targetPosition);
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
            else
            {
                _vm._pendingResumeSeekPosition = targetPosition;
                _vm._pendingResumeSeekAttempts = 0;
            }
        }
        else
        {
            if (!_vm.IsDownloadedPlayback && streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _vm.LogDebug($"ResumePlaybackAsync: Fresh play for HTTP stream, passing startTime={targetPosition}s to PlayAsync");
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                await _vm.VideoPlayerService.PlayAsync(streamUrl, targetPosition);
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
            else
            {
                _vm._pendingResumeSeekPosition = targetPosition;
                _vm._pendingResumeSeekAttempts = 0;
                await _vm.VideoPlayerService.PlayAsync(streamUrl);
            }
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(220 + (attempt * 60));
            if (_vm.IsLiveContent || _vm.CurrentChannel == null)
            {
                return;
            }

            var mediaPlayer = _vm.VideoPlayerService.GetMediaPlayer();
            if (mediaPlayer != null && targetTimeMs > 0)
            {
                mediaPlayer.Time = targetTimeMs;
            }
            else
            {
                _vm.VideoPlayerService.Position = targetPosition;
            }
            await Task.Delay(120);

            var currentTimeMs = mediaPlayer?.Time ?? 0;
            if (currentTimeMs > 0 && currentTimeMs + 1200 >= targetTimeMs)
            {
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }

            if (_vm.Position + 1 >= targetPosition)
            {
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                await EnsurePlaybackStartedAsync(streamUrl);
                return;
            }
        }

        await EnsurePlaybackStartedAsync(streamUrl);
    }

    public async Task EnsurePlaybackStartedAsync(string streamUrl)
    {
        if (_vm.IsPlaying || _vm.CurrentChannel == null || _vm.IsLiveContent)
        {
            return;
        }

        await Task.Delay(280);
        if (_vm.IsPlaying)
        {
            return;
        }

        await _vm.VideoPlayerService.PlayAsync(streamUrl);
    }

    public bool LooksLikeLiveStreamUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var normalized = url.ToLowerInvariant();
        if (normalized.Contains("/movie/") || normalized.Contains("/series/") || normalized.Contains("/vod/"))
        {
            return false;
        }

        return normalized.Contains("/live/");
    }

    public void TryApplyPendingResumeSeek()
    {
        if (_vm.IsLiveContent || _vm._pendingResumeSeekPosition <= 1)
        {
            return;
        }

        var mediaPlayer = _vm.VideoPlayerService.GetMediaPlayer();
        var pendingTimeMs = _vm._lastPausedTimeMs > 0 ? _vm._lastPausedTimeMs : (long)(_vm._pendingResumeSeekPosition * 1000);
        _vm.LogDebug($"TryApplyPendingResumeSeek: Current Position={_vm.Position}, PendingResumeSeekPosition={_vm._pendingResumeSeekPosition}, PendingTimeMs={pendingTimeMs}");

        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            if (mediaPlayer.Time + 1000 >= pendingTimeMs)
            {
                _vm.LogDebug($"TryApplyPendingResumeSeek: mediaPlayer.Time ({mediaPlayer.Time}) is close enough to pendingTimeMs ({pendingTimeMs}). Resetting pending seek.");
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                return;
            }
        }

        if (_vm.Position + 1 >= _vm._pendingResumeSeekPosition)
        {
            _vm.LogDebug($"TryApplyPendingResumeSeek: Position ({_vm.Position}) is close enough to PendingResumeSeekPosition ({_vm._pendingResumeSeekPosition}). Resetting pending seek.");
            _vm._pendingResumeSeekPosition = 0;
            _vm._pendingResumeSeekAttempts = 0;
            return;
        }

        if (_vm._pendingResumeSeekAttempts >= 20)
        {
            _vm.LogDebug($"TryApplyPendingResumeSeek: Max attempts reached ({_vm._pendingResumeSeekAttempts}). Resetting pending seek.");
            _vm._pendingResumeSeekPosition = 0;
            _vm._pendingResumeSeekAttempts = 0;
            return;
        }

        _vm._pendingResumeSeekAttempts++;
        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            var driftMs = pendingTimeMs - mediaPlayer.Time;
            if (driftMs <= 1000)
            {
                _vm.LogDebug($"TryApplyPendingResumeSeek: Drift ({driftMs}ms) is within tolerance. Resetting pending seek.");
                _vm._pendingResumeSeekPosition = 0;
                _vm._pendingResumeSeekAttempts = 0;
                return;
            }
        }

        if (mediaPlayer != null && pendingTimeMs > 0)
        {
            var duration = _vm.VideoPlayerService.Duration;
            if (duration > 0)
            {
                var fraction = (float)(_vm._pendingResumeSeekPosition / duration);
                _vm.LogDebug($"TryApplyPendingResumeSeek: Setting mediaPlayer.Position fraction={fraction} for {pendingTimeMs}ms");
                mediaPlayer.Position = Math.Clamp(fraction, 0f, 1f);
            }
            else
            {
                _vm.LogDebug($"TryApplyPendingResumeSeek: Duration is 0, falling back to Time={pendingTimeMs}");
                mediaPlayer.Time = pendingTimeMs;
            }
        }
        else
        {
            _vm.LogDebug($"TryApplyPendingResumeSeek: Setting Position setter to {_vm._pendingResumeSeekPosition}");
            _vm.VideoPlayerService.Position = _vm._pendingResumeSeekPosition;
        }
    }

    public async Task Stop()
    {
        _vm.LogDebug("UI Action: Stop clicked");
        _vm._isContentTransitioning = false;
        _vm._isPlaybackEnded = false;
        ResetSeekInteractionState();
        _vm._watchHistoryTimer.Stop();
        await _vm.FlushWatchHistoryAsync(force: true);
        _vm.VideoPlayerService.Stop();
        _vm._livePauseRequiresHardRestart = false;
        _vm._lastLiveProgressAtUtc = DateTime.MinValue;
        _vm._lastLivePositionEventAtUtc = DateTime.MinValue;
        _vm._lastLiveAutoRecoverAttemptAtUtc = DateTime.MinValue;
        _vm._liveRecoveryWindowStartUtc = DateTime.MinValue;
        _vm.CurrentChannel = null;
        _vm.CurrentProgram = null;
        _vm.IsVisible = true;
        await _vm.ContentDownloadService.CleanupPlaybackCacheAsync();
    }

    public void ToggleMute()
    {
        _vm.LogDebug($"UI Action: ToggleMute clicked (Current IsMuted={_vm.IsMuted})");
        if (!_vm.IsMuted)
        {
            if (_vm.Volume > 0)
            {
                _vm._volumeBeforeMute = _vm.Volume;
            }

            _vm.IsMuted = true;
            if (_vm.Volume != 0)
            {
                _vm.Volume = 0;
            }
        }
        else
        {
            _vm.IsMuted = false;
            if (_vm.Volume == 0)
            {
                _vm.Volume = _vm._volumeBeforeMute > 0 ? _vm._volumeBeforeMute : 50;
            }
        }

        _vm.RestartAutoHideTimer();
    }

    public void StartSeeking()
    {
        _vm.LogDebug("StartSeeking invoked.");
        if (Interlocked.CompareExchange(ref _vm._recoveryState, 0, 0) != 0)
            return;

        _vm._isUserSeeking = true;
    }

    public void Seek(double position)
    {
        _vm.LogDebug($"Seek invoked with value: {position}");
        _vm._isUserSeeking = false;

        if (Interlocked.CompareExchange(ref _vm._recoveryState, 0, 0) != 0)
            return;

        if (_vm.IsLiveContent) return;
        EnableSeekBufferShieldSuppression();
        var clamped = ClampSeekPosition(position);
        ResetSkipSeekCarry();
        ResetSkipOverlayAggregation();
        _vm.Position = clamped;
        _vm.PositionText = TimeSpan.FromSeconds(clamped).ToString(@"hh\:mm\:ss");
        if (_vm.Duration > 0)
        {
            var remaining = Math.Max(0, _vm.Duration - clamped);
            _vm.RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
        }
        _vm.CheckIntroCreditsPosition(clamped);
        SetPlaybackPosition(clamped);
        if (_vm._isPlaybackEnded)
        {
            _ = EnsurePlaybackResumedAfterEndedSeekAsync(clamped);
        }
        _vm.RestartAutoHideTimer();
    }

    public void SkipForward(object? parameter)
    {
        ApplySkipDelta(ParseSkipSeconds(parameter));
    }

    public void SkipBackward(object? parameter)
    {
        ApplySkipDelta(-ParseSkipSeconds(parameter));
    }

    public void ApplySkipDelta(double deltaSeconds)
    {
        if (_vm.IsLiveContent)
        {
            return;
        }

        if (Math.Abs(deltaSeconds) <= 0.001)
        {
            _vm.RestartAutoHideTimer();
            return;
        }

        EnableSeekBufferShieldSuppression();

        var nowUtc = DateTime.UtcNow;
        var basePosition = _vm.Position;
        if (_vm._hasPendingSkipSeekTarget && nowUtc <= _vm._pendingSkipSeekExpiresUtc)
        {
            basePosition = _vm._pendingSkipSeekTarget;
        }
        else
        {
            ResetSkipSeekCarry();
        }

        var targetPosition = ClampSeekPosition(basePosition + deltaSeconds);
        var effectiveDelta = targetPosition - basePosition;
        if (Math.Abs(effectiveDelta) <= 0.001)
        {
            _vm.RestartAutoHideTimer();
            return;
        }

        _vm._pendingSkipSeekTarget = targetPosition;
        _vm._pendingSkipSeekExpiresUtc = nowUtc.AddMilliseconds(1400); // SkipSeekCarryWindowMs
        _vm._hasPendingSkipSeekTarget = true;

        _vm.Position = targetPosition;
        _vm.PositionText = TimeSpan.FromSeconds(targetPosition).ToString(@"hh\:mm\:ss");
        if (_vm.Duration > 0)
        {
            var remaining = Math.Max(0, _vm.Duration - targetPosition);
            _vm.RemainingTime = "-" + TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");
        }
        _vm.CheckIntroCreditsPosition(targetPosition);

        SetPlaybackPosition(targetPosition);
        if (_vm._isPlaybackEnded)
        {
            _ = EnsurePlaybackResumedAfterEndedSeekAsync(targetPosition);
        }
        RaiseSkipOverlay(effectiveDelta, nowUtc);
        _vm.RestartAutoHideTimer();
    }

    public async Task EnsurePlaybackResumedAfterEndedSeekAsync(double targetPosition)
    {
        if (!_vm._isPlaybackEnded || _vm.IsLiveContent || _vm.CurrentChannel == null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _vm._recoveryState, 2, 0) != 0) // EndedSeekRecovering
        {
            return;
        }

        try
        {
            _vm.VideoPlayerService.Resume();
            await Task.Delay(180);

            if (_vm.IsPlaying)
            {
                _vm._isPlaybackEnded = false;
                return;
            }

            var existingPlayer = _vm.VideoPlayerService.GetMediaPlayer();
            if (existingPlayer?.Media != null)
            {
                existingPlayer.Play();
                await Task.Delay(180);
                if (_vm.IsPlaying)
                {
                    _vm._isPlaybackEnded = false;
                    return;
                }
            }

            var streamUrl = _vm.CurrentChannel?.StreamUrl;
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return;
            }

            await _vm.VideoPlayerService.PlayAsync(streamUrl);
            SetPlaybackPosition(targetPosition);

            _vm._isPlaybackEnded = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Ended-seek recover failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _vm._recoveryState, 0);
        }
    }

    public void RaiseSkipOverlay(double deltaSeconds, DateTime nowUtc)
    {
        var shouldResetAggregation =
            _vm._skipAggregationLastUpdatedUtc == DateTime.MinValue ||
            nowUtc - _vm._skipAggregationLastUpdatedUtc > TimeSpan.FromMilliseconds(1200) || // SkipAggregationWindowMs
            Math.Sign(_vm._skipAggregationSeconds) != Math.Sign(deltaSeconds);

        if (shouldResetAggregation)
        {
            _vm._skipAggregationSeconds = deltaSeconds;
        }
        else
        {
            _vm._skipAggregationSeconds += deltaSeconds;
        }

        _vm._skipAggregationLastUpdatedUtc = nowUtc;
        _vm.RaiseSkipOverlayEvent(_vm._skipAggregationSeconds);
    }

    public double ParseSkipSeconds(object? parameter)
    {
        const double defaultSeconds = 10;

        if (parameter == null)
        {
            return defaultSeconds;
        }

        if (parameter is int intValue)
        {
            return Math.Abs(intValue);
        }

        if (parameter is double doubleValue)
        {
            return Math.Abs(doubleValue);
        }

        if (parameter is string str && double.TryParse(str, out var parsed))
        {
            return Math.Abs(parsed);
        }

        return defaultSeconds;
    }

    public void ReleaseSkipSeekCarryIfSettled(DateTime nowUtc, double currentPosition)
    {
        if (!_vm._hasPendingSkipSeekTarget)
        {
            return;
        }

        if (nowUtc > _vm._pendingSkipSeekExpiresUtc ||
            Math.Abs(currentPosition - _vm._pendingSkipSeekTarget) <= 2.0) // SkipSeekCarryToleranceSeconds
        {
            ResetSkipSeekCarry();
        }
    }

    public void ResetSkipSeekCarry()
    {
        _vm._hasPendingSkipSeekTarget = false;
        _vm._pendingSkipSeekTarget = 0;
        _vm._pendingSkipSeekExpiresUtc = DateTime.MinValue;
    }

    public void ResetSkipOverlayAggregation()
    {
        _vm._skipAggregationSeconds = 0;
        _vm._skipAggregationLastUpdatedUtc = DateTime.MinValue;
    }

    public void ResetSeekInteractionState()
    {
        ResetSkipSeekCarry();
        ResetSkipOverlayAggregation();
    }

    public void EnableSeekBufferShieldSuppression()
    {
        _vm._suppressBufferShieldForSeek = true;
        _vm.RaisePropertyChanged(nameof(PlayerViewModel.IsBufferShieldVisible));
        var token = Interlocked.Increment(ref _vm._seekShieldSuppressionToken);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(8000).ConfigureAwait(false); // SeekBufferShieldSuppressionMs
            }
            catch
            {
                return;
            }

            if (token != _vm._seekShieldSuppressionToken)
            {
                return;
            }

            _vm.DispatcherService.BeginInvoke(CancelSeekBufferShieldSuppression);
        });
    }

    public void CancelSeekBufferShieldSuppression()
    {
        var wasSuppressed = _vm._suppressBufferShieldForSeek;
        _vm._suppressBufferShieldForSeek = false;
        
        if (wasSuppressed && !_vm._isContentTransitioning && _vm.IsBuffering && !_vm.IsLiveContent)
        {
            _vm.IsBuffering = false;
        }

        _vm.RaisePropertyChanged(nameof(PlayerViewModel.IsBufferShieldVisible));
    }

    public void SetPlaybackPosition(double position)
    {
        var clamped = ClampSeekPosition(position);
        
        var targetTimeMs = (long)Math.Max(0, clamped * 1000);
        if (targetTimeMs == _vm._lastSeekTargetMs) return;
        _vm._lastSeekTargetMs = targetTimeMs;

        _vm._lastKnownValidPosition = clamped;

        _vm.LogDebug($"SetPlaybackPosition: position={position}, clamped={clamped}");

        if (!_vm.IsDownloadedPlayback && _vm.VideoPlayerService.CurrentUrl?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
        {
            _vm.LogDebug($"SetPlaybackPosition: HTTP stream detected, executing HardSeekAsync to {clamped}s");
            _ = _vm.VideoPlayerService.HardSeekAsync(clamped);
            return;
        }

        var duration = _vm.VideoPlayerService.Duration > 0 ? _vm.VideoPlayerService.Duration : _vm.Duration;

        var targetFraction = duration > 0 ? (float)(clamped / duration) : 0f;
        if (targetFraction < 0f) targetFraction = 0f;
        if (targetFraction > 1f) targetFraction = 1f;

        var mediaPlayer = _vm.VideoPlayerService.GetMediaPlayer();
        if (mediaPlayer != null)
        {
            if (duration > 0)
            {
                _vm.LogDebug($"SetPlaybackPosition: Executing internal Position seek to {targetFraction} (Time: {targetTimeMs}ms)");
                mediaPlayer.Position = targetFraction;
            }
            else
            {
                _vm.LogDebug($"SetPlaybackPosition: Fallback executing internal Time seek to {targetTimeMs}ms");
                mediaPlayer.Time = targetTimeMs;
            }
            return;
        }

        _vm.VideoPlayerService.Position = clamped;
    }

    public double ClampSeekPosition(double position)
    {
        if (double.IsNaN(position) || double.IsInfinity(position))
        {
            return _vm.Position;
        }

        if (_vm.Duration > 0)
        {
            return Math.Clamp(position, 0, _vm.Duration);
        }

        return Math.Max(0, position);
    }
}
