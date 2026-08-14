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
        if (Enum.TryParse<SubtitleTextSize>(size, ignoreCase: true, out var parsed))
        {
            _vm.SubtitleTextSize = parsed;
            return;
        }

        if (int.TryParse(size, out var legacyValue))
        {
            _vm.SubtitleTextSize = SubtitleAppearanceDefaults.ResolveTextSize(legacyValue);
        }
    }

    public void SetSubtitleBackground(string opacity)
    {
        _vm.LogDebug($"UI Action: SetSubtitleBackground clicked (Opacity={opacity})");
        if (int.TryParse(opacity, out var parsed))
        {
            _vm.SubtitleBackgroundOpacity = SubtitleAppearanceDefaults.NormalizeOpacityPercent(parsed);
        }
    }

    public void SetSubtitlePosition(string position)
    {
        _vm.LogDebug($"UI Action: SetSubtitlePosition clicked (Position={position})");
        if (Enum.TryParse<SubtitleVerticalPosition>(position, ignoreCase: true, out var parsed))
        {
            _vm.SubtitlePosition = parsed;
            return;
        }

        // Eski XAML komut parametreleriyle geriye uyumluluk.
        if (int.TryParse(position, out var legacyMargin))
        {
            _vm.SubtitlePosition = SubtitleAppearanceDefaults.ResolveLegacyPosition(legacyMargin);
        }
    }

    public void CycleVideoFillMode()
    {
        var current = _vm.VideoFillMode;
        var next = current switch
        {
            Noctra.Models.VideoScaleMode.Fit => Noctra.Models.VideoScaleMode.Fill,
            Noctra.Models.VideoScaleMode.Fill => Noctra.Models.VideoScaleMode.Stretch,
            _ => Noctra.Models.VideoScaleMode.Fit
        };

        _vm.LogDebug($"UI Action: CycleVideoFillMode clicked (Current={current} -> Next={next})");
        _vm.VideoFillMode = next;
        _vm.ApplyVideoFillMode();

        var messageKey = GetFillModeKey(next);
        _ = _vm.OverlayManager.ShowOverlayMessageAsync(_vm.LocalizationService.GetString(messageKey));
        _vm.RestartAutoHideTimer();
    }

    public string GetFillModeText(Noctra.Models.VideoScaleMode mode)
        => _vm.LocalizationService.GetString(GetFillModeKey(mode));

    private static string GetFillModeKey(Noctra.Models.VideoScaleMode mode) => mode switch
    {
        Noctra.Models.VideoScaleMode.Fit => "Player.FillMode.Fit",
        Noctra.Models.VideoScaleMode.Fill => "Player.FillMode.Fill",
        Noctra.Models.VideoScaleMode.Stretch => "Player.FillMode.Stretch",
        _ => "Player.FillMode.Fit"
    };

    public void OnSettingsChanged()
    {
        _vm.DispatcherService.BeginInvoke(() =>
        {
            if (_vm.SettingsService?.Settings != null)
            {
                _vm.SubtitleTextSize = _vm.SettingsService.Settings.SubtitleTextSize;
                _vm.SubtitleBackgroundOpacity = SubtitleAppearanceDefaults.NormalizeOpacityPercent(_vm.SettingsService.Settings.SubtitleBackgroundOpacity);

                _vm.SubtitlePosition = _vm.SettingsService.Settings.SubtitlePosition;

                // Sync volume and mute states!
                _vm.Volume = _vm.SettingsService.Settings.DefaultVolume;
                _vm.IsMuted = _vm.SettingsService.Settings.IsMuted;
                _vm.EpisodeNavigator.OnAutoPlayPreferenceChanged();
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
            _vm.RaisePropertyChanged(nameof(PlayerViewModel.NextEpisodeCountdownText));
            _vm.QualityMonitor.UpdateStreamInfoFromQuality();
        });
    }
}
