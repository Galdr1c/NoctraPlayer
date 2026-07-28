using System;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.ViewModels;

public class PlayerSettingsAdapter
{
    private readonly PlayerViewModel _vm;

    public PlayerSettingsAdapter(PlayerViewModel vm)
    {
        _vm = vm;
    }

    public void SetSubtitleSize(string size)
    {
        _vm.LogDebug($"UI Action: SetSubtitleSize clicked (Size={size})");
        _vm.SubtitleFontSize = int.Parse(size);
    }

    public void SetSubtitleBackground(string opacity)
    {
        _vm.LogDebug($"UI Action: SetSubtitleBackground clicked (Opacity={opacity})");
        _vm.SubtitleBackgroundOpacity = int.Parse(opacity);
    }

    public void SetSubtitlePosition(string margin)
    {
        _vm.LogDebug($"UI Action: SetSubtitlePosition clicked (Margin={margin})");
        _vm.SubtitleMargin = int.Parse(margin);
    }

    public void CycleVideoFillMode()
    {
        var current = _vm.VideoFillMode;
        var next = current switch
        {
            PlayerViewModel.FillMode.Fit => PlayerViewModel.FillMode.Fill,
            PlayerViewModel.FillMode.Fill => PlayerViewModel.FillMode.Stretch,
            PlayerViewModel.FillMode.Stretch => PlayerViewModel.FillMode.Original,
            _ => PlayerViewModel.FillMode.Fit
        };

        _vm.LogDebug($"UI Action: CycleVideoFillMode clicked (Current={current} -> Next={next})");
        _vm.VideoFillMode = next;
        _vm.ApplyVideoFillMode();
        
        var messageKey = next switch
        {
            PlayerViewModel.FillMode.Fit => "Player.FillMode.Fit",
            PlayerViewModel.FillMode.Fill => "Player.FillMode.Fill",
            PlayerViewModel.FillMode.Stretch => "Player.FillMode.Stretch",
            PlayerViewModel.FillMode.Original => "Player.FillMode.Original",
            _ => "Player.FillMode.Fit"
        };
        _ = _vm.OverlayManager.ShowOverlayMessageAsync(_vm.LocalizationService.GetString(messageKey));
        _vm.RestartAutoHideTimer();
    }

    public void OnSettingsChanged()
    {
        _vm.DispatcherService.BeginInvoke(() =>
        {
            if (_vm.SettingsService?.Settings != null)
            {
                _vm.SubtitleFontSize = _vm.SettingsService.Settings.SubtitleFontSize;
                _vm.SubtitleBackgroundOpacity = _vm.SettingsService.Settings.SubtitleBackgroundOpacity;
                _vm.SubtitleMargin = _vm.SettingsService.Settings.SubtitleMargin;
                
                // Sync volume and mute states!
                _vm.Volume = _vm.SettingsService.Settings.DefaultVolume;
                _vm.IsMuted = _vm.SettingsService.Settings.IsMuted;
            }
        });
    }

    public void OnLanguageChanged()
    {
        _vm.DispatcherService.BeginInvoke(() =>
        {
            if (_vm.SleepTimerMode == PlayerViewModel.SleepTimerOption.EndOfEpisode)
            {
                _vm.SleepTimerCountdown = _vm.LocalizationService.GetString(_vm.IsSeriesContent ? "Player.Sleep.EndOfEpisode" : "Player.Sleep.EndOfMovie");
            }

            _vm.RaisePropertyChanged(nameof(PlayerViewModel.SleepTimerLabel));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.EndOfContentText));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.EndOfContentDescription));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.QualityResolutionText));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.QualityFpsText));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.QualityVideoCodecText));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.QualityVideoBitrateText));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.QualityAudioText));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.PlayPauseAccessibilityName));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.MuteAccessibilityName));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.LiveFavoriteAccessibilityName));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.LockAccessibilityName));
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.TimelineAccessibilityName));
            _vm.QualityMonitor.UpdateStreamInfoFromQuality();
        });
    }
}
