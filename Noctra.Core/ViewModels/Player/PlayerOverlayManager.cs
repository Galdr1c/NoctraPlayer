using System;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;
using static Noctra.ViewModels.PlayerViewModel;

namespace Noctra.ViewModels;

public class PlayerOverlayManager
{
    private readonly PlayerViewModel _vm;

    public PlayerOverlayManager(PlayerViewModel vm)
    {
        _vm = vm;
    }

    public async Task ShowOverlayMessageAsync(string message, int durationMs = 2000)
    {
        _vm._overlayMessageCts?.Cancel();
        _vm._overlayMessageCts = new CancellationTokenSource();
        var token = _vm._overlayMessageCts.Token;

        _vm.OverlayMessage = message;
        _vm.IsOverlayMessageVisible = true;

        try
        {
            await Task.Delay(durationMs, token);
            if (!token.IsCancellationRequested)
                _vm.IsOverlayMessageVisible = false;
        }
        catch (TaskCanceledException) { }
    }

    public void RestartAutoHideTimer()
    {
        _vm._autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _vm.IsVisible = true;
        if (CanAutoHideOverlay())
        {
            _vm._autoHideTimer.Change(4000, Timeout.Infinite);
        }
    }

    public bool CanAutoHideOverlay()
    {
        return _vm.IsPlaying
            && !_vm.IsLocked
            && !_vm.IsClosingPlayer
            && !_vm._isUserSeeking
            && !_vm.IsDragging
            && !_vm.IsResizing
            && !_vm.IsBuffering
            && !_vm.IsOverlayMessageVisible
            && !_vm.IsActionsPanelOpen
            && !_vm.IsAudioSettingsOpen
            && !_vm.IsQualitySettingsOpen
            && !_vm.IsSubtitleAppearanceSettingsOpen
            && !_vm.IsEpisodesPanelOpen
            && !_vm.IsInfoPanelOpen
            && !_vm.IsSleepTimerPanelOpen
            && !_vm.IsEpgPanelOpen
            && !_vm.IsNextEpisodePromptVisible;
    }

    public void ShowOverlay() => RestartAutoHideTimer();

    public void ToggleLock()
    {
        if (_vm.IsLocked)
        {
            _vm.Unlock();
            return;
        }

        _vm.IsLocked = true;
        _vm.ShowLockIndicatorBriefly();
        RestartAutoHideTimer();
    }

    public void OpenAudioSettings()
    {
        _vm.LogDebug("UI Action: OpenAudioSettings clicked");
        _vm.OpenChildPanel(MobilePanelState.Audio);
        if (_vm.IsAudioSettingsOpen)
        {
            _vm.UpdateMediaInfo();
            _ = _vm.RefreshTracksWithRetryAsync();
        }
    }

    public void OpenQualitySettings()
    {
        _vm.LogDebug("UI Action: OpenQualitySettings clicked");
        _vm.OpenChildPanel(MobilePanelState.Quality);
    }

    public void OpenSubtitleAppearanceSettings()
    {
        _vm.LogDebug("UI Action: OpenSubtitleAppearanceSettings clicked");
        _vm.OpenChildPanel(MobilePanelState.SubtitleAppearance);
    }

    public void OpenInfoPanel()
    {
        _vm.LogDebug("UI Action: OpenInfoPanel clicked");
        if (_vm.IsDownloadedPlayback)
        {
            return;
        }

        _vm.OpenChildPanel(MobilePanelState.Info);
    }

    public void ClosePanels()
    {
        _vm.LogDebug("UI Action: ClosePanels clicked");

        if (_vm.IsResumeDialogVisible)
        {
            _vm.CancelResumeDialog();
            return;
        }

        if (_vm.IsMobileDetailPanelOpen)
        {
            _vm.SetMobilePanelState(MobilePanelState.None);
            RestartAutoHideTimer();
            return;
        }

        if (_vm.IsNextEpisodePromptVisible)
        {
            _vm.EpisodeNavigator.CancelNextEpisode();
            return;
        }

        _vm.SetMobilePanelState(MobilePanelState.None);
        RestartAutoHideTimer();
    }

    public void ShowSleepTimerMenu()
    {
        _vm.LogDebug("UI Action: ShowSleepTimerMenu clicked");
        _vm.OpenChildPanel(MobilePanelState.Sleep);
    }

    public void SetSleepTimer(PlayerViewModel.SleepTimerOption mode)
    {
        if (mode != PlayerViewModel.SleepTimerOption.Off && _vm.IsLiveContent)
        {
            _ = ShowOverlayMessageAsync(_vm.LocalizationService.GetString("Player.Error.SleepTimerLive"));
            return;
        }

        if (mode != PlayerViewModel.SleepTimerOption.Off && !_vm.IsPremium) return;

        CancelSleepTimer();

        _vm.SleepTimerMode = mode;
        switch (mode)
        {
            case PlayerViewModel.SleepTimerOption.Minutes15:
                StartCountdownTimer(TimeSpan.FromMinutes(15));
                break;
            case PlayerViewModel.SleepTimerOption.Minutes30:
                StartCountdownTimer(TimeSpan.FromMinutes(30));
                break;
            case PlayerViewModel.SleepTimerOption.Minutes60:
                StartCountdownTimer(TimeSpan.FromMinutes(60));
                break;
            case PlayerViewModel.SleepTimerOption.EndOfEpisode:
                _vm.SleepTimerCountdown = _vm.LocalizationService.GetString(_vm.IsSeriesContent ? "Player.Sleep.EndOfEpisode" : "Player.Sleep.EndOfMovie");
                break;
        }
    }

    public void CancelSleepTimer()
    {
        _vm._sleepCountdownCts?.Cancel();
        _vm._sleepCountdownCts?.Dispose();
        _vm._sleepCountdownCts = null;

        _vm.SleepTimerMode = PlayerViewModel.SleepTimerOption.Off;
        _vm.SleepTimerCountdown = string.Empty;
    }

    public void StartCountdownTimer(TimeSpan duration)
    {
        _vm._sleepCountdownCts = new CancellationTokenSource();
        var token = _vm._sleepCountdownCts.Token;
        var endsAt = DateTime.UtcNow + duration;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var remaining = endsAt - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero)
                {
                    _vm.DispatcherService.BeginInvoke(() =>
                    {
                        _vm.SleepTimerCountdown = "00:00";
                        TriggerSleepShutdown();
                    });
                    return;
                }

                var display = remaining.TotalHours >= 1
                    ? remaining.ToString(@"h\:mm\:ss")
                    : remaining.ToString(@"mm\:ss");

                _vm.DispatcherService.BeginInvoke(() => _vm.SleepTimerCountdown = display);

                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { return; }
            }
        }, token);
    }

    public void TriggerSleepShutdown()
    {
        _vm.VideoPlayerService.Stop();
        _vm.SleepTimerMode = PlayerViewModel.SleepTimerOption.Off;
        _vm.SleepTimerCountdown = string.Empty;

        _ = ShowOverlayMessageAsync(_vm.LocalizationService.GetString("Player.Status.SleepTimerStopped"));
    }
}
