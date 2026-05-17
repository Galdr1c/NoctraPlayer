using System;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;

namespace Noctra.ViewModels;

public class PlayerStallDetector
{
    private readonly PlayerViewModel _vm;

    public PlayerStallDetector(PlayerViewModel vm)
    {
        _vm = vm;
    }

    public async Task AutoRecoverPrematureEndAsync(double lastPos)
    {
        _vm.LogDebug($"AutoRecoverPrematureEnd: Reconnecting silently to let proxy clear...");
        _vm.IsBuffering = true;

        var requestVersion = _vm._playRequestVersion;
        await Task.Delay(500);

        if (_vm.CurrentChannel == null || _vm._isContentTransitioning || requestVersion != _vm._playRequestVersion) return;

        var compensatedPos = _vm.IsLiveContent ? lastPos : lastPos + 2.5;

        _vm.LogDebug($"AutoRecoverPrematureEnd: Executing ResumePlaybackAsync from {compensatedPos}s");
        _vm._lastPausedPosition = compensatedPos;
        _vm._isPlaybackEnded = false;
        await _vm.PlaybackController.ResumePlaybackAsync(_vm.CurrentChannel.StreamUrl, false);
    }

    public async Task EnsurePlaybackHealthAsync(Channel channel, int requestVersion)
    {
        const int retryCountdownSeconds = 5;
        const int maxAttempts = 4;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            for (int remaining = retryCountdownSeconds; remaining > 0; remaining--)
            {
                await Task.Delay(1000);

                if (IsHealthCheckCancelled(channel, requestVersion))
                    return;

                if (_vm.IsPlaying)
                {
                    _vm.DispatcherService.Invoke(() => _vm.PlayerLoadingWarningMessage = string.Empty);
                    return;
                }

                if (_vm._suppressBufferShieldForSeek)
                {
                    _vm.DispatcherService.Invoke(() => _vm.PlayerLoadingWarningMessage = string.Empty);
                    return;
                }

                var msg = string.Format(_vm.LocalizationService.GetString("Player.Status.RetryInSeconds"), remaining);
                _vm.DispatcherService.Invoke(() => _vm.PlayerLoadingWarningMessage = msg);
            }

            if (IsHealthCheckCancelled(channel, requestVersion) || _vm.IsPlaying || _vm._suppressBufferShieldForSeek)
                return;

            _vm.DispatcherService.Invoke(() =>
            {
                _vm.PlayerLoadingWarningMessage = string.Format(_vm.LocalizationService.GetString("Player.Status.ReconnectingFormat"), attempt + 1, maxAttempts);
                _vm.ConnectionStatus = _vm.LocalizationService.GetString("Player.Status.Reconnecting");
                _vm.IsBuffering = true;
                _vm.BufferingProgress = 0;
            });

            _vm.VideoPlayerService.Stop();
            await Task.Delay(200);

            if (IsHealthCheckCancelled(channel, requestVersion))
                return;

            try
            {
                var resolvedUrl = await _vm.ContentDownloadService.ResolvePlayableUrlAsync(channel.StreamUrl);
                await _vm.VideoPlayerService.PlayAsync(resolvedUrl);

                _vm.DispatcherService.Invoke(() => _vm.PlayerLoadingWarningMessage = string.Empty);
            }
            catch
            {
                // Proceed to next attempt
            }
        }

        if (IsHealthCheckCancelled(channel, requestVersion) || _vm.IsPlaying)
            return;

        _vm.DispatcherService.Invoke(() =>
            _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Warning.SlowConnection"));

        _vm._unreachableWarningTimer?.Dispose();
        _vm._unreachableWarningTimer = new Timer(_ =>
        {
            if (_vm.IsPlaying || !_vm.IsVisible) return;
            _vm.DispatcherService.Invoke(() => _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Warning.Unreachable"));
        }, null, 15000, Timeout.Infinite);

        await Task.Delay(7000);

        if (IsHealthCheckCancelled(channel, requestVersion) || _vm.IsPlaying)
            return;

        _vm.DispatcherService.Invoke(() =>
            _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Warning.Unreachable"));
    }

    public bool IsHealthCheckCancelled(Channel channel, int requestVersion)
    {
        if (requestVersion != _vm._playRequestVersion || _vm.CurrentChannel?.Id != channel.Id)
            return true;
        if (_vm._isIntentionallyPaused || Interlocked.CompareExchange(ref _vm._isPlayPauseInProgress, 0, 0) == 1)
            return true;
        if (_vm._livePauseRequiresHardRestart)
            return true;
        
        return false;
    }

    public async Task MonitorLivePlaybackHealthAsync()
    {
        // INTENTIONAL KILLSWITCH: VLC does not reliably fire PositionChanged for live streams,
        // causing this monitor to falsely detect a stall and restart exactly every 5 seconds.
        // We now rely on EndReached/EncounteredError combined with AutoRecoverPrematureEndAsync.
        await Task.CompletedTask;
    }

    public void OnNetworkStatusChanged(object? sender, string status)
    {
        UpdateNetworkStatus(status);
    }

    public void UpdateNetworkStatus(string status)
    {
        _vm.NetworkStatus = status;
        _vm.NetworkIcon = status switch
        {
            "Ethernet" => "Ethernet",
            "Wi-Fi" => "Wifi",
            "Cellular" => "SignalCellular4Bar",
            "Offline" => "WifiOff",
            _ => "Web"
        };
    }
}
