using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public sealed class PlaybackExitSnapshot
{
    public int? ProfileId { get; init; }
    public Channel? Channel { get; init; }
    public Episode? Episode { get; init; }
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public class PlayerEpisodeNavigator
{
    private const int NextEpisodeCountdownStartSeconds = 8;

    private readonly PlayerViewModel _vm;
    private CancellationTokenSource? _nextEpisodeCountdownCts;
    private int _nextEpisodeCountdownGeneration;
    private int _nextEpisodeTransitionClaimed;
    private int _isDisposed;
    private bool _isPlaybackSessionActive;
    private bool _isAutoPlayCancelledForCurrentEpisode;

    public PlayerEpisodeNavigator(PlayerViewModel vm)
    {
        _vm = vm;
    }

    internal int NextEpisodeCountdownGeneration =>
        Volatile.Read(ref _nextEpisodeCountdownGeneration);

    internal bool IsAutoPlayCancelledForCurrentEpisode =>
        _isAutoPlayCancelledForCurrentEpisode;

    public void SetCurrentEpisode(Episode? episode, Episode? nextEpisode = null, Series? series = null)
    {
        ResetNextEpisodeSession();
        _isPlaybackSessionActive = episode != null;
        _vm.CurrentEpisode = episode;
        _vm.CurrentEpisodeIdentity = BuildEpisodeIdentity(episode);
        _vm.NextEpisode = nextEpisode;
        _vm._creditsTriggered = false;
        _vm.IsCreditsZone = false;
        _vm.IsEpisodesPanelOpen = false;
        _vm._isPreferenceApplied = false;

        if (episode == null)
        {
            _vm._currentSeriesContext = null;
            _vm.EpisodeSeasons = new List<Season>();
            _vm.EpisodesPanelTitle = string.Empty;
            _vm.RaiseInfoPanelMetadataChanged();
            _vm.PlayEpisodeFromOverlayCommand.NotifyCanExecuteChanged();
            _vm.PlayNextEpisodeCommand.NotifyCanExecuteChanged();
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.CanDownloadCurrentContent));
            _vm.DownloadCurrentContentCommand.NotifyCanExecuteChanged();
            return;
        }

        _vm._currentSeriesContext = series
            ?? episode.Season?.Series
            ?? _vm._currentSeriesContext;
        _vm.RaiseInfoPanelMetadataChanged();
        RefreshEpisodeBrowserContext(_vm._currentSeriesContext);

        if (episode?.Duration is TimeSpan knownDuration && knownDuration.TotalSeconds > 0)
        {
            var seconds = knownDuration.TotalSeconds;
            if (_vm.Duration <= 0 || Math.Abs(_vm.Duration - seconds) > 1)
            {
                _vm.Duration = seconds;
                _vm.DurationText = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
            }
        }

        _vm.PlayNextEpisodeCommand.NotifyCanExecuteChanged();
        _vm.PlayEpisodeFromOverlayCommand.NotifyCanExecuteChanged();
        _vm.RaisePropertyChanged(nameof(PlayerViewModel.CanDownloadCurrentContent));
        _vm.DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    public void TryShowNextEpisodePromptAtEnd()
    {
        if (!_isPlaybackSessionActive ||
            Volatile.Read(ref _isDisposed) != 0 ||
            _vm.IsClosingPlayer ||
            _vm.IsLiveContent ||
            _vm.CurrentChannel?.Type != ChannelType.Series ||
            _vm.NextEpisode == null)
        {
            return;
        }

        _vm._creditsTriggered = true;
        _vm.IsCreditsZone = true;

        if (_isAutoPlayCancelledForCurrentEpisode)
        {
            StopNextEpisodeCountdown();
            _vm.IsNextEpisodePromptVisible = false;
            return;
        }

        if (!_vm.SettingsService.Settings.AutoPlayNext)
        {
            StopNextEpisodeCountdown();
            _vm.IsNextEpisodePromptVisible = true;
            return;
        }

        _ = TryPlayNextEpisodeAsync(userInitiated: false);
    }

    public void CheckIntroCreditsPosition(double pos)
    {
        if (!_isPlaybackSessionActive ||
            Volatile.Read(ref _isDisposed) != 0 ||
            _vm.IsClosingPlayer)
        {
            return;
        }

        if (_vm.CurrentEpisode == null || _vm.IsLiveContent || _vm.NextEpisode == null)
        {
            return;
        }

        if (!TryGetCreditsTriggerThreshold(out var triggerAt))
        {
            return;
        }

        var isInCreditsZone = pos >= triggerAt;
        var exitThreshold = Math.Max(0, triggerAt - 3);
        var hasExitedCreditsZone = pos < exitThreshold;

        if (_isAutoPlayCancelledForCurrentEpisode)
        {
            StopNextEpisodeCountdown();
            _vm.IsNextEpisodePromptVisible = false;
            _vm.IsCreditsZone = !hasExitedCreditsZone;

            if (hasExitedCreditsZone)
            {
                _vm._creditsTriggered = false;
            }

            return;
        }

        if (_vm._creditsTriggered)
        {
            if (hasExitedCreditsZone)
            {
                StopNextEpisodeCountdown();
                _vm._creditsTriggered = false;
                _vm.IsCreditsZone = false;
                _vm.IsNextEpisodePromptVisible = false;
            }
            else
            {
                _vm.IsCreditsZone = true;
                _vm.IsNextEpisodePromptVisible = true;
                StartNextEpisodeCountdownIfEligible();
            }

            return;
        }

        if (!isInCreditsZone)
        {
            return;
        }

        _vm._creditsTriggered = true;
        _vm.IsCreditsZone = true;
        _vm.IsNextEpisodePromptVisible = true;
        StartNextEpisodeCountdownIfEligible();
    }

    public bool TryGetCreditsTriggerThreshold(out double triggerAt)
    {
        triggerAt = 0;
        if (_vm.CurrentEpisode == null)
        {
            return false;
        }

        var hasDuration = _vm.Duration > 0;
        var fallbackThreeMinuteTrigger = hasDuration
            ? Math.Max(0, _vm.Duration - TimeSpan.FromMinutes(3).TotalSeconds)
            : double.MaxValue;

        if (_vm.CurrentEpisode.CreditsStartSec is double creditsStartSec && creditsStartSec > 0)
        {
            triggerAt = hasDuration
                ? Math.Min(creditsStartSec, fallbackThreeMinuteTrigger)
                : creditsStartSec;
            return true;
        }

        if (!hasDuration)
        {
            return false;
        }

        var tailThreshold = Math.Clamp(
            _vm.Duration * 0.06, // NextEpisodePromptTailRatio = 0.06
            25, // NextEpisodePromptMinTailSeconds = 25
            180); // NextEpisodePromptMaxTailSeconds = 180
        triggerAt = Math.Min(
            Math.Max(0, _vm.Duration - tailThreshold),
            fallbackThreeMinuteTrigger);
        return true;
    }

    public async Task PlayNextEpisode()
    {
        await TryPlayNextEpisodeAsync(userInitiated: true);
    }

    public void CancelNextEpisode()
    {
        _isAutoPlayCancelledForCurrentEpisode = true;
        StopNextEpisodeCountdown();
        _vm.IsNextEpisodePromptVisible = false;
        _vm.RestartAutoHideTimer();
    }

    public void OnAutoPlayPreferenceChanged()
    {
        if (!_isPlaybackSessionActive ||
            Volatile.Read(ref _isDisposed) != 0 ||
            _vm.IsClosingPlayer)
        {
            return;
        }

        if (!_vm.SettingsService.Settings.AutoPlayNext)
        {
            StopNextEpisodeCountdown();
            return;
        }

        if (_vm.IsNextEpisodePromptVisible &&
            !_isAutoPlayCancelledForCurrentEpisode)
        {
            StartNextEpisodeCountdownIfEligible();
        }
    }

    public void ResetForPlaybackExit()
    {
        _isPlaybackSessionActive = false;
        ResetNextEpisodeSession();
        _vm._creditsTriggered = false;
        _vm.IsCreditsZone = false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _isPlaybackSessionActive = false;
        StopNextEpisodeCountdown();
        _vm.IsNextEpisodePromptVisible = false;
    }

    internal void AdvanceNextEpisodeCountdown(int generation)
    {
        if (!_isPlaybackSessionActive ||
            Volatile.Read(ref _isDisposed) != 0 ||
            _vm.IsClosingPlayer ||
            generation != NextEpisodeCountdownGeneration ||
            !_vm.IsNextEpisodeCountdownActive ||
            _isAutoPlayCancelledForCurrentEpisode ||
            !_vm.SettingsService.Settings.AutoPlayNext)
        {
            return;
        }

        var nextValue = Math.Max(0, _vm.NextEpisodeCountdownSeconds - 1);
        _vm.NextEpisodeCountdownSeconds = nextValue;

        if (nextValue > 0)
        {
            return;
        }

        StopNextEpisodeCountdown();
        _ = TryPlayNextEpisodeAsync(userInitiated: false);
    }

    private void StartNextEpisodeCountdownIfEligible()
    {
        if (!_isPlaybackSessionActive ||
            Volatile.Read(ref _isDisposed) != 0 ||
            _vm.IsClosingPlayer ||
            !_vm.SettingsService.Settings.AutoPlayNext ||
            _isAutoPlayCancelledForCurrentEpisode ||
            _vm.NextEpisode == null ||
            _vm.IsNextEpisodeCountdownActive)
        {
            return;
        }

        StopNextEpisodeCountdown();

        var countdownCts = new CancellationTokenSource();
        _nextEpisodeCountdownCts = countdownCts;
        var generation = Interlocked.Increment(ref _nextEpisodeCountdownGeneration);

        _vm.NextEpisodeCountdownSeconds = NextEpisodeCountdownStartSeconds;
        _vm.IsNextEpisodeCountdownActive = true;
        _ = RunNextEpisodeCountdownAsync(generation, countdownCts.Token);
    }

    private async Task RunNextEpisodeCountdownAsync(
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);

                _vm.DispatcherService.BeginInvoke(
                    () => AdvanceNextEpisodeCountdown(generation));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user cancels, rewinds, or changes content.
        }
    }

    private void StopNextEpisodeCountdown()
    {
        Interlocked.Increment(ref _nextEpisodeCountdownGeneration);

        var countdownCts = Interlocked.Exchange(
            ref _nextEpisodeCountdownCts,
            null);
        countdownCts?.Cancel();
        countdownCts?.Dispose();

        _vm.IsNextEpisodeCountdownActive = false;
        _vm.NextEpisodeCountdownSeconds = 0;
    }

    private void ResetNextEpisodeSession()
    {
        StopNextEpisodeCountdown();
        _isAutoPlayCancelledForCurrentEpisode = false;
        Interlocked.Exchange(ref _nextEpisodeTransitionClaimed, 0);
        _vm.IsNextEpisodePromptVisible = false;
    }

    private async Task TryPlayNextEpisodeAsync(bool userInitiated)
    {
        if (!_isPlaybackSessionActive ||
            Volatile.Read(ref _isDisposed) != 0 ||
            _vm.IsClosingPlayer ||
            _vm.NextEpisode == null ||
            (!userInitiated &&
             (!_vm.SettingsService.Settings.AutoPlayNext ||
              _isAutoPlayCancelledForCurrentEpisode)))
        {
            return;
        }

        if (_vm.IsDownloadedPlayback && !IsDownloadedStreamUrl(_vm.NextEpisode.StreamUrl))
        {
            _vm.DownloadStatusMessage =
                _vm.LocalizationService.GetString("Player.NextEpisode.NotDownloaded");
            _vm.RestartAutoHideTimer();
            return;
        }

        if (Interlocked.CompareExchange(
                ref _nextEpisodeTransitionClaimed,
                1,
                0) != 0)
        {
            return;
        }

        var nextEpisode = _vm.NextEpisode;
        StopNextEpisodeCountdown();
        _vm.PrepareForContentLoading();
        _vm.IsNextEpisodePromptVisible = false;
        _vm.IsCreditsZone = false;
        _vm._creditsTriggered = false;
        _vm.RaiseNextEpisodeRequestedEvent(nextEpisode);
        await Task.CompletedTask;
    }

    public async Task DownloadCurrentContentAsync()
    {
        if (_vm.CurrentChannel == null || !_vm.CanDownloadCurrentContent)
        {
            return;
        }

        if (Interlocked.Exchange(ref _vm._isDownloadActionRunning, 1) == 1)
        {
            return;
        }

        _vm.IsDownloadInProgress = true;
        _vm.DownloadStatusMessage = _vm.LocalizationService.GetString("Download.Status.Starting");
        _vm.RestartAutoHideTimer();

        try
        {
            var request = BuildDownloadRequest(_vm.CurrentChannel);
            var result = await _vm.ContentDownloadService.QueueDownloadAsync(request);
            _vm.DownloadStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            _vm.DownloadStatusMessage = UserFriendlyErrorMessage.WithPrefix(_vm.LocalizationService.GetString("Download.Status.Error"), ex);
        }
        finally
        {
            _vm.IsDownloadInProgress = false;
            Interlocked.Exchange(ref _vm._isDownloadActionRunning, 0);
        }
    }

    public DownloadContentRequest BuildDownloadRequest(Channel channel)
    {
        var profileId = _vm.CurrentProfileId ?? 0;
        var playlistId = channel.PlaylistId > 0
            ? channel.PlaylistId
            : _vm._currentSeriesContext?.PlaylistId ?? 0;
        var audioTracks = _vm.AudioTracks
            .Select(t => new DownloadTrackOption(t.Id, t.Name))
            .ToList();
        var subtitleTracks = _vm.SubtitleTracks
            .Select(t => new DownloadTrackOption(t.Id, t.Name))
            .ToList();
        var poster = channel.CoverUrl ?? channel.LogoUrl;

        if (channel.Type == ChannelType.Series)
        {
            return new DownloadContentRequest(
                profileId,
                DownloadItemType.SeriesEpisode,
                _vm.CurrentEpisode?.Name ?? channel.Name,
                _vm.CurrentEpisode?.StreamUrl ?? channel.StreamUrl,
                poster,
                playlistId,
                channel.Id,
                _vm.CurrentEpisode?.Id ?? 0,
                audioTracks,
                subtitleTracks);
        }

        return new DownloadContentRequest(
            profileId,
            DownloadItemType.Vod,
            channel.Name,
            channel.StreamUrl,
            poster,
            playlistId,
            channel.Id,
            0,
            audioTracks,
            subtitleTracks);
    }

    public void PlayEpisodeFromOverlay(Episode? episode)
    {
        if (episode == null)
        {
            return;
        }

        var nextEpisode = FindNextEpisodeInBrowser(episode);
        SetCurrentEpisode(episode, nextEpisode, _vm._currentSeriesContext);
        _vm.IsLocked = false;

        _vm.PrepareForContentLoading();
        _vm.RaiseEpisodeRequestedEvent(episode);
        _vm.RestartAutoHideTimer();
    }

    public bool CanPlayEpisodeFromOverlay(Episode? episode)
    {
        return episode != null &&
               !string.IsNullOrWhiteSpace(episode.StreamUrl);
    }

    public void RefreshEpisodeBrowserContext(Series? series)
    {
        _vm._currentSeriesContext = series;
        _vm.EpisodesPanelTitle = series?.Name ?? _vm.CurrentChannel?.Name ?? string.Empty;

        var sourceSeasons = series?.Seasons;

        if (sourceSeasons == null || sourceSeasons.Count == 0)
        {
            _vm.EpisodeSeasons = new List<Season>();
            _vm.SelectedEpisodeSeason = null;
            return;
        }

        var newSeasons = sourceSeasons
            .Where(s => s.Episodes != null && s.Episodes.Count > 0)
            .OrderBy(s => s.SeasonNumber)
            .ToList();

        foreach (var season in newSeasons)
        {
            season.IsExpanded = season.Episodes.Any(e => BuildEpisodeIdentity(e) == _vm.CurrentEpisodeIdentity);
        }

        _vm.EpisodeSeasons = newSeasons;
        var selectedSeason = newSeasons.FirstOrDefault(s => s.IsExpanded) ?? newSeasons.FirstOrDefault();
        _vm.SelectedEpisodeSeason = selectedSeason;
    }

    public Episode? FindNextEpisodeInBrowser(Episode episode)
    {
        if (_vm.EpisodeSeasons.Count == 0)
        {
            return null;
        }

        var orderedEpisodes = _vm.EpisodeSeasons
            .OrderBy(s => s.SeasonNumber)
            .SelectMany(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
            .ToList();

        var currentIndex = orderedEpisodes.FindIndex(e =>
            e.Id > 0 && episode.Id > 0
                ? e.Id == episode.Id
                : string.Equals(e.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0 || currentIndex + 1 >= orderedEpisodes.Count)
        {
            return null;
        }

        return orderedEpisodes[currentIndex + 1];
    }

    public static string BuildEpisodeIdentity(Episode? episode)
    {
        if (episode == null)
        {
            return string.Empty;
        }

        if (episode.Id > 0)
        {
            return $"id:{episode.Id}";
        }

        if (!string.IsNullOrWhiteSpace(episode.StreamUrl))
        {
            return $"url:{episode.StreamUrl.Trim()}";
        }

        return string.Empty;
    }

    public static bool IsDownloadedStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        var normalized = streamUrl.Trim().Trim('"', '\'');
        if (normalized.Length < 4)
        {
            return false;
        }

        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri))
            {
                return fileUri.IsFile && File.Exists(fileUri.LocalPath);
            }
        }

        var isLocal = normalized.StartsWith(@"\\", StringComparison.Ordinal) ||
                      Regex.IsMatch(normalized, @"^[a-zA-Z]:[\\/]");
        
        if (isLocal)
        {
            return File.Exists(normalized);
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return File.Exists(normalized);
        }

        return false;
    }

    public static bool LooksLikeDownloadedPlaybackStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        if (IsDownloadedStreamUrl(streamUrl))
        {
            return true;
        }

        var normalized = streamUrl.Trim().Trim('"', '\'').ToLowerInvariant();
        
        if (normalized.Contains(@"\noctra\downloads\profile_") || normalized.Contains("/noctra/downloads/profile_"))
        {
            return true;
        }

        return false;
    }

    public static bool IsEpisodeCompleted(double durationSeconds, double positionSeconds)
    {
        if (durationSeconds <= 0 || positionSeconds <= 0)
        {
            return false;
        }

        var percentReached = (positionSeconds / durationSeconds) * 100.0;
        var remainingSeconds = Math.Max(0, durationSeconds - positionSeconds);

        return percentReached >= 90.0 // EpisodeCompletedPercentThreshold = 90.0
            || (durationSeconds > 180 && remainingSeconds <= 180); // EpisodeCompletedTailSeconds = 180
    }

    public async Task TrackWatchHistoryAsync()
    {
        if (_vm.WatchHistoryService == null || _vm.CurrentProfileId == null || _vm.CurrentChannel == null || !_vm.IsPlaying)
            return;

        var nowUtc = DateTime.UtcNow;
        var delta = _vm._lastWatchHistoryUpdateUtc == DateTime.MinValue 
            ? TimeSpan.Zero 
            : nowUtc - _vm._lastWatchHistoryUpdateUtc;
            
        _vm._lastWatchHistoryUpdateUtc = nowUtc;

        await FlushWatchHistoryAsync(force: false, incrementDelta: delta);
    }

    public async Task FlushWatchHistoryAsync(bool force, TimeSpan? incrementDelta = null)
    {
        if (_vm.WatchHistoryService == null || _vm.CurrentProfileId == null || _vm.CurrentChannel == null)
        {
            return;
        }

        if (!force && !_vm.IsPlaying)
        {
            return;
        }

        try
        {
            var isEpisodePlayback = _vm.CurrentChannel.Type == ChannelType.Series && _vm.CurrentEpisode?.Id > 0;
            var channelId = !isEpisodePlayback && _vm.CurrentChannel.Id > 0 ? _vm.CurrentChannel.Id : (int?)null;
            var livePosition = Math.Max(0, _vm.VideoPlayerService.Position);
            var currentPosition = TimeSpan.FromSeconds(Math.Max(_vm.Position, livePosition));
            var currentDuration = _vm.Duration > 0 ? TimeSpan.FromSeconds(_vm.Duration) : (TimeSpan?)null;
            var isCompleted = IsEpisodeCompleted(_vm.Duration, _vm.Position);

            var sessionDurationSeconds = _vm._sessionPlaybackStartTimeUtc == DateTime.MinValue 
                ? 0 
                : (DateTime.UtcNow - _vm._sessionPlaybackStartTimeUtc).TotalSeconds;

            // ── Sorun 2 Koruması: Başarısız resume sonrası near-zero save'i engelle ──
            // Kullanıcı "Devam Et" dediğinde stream yüklenmezse Position ~0 olur.
            // force=true ile yapılan Stop() çağrısı bu 0 değerini veritabanına yazarak
            // eski ilerlemeyi silerdi. Bu guard, sadece force modunda ve oynatma hiç
            // başlamamışsa kaydı tamamen atlar.
            if (force && !_vm.IsPlaying && currentPosition.TotalSeconds < 2 && sessionDurationSeconds < 5)
            {
                _vm.LogDebug($"FlushWatchHistoryAsync: Force-skip near-zero save (playback never started). Session: {sessionDurationSeconds:F1}s, Pos: {currentPosition.TotalSeconds:F1}s");
                return;
            }

            // ── Sorun 1 Koruması: Baştan Başla durumunda eski progress'i koru ──
            // Kullanıcı "Baştan Başla" dediğinde _isStartingOver=true olur.
            // Eski _oldResumePosition değerine ulaşana kadar yeni progress
            // kaydedilmez. Bu sayede eski kaldığı nokta korunur, kullanıcı
            // yanlışlıkla bastıysa veya kısa süreli giriş yaptıysa progress kaybolmaz.
            if (_vm._isStartingOver && _vm._oldResumePosition > 0 && currentPosition.TotalSeconds < _vm._oldResumePosition)
            {
                _vm.LogDebug($"FlushWatchHistoryAsync: Safety Net v3 (StartOver). Preserving old position {_vm._oldResumePosition:F1}s, current: {currentPosition.TotalSeconds:F1}s, session: {sessionDurationSeconds:F1}s");
                return;
            }

            var safetyThreshold = _vm._isStartingOver ? 60 : 15;

            if (currentPosition.TotalSeconds < safetyThreshold && sessionDurationSeconds < safetyThreshold)
            {
                _vm.LogDebug($"FlushWatchHistoryAsync: Skipping early near-zero save (Safety Net). StartingOver: {_vm._isStartingOver}, Session: {sessionDurationSeconds:F1}s, Pos: {currentPosition.TotalSeconds:F1}s");
                return;
            }

            await _vm.WatchHistoryService.TrackWatchAsync(
                _vm.CurrentProfileId.Value,
                channelId,
                isEpisodePlayback ? _vm.CurrentEpisode!.Id : null,
                currentPosition,
                isCompleted,
                currentDuration,
                incrementDelta,
                /* allowReset: false - Baştan Başla durumunda eski completed
                 * durumunu korumak için allowReset asla true olmamalı.       
                 * Eski mekanizma _isStartingOver'i allowReset olarak geçiyordu,
                 * bu da eski tamamlanma durumunu sıfırlıyordu.                 */
                allowReset: false
            );

            if (isEpisodePlayback && _vm.CurrentEpisode != null)
            {
                var finalCompleted = _vm.CurrentEpisode.IsCompleted || isCompleted;
                var watchedAtUtc = DateTime.UtcNow;
                _vm.DispatcherService.BeginInvoke(() =>
                {
                    try
                    {
                        _vm.CurrentEpisode.LastWatched = watchedAtUtc;
                        _vm.CurrentEpisode.WatchedPosition = finalCompleted && currentDuration.HasValue
                            ? currentDuration.Value
                            : currentPosition;
                        _vm.CurrentEpisode.IsCompleted = finalCompleted;
                        if (currentDuration.HasValue && currentDuration.Value.TotalSeconds > 0)
                        {
                            _vm.CurrentEpisode.Duration = currentDuration.Value;
                        }

                        RefreshEpisodeBrowserContext(_vm._currentSeriesContext);
                        _vm.RaiseEpisodeProgressUpdatedEvent(_vm.CurrentEpisode);

                        // Update LastWatchedEpisodeAt on the Series for O(n) history sorting
                        if (_vm._currentSeriesContext?.Id > 0)
                        {
                            _vm.MainViewModel.UpdateSeriesLastWatchedEpisodeAt(_vm._currentSeriesContext.Id, watchedAtUtc);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Episode UI progress update error: {ex.Message}");
                    }
                });
            }
            else if (!isEpisodePlayback && _vm.CurrentChannel is { Type: ChannelType.VOD } vodChannel)
            {
                var finalCompleted = vodChannel.IsCompleted || isCompleted;
                var watchedAtUtc = DateTime.UtcNow;
                _vm.DispatcherService.BeginInvoke(() =>
                {
                    try
                    {
                        vodChannel.LastWatched = watchedAtUtc;
                        vodChannel.WatchedPosition = finalCompleted && currentDuration.HasValue
                            ? currentDuration.Value
                            : currentPosition;
                        vodChannel.IsCompleted = finalCompleted;
                        if (currentDuration.HasValue && currentDuration.Value.TotalSeconds > 0)
                        {
                            vodChannel.Duration = currentDuration.Value;
                        }

                        _vm.MainViewModel.RefreshContinueWatchingRail(episodeContinueDirty: false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"VOD UI progress update error: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Watch history tracking error: {ex.Message}");
        }
    }

    public PlaybackExitSnapshot CreatePlaybackExitSnapshot()
    {
        var channel = _vm.CurrentChannel;
        var episode = _vm.CurrentEpisode;
        var position = _vm.Position > 0 ? _vm.Position : Math.Max(0, _vm.VideoPlayerService.Position);
        var duration = _vm.Duration > 0 ? _vm.Duration : 0;

        return new PlaybackExitSnapshot
        {
            ProfileId = _vm.CurrentProfileId,
            Channel = channel,
            Episode = episode,
            PositionSeconds = position,
            DurationSeconds = duration,
            IsCompleted = IsEpisodeCompleted(duration, position),
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task FlushPlaybackExitSnapshotAsync(PlaybackExitSnapshot snapshot)
    {
        if (_vm.WatchHistoryService == null || snapshot.ProfileId == null || snapshot.Channel == null)
            return;

        try
        {
            var channelId = snapshot.Channel.Id > 0 ? snapshot.Channel.Id : (int?)null;
            var currentPosition = TimeSpan.FromSeconds(Math.Max(0, snapshot.PositionSeconds));
            var currentDuration = snapshot.DurationSeconds > 0
                ? TimeSpan.FromSeconds(snapshot.DurationSeconds)
                : (TimeSpan?)null;

            await _vm.WatchHistoryService.TrackWatchAsync(
                snapshot.ProfileId.Value,
                snapshot.Channel.Type == ChannelType.Series && snapshot.Episode?.Id > 0 ? null : channelId,
                snapshot.Episode?.Id,
                currentPosition,
                snapshot.IsCompleted,
                currentDuration,
                incrementDelta: null,
                allowReset: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlaybackExitSnapshot flush error: {ex.Message}");
        }
    }
}
