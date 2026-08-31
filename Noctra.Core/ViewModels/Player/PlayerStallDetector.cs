using System;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;

namespace Noctra.ViewModels;

public class PlayerStallDetector
{
    private readonly PlayerViewModel _vm;
    private readonly object _startupRecoveryGate = new();
    private int _startupRecoveryRequestVersion = -1;
    private int _startupRecoveryAttempts;

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

    public async Task EnsurePlaybackHealthAsync(Channel channel, int requestVersion, double? startPosition = null)
    {
        const int retryCountdownSeconds = 5;

        for (int remaining = retryCountdownSeconds; remaining > 0; remaining--)
        {
            await Task.Delay(1000);

            if (IsHealthCheckCancelled(channel, requestVersion))
                return;

            if (_vm.IsPlaying)
            {
                ResetStartupRecovery(requestVersion);
                await InvokeIfCurrentAsync(
                    channel,
                    requestVersion,
                    () => _vm.PlayerLoadingWarningMessage = string.Empty);
                return;
            }

            if (_vm._suppressBufferShieldForSeek)
            {
                await InvokeIfCurrentAsync(
                    channel,
                    requestVersion,
                    () => _vm.PlayerLoadingWarningMessage = string.Empty);
                return;
            }

            var msg = string.Format(_vm.LocalizationService.GetString("Player.Status.RetryInSeconds"), remaining);
            if (!await InvokeIfCurrentAsync(
                channel,
                requestVersion,
                () => _vm.PlayerLoadingWarningMessage = msg,
                requireNotPlaying: true))
            {
                return;
            }
        }

        if (IsHealthCheckCancelled(channel, requestVersion) || _vm.IsPlaying)
            return;

        if (TryReserveStartupRecovery(requestVersion))
        {
            if (!await InvokeIfCurrentAsync(
                channel,
                requestVersion,
                () =>
                {
                    _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Status.Reconnecting");
                    _vm.ConnectionStatus = _vm.LocalizationService.GetString("Player.Status.Reconnecting");
                    _vm.IsBuffering = true;
                    _vm.BufferingProgress = 0;
                },
                requireNotPlaying: true))
            {
                return;
            }

            try
            {
                await _vm.PlaybackController.PlayChannelAsync(
                    channel,
                    startPosition,
                    existingRequestVersion: requestVersion);
            }
            catch (Exception ex)
            {
                _vm.LogDebug($"Startup recovery attempt failed: {ex.Message}");
                if (!IsHealthCheckCancelled(channel, requestVersion))
                {
                    await EnsurePlaybackHealthAsync(channel, requestVersion, startPosition);
                }
            }
            return;
        }

        if (!await InvokeIfCurrentAsync(
            channel,
            requestVersion,
            () => _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Warning.SlowConnection"),
            requireNotPlaying: true))
        {
            return;
        }

        _vm._unreachableWarningTimer?.Dispose();
        _vm._unreachableWarningTimer = new Timer(_ =>
        {
            if (_vm.IsPlaying || !_vm.IsVisible) return;
            _vm.DispatcherService.BeginInvoke(() =>
            {
                if (IsHealthCheckCancelled(channel, requestVersion) || _vm.IsPlaying || !_vm.IsVisible)
                {
                    return;
                }

                _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Warning.Unreachable");
            });
        }, null, 15000, Timeout.Infinite);

        await Task.Delay(7000);

        if (IsHealthCheckCancelled(channel, requestVersion) || _vm.IsPlaying)
            return;

        await InvokeIfCurrentAsync(
            channel,
            requestVersion,
            () => _vm.PlayerLoadingWarningMessage = _vm.LocalizationService.GetString("Player.Warning.Unreachable"),
            requireNotPlaying: true);
    }

    private Task<bool> InvokeIfCurrentAsync(
        Channel channel,
        int requestVersion,
        Action action,
        bool requireNotPlaying = false)
        => _vm.DispatcherService.InvokeAsync(() =>
        {
            if (IsHealthCheckCancelled(channel, requestVersion) ||
                (requireNotPlaying && _vm.IsPlaying))
            {
                return false;
            }

            action();
            return true;
        });

    private bool TryReserveStartupRecovery(int requestVersion)
    {
        lock (_startupRecoveryGate)
        {
            if (_startupRecoveryRequestVersion != requestVersion)
            {
                _startupRecoveryRequestVersion = requestVersion;
                _startupRecoveryAttempts = 0;
            }

            if (_startupRecoveryAttempts >= 4)
            {
                return false;
            }

            _startupRecoveryAttempts++;
            return true;
        }
    }

    private void ResetStartupRecovery(int requestVersion)
    {
        lock (_startupRecoveryGate)
        {
            if (_startupRecoveryRequestVersion == requestVersion)
            {
                _startupRecoveryAttempts = 0;
            }
        }
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
