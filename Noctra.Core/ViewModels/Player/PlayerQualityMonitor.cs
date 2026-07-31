using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Noctra.Models;

namespace Noctra.ViewModels;

public class PlayerQualityMonitor
{
    private readonly PlayerViewModel _vm;

    public PlayerQualityMonitor(PlayerViewModel vm)
    {
        _vm = vm;
    }

    public void OnVideoPlayerServiceQualityDetected(object? s, StreamQualityInfo quality)
    {
        _vm.DispatcherService.Invoke(() =>
        {
            _vm.StreamQuality = quality;
            UpdateStreamInfoFromQuality();
        });
    }

    public void UpdateStreamInfoFromQuality()
    {
        if (_vm.StreamQuality == null)
        {
            _vm.StreamInfo = _vm.LocalizationService.GetString("Player.Status.QualityDetecting");
            return;
        }

        _vm.StreamInfo = _vm.StreamQuality.ResolutionLabel;
    }

    public void OnVideoPlayerServiceErrorOccurred(object? s, string errorMessage)
    {
        _vm.DispatcherService.Invoke(() =>
        {
            _vm.LogDebug($"VM: VideoPlayerService Error: {errorMessage}");
        });
    }

    public async Task RefreshTracksWithRetryAsync()
    {
        var delays = new[] { 250, 800, 1600, 3000 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay);
            if (!_vm.IsPlaying || _vm.CurrentChannel == null)
            {
                return;
            }

            _vm.DispatcherService.Invoke(UpdateMediaInfo);

            if (_vm.AudioTracks.Any(t => t.Id >= 0) && (_vm.SubtitleTracks.Any(t => t.Id >= 0) || delay >= 1600))
            {
                return;
            }
        }
    }

    public void UpdateMediaInfo()
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        UpdateDurationFromService(force: true);

        var offText = _vm.LocalizationService.GetString("Player.Track.Off");
        var audioFallbackFormat = _vm.LocalizationService.GetString("Player.Track.AudioFallback");
        var subtitleFallbackFormat = _vm.LocalizationService.GetString("Player.Track.SubtitleFallback");

        var audioTracks = _vm.VideoPlayerService.AudioTracks
            .Where(t => t.Id >= 0 && !IsDisabledTrackLabel(t.Name))
            .Select(t => BuildTrackOption(t.Id, t.Name, string.Format(audioFallbackFormat, t.Id)))
            .ToList();

        var subtitleTracks = _vm.VideoPlayerService.SubtitleTracks
            .Where(t => t.Id >= 0 && !IsDisabledTrackLabel(t.Name))
            .Select(t => BuildTrackOption(t.Id, t.Name, string.Format(subtitleFallbackFormat, t.Id)))
            .ToList();

        subtitleTracks.Insert(0, new PlayerViewModel.TrackOption(-1, offText));
        swTotal.Stop();
        System.Diagnostics.Debug.WriteLine($"[PVM] UpdateMediaInfo: collections={swTotal.ElapsedMilliseconds}ms");

        var swInvoke = System.Diagnostics.Stopwatch.StartNew();
        _vm.DispatcherService.Invoke(() => 
        {
            _vm.AudioTracks.Clear();
            foreach (var t in audioTracks) _vm.AudioTracks.Add(t);

            _vm.SubtitleTracks.Clear();
            foreach (var t in subtitleTracks) _vm.SubtitleTracks.Add(t);
        });

        _vm.DispatcherService.Invoke(() => _vm.RaiseTrackSelectionPropertiesChanged());
        swInvoke.Stop();
        System.Diagnostics.Debug.WriteLine($"[PVM] UpdateMediaInfo: dispatcher={swInvoke.ElapsedMilliseconds}ms");

        if (!_vm._isPreferenceApplied)
        {
            var remembered = _vm.TryApplyRememberedTrackSelection(audioTracks, subtitleTracks);
            ApplyDefaultTracks(
                _vm.VideoPlayerService.AudioTracks,
                _vm.VideoPlayerService.SubtitleTracks,
                skipAudio: remembered.AudioApplied,
                skipSubtitle: remembered.SubtitleApplied);
        }
        else
        {
            if (_vm.SelectedAudioTrack >= 0 && audioTracks.Any(t => t.Id == _vm.SelectedAudioTrack))
            {
                _vm.VideoPlayerService.SetAudioTrack(_vm.SelectedAudioTrack);
            }

            if (_vm.SelectedSubtitleTrack >= -1 && subtitleTracks.Any(t => t.Id == _vm.SelectedSubtitleTrack))
            {
                _vm.VideoPlayerService.SetSubtitleTrack(_vm.SelectedSubtitleTrack);
            }
        }
    }

    public void ApplyDefaultTracks(
        IReadOnlyList<(int Id, string? Name)> audioTracks,
        IReadOnlyList<(int Id, string? Name)> subtitleTracks,
        bool skipAudio = false,
        bool skipSubtitle = false)
    {
        var settings = _vm.SettingsService.Settings;

        if (!skipAudio)
        {
            var targetAudioId = FindBestTrackMatch(audioTracks, settings.PreferredAudioLanguage);
            if (targetAudioId >= 0)
            {
                _vm.VideoPlayerService.SetAudioTrack(targetAudioId);
                _vm.SelectedAudioTrack = targetAudioId;
            }
            else if (_vm.SelectedAudioTrack < 0 && audioTracks.Any(t => t.Id >= 0))
            {
                // Android/LibVLC usually starts with the first audio track. Reflect that in the UI
                // when there is no preferred-language match instead of leaving selection blank.
                _vm.SelectedAudioTrack = audioTracks.First(t => t.Id >= 0).Id;
            }
        }

        if (!skipSubtitle)
        {
            if (settings.SubtitleEnabled)
            {
                var targetSubtitleId = FindBestTrackMatch(subtitleTracks, settings.SubtitleLanguage);
                if (targetSubtitleId >= 0)
                {
                    _vm.VideoPlayerService.SetSubtitleTrack(targetSubtitleId);
                    _vm.SelectedSubtitleTrack = targetSubtitleId;
                }
                else
                {
                    _vm.VideoPlayerService.SetSubtitleTrack(-1);
                    _vm.SelectedSubtitleTrack = -1;
                }
            }
            else
            {
                _vm.VideoPlayerService.SetSubtitleTrack(-1);
                _vm.SelectedSubtitleTrack = -1;
            }
        }

        _vm._isPreferenceApplied = true;
        _vm.DispatcherService.Invoke(() => _vm.RaiseTrackSelectionPropertiesChanged());
    }

    public static int FindBestTrackMatch(IReadOnlyList<(int Id, string? Name)> tracks, string langCode)
    {
        if (tracks == null || tracks.Count == 0 || string.IsNullOrWhiteSpace(langCode)) return -1;

        foreach (var track in tracks)
        {
            if (track.Id < 0 || string.IsNullOrWhiteSpace(track.Name)) continue;
            
            if (track.Name.Contains($"({langCode})", StringComparison.OrdinalIgnoreCase) || 
                track.Name.Contains($"[{langCode}]", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(track.Name, $@"\b{langCode}\b", RegexOptions.IgnoreCase))
            {
                return track.Id;
            }
        }

        var fullName = langCode.ToLower() switch
        {
            "tr" => "turkish",
            "en" => "english",
            "de" => "german",
            "fr" => "french",
            "es" => "spanish",
            "it" => "italian",
            "pt" => "portuguese",
            "ru" => "russian",
            "ar" => "arabic",
            "nl" => "dutch",
            _ => null
        };

        if (fullName != null)
        {
            foreach (var track in tracks)
            {
                if (track.Id < 0 || string.IsNullOrWhiteSpace(track.Name)) continue;
                if (track.Name.Contains(fullName, StringComparison.OrdinalIgnoreCase))
                {
                    return track.Id;
                }
            }
        }

        var locName = langCode.ToLower() switch
        {
            "tr" => "türkçe",
            "en" => "ingilizce",
            "de" => "almanca",
            "fr" => "fransızca",
            "es" => "ispanyolca",
            "it" => "italyanca",
            "pt" => "portekizce",
            "ru" => "rusça",
            "ar" => "arapça",
            "nl" => "flemenkçe",
            _ => null
        };

        if (locName != null)
        {
            foreach (var track in tracks)
            {
                if (track.Id < 0 || string.IsNullOrWhiteSpace(track.Name)) continue;
                if (track.Name.Contains(locName, StringComparison.OrdinalIgnoreCase))
                {
                    return track.Id;
                }
            }
        }

        return -1;
    }

    public void UpdateDurationFromService(bool force = false)
    {
        var latestDuration = _vm.VideoPlayerService.Duration;
        if (latestDuration <= 0)
        {
            if (force && _vm.Duration <= 0)
            {
                _vm.DurationText = "00:00:00";
            }
            return;
        }

        if (!force && _vm.Duration > 0 && Math.Abs(_vm.Duration - latestDuration) < 0.25)
        {
            return;
        }

        _vm.Duration = latestDuration;
        _vm.DurationText = TimeSpan.FromSeconds(latestDuration).ToString(@"hh\:mm\:ss");
    }

    private static PlayerViewModel.TrackOption BuildTrackOption(int id, string? rawName, string fallback)
    {
        var languageCode = PlayerViewModel.ExtractTrackLanguageCode(rawName);
        var name = NormalizeTrackName(rawName, fallback);

        if (!string.IsNullOrWhiteSpace(languageCode) &&
            !name.Contains($"({languageCode})", StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(name, $@"\b{Regex.Escape(languageCode!)}\b", RegexOptions.IgnoreCase))
        {
            name = $"{name} ({languageCode!.ToUpperInvariant()})";
        }

        return new PlayerViewModel.TrackOption(id, name, languageCode);
    }

    public static bool IsDisabledTrackLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.Trim();
        return string.Equals(value, "Disable", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Devre Dışı", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Kapalı", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeTrackName(string? rawName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return fallback;
        }

        var name = rawName.Trim();

        name = Regex.Replace(
            name,
            @"^\s*(track|audio|subtitle|ses|altyazı)\s*\d+\s*([:\-\)\.]|\s)\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        name = Regex.Replace(
            name,
            @"(#\w+|\[NCTRA\]|\[.*?SEED\]|\[.*?RIP\]|\[.*?WEB\]|\[.*?HD\]|\[.*?TV\])",
            string.Empty,
            RegexOptions.IgnoreCase);

        name = Regex.Replace(
            name,
            @"https?://\S+|www\.\S+|\b[\w-]+\.(org|com|net|info|tv|io|cc|me|co|xyz)\b",
            string.Empty,
            RegexOptions.IgnoreCase);

        name = Regex.Replace(
            name,
            @"\((?i:tr|en|de|fr|es|it|ru|ar|pl|pt|nl|sv|da|no|fi)\)|\b(?i:tr|en|de|fr|es|it|ru|ar|pl|pt|nl|sv|da|no|fi)\b",
            string.Empty,
            RegexOptions.IgnoreCase);

        if (Regex.IsMatch(name, @"^\s*(track|audio|subtitle|ses|altyazı)\s*\d+\s*$", RegexOptions.IgnoreCase))
        {
            return fallback;
        }

        name = name.Replace("[", string.Empty).Replace("]", string.Empty);
        
        name = Regex.Replace(name, @"\s*[:\-\.]+\s*$", string.Empty);
        name = Regex.Replace(name, @"^[:\-\.]+\s*", string.Empty);
        
        name = Regex.Replace(name, @"\s+", " ").Trim();
        name = CollapseDuplicateLabelParts(name);

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string CollapseDuplicateLabelParts(string value)
    {
        var parts = value
            .Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (parts.Count <= 1)
        {
            return value;
        }

        static string Key(string text) => Regex.Replace(text, @"[\W_]+", string.Empty).ToLowerInvariant();

        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var key = Key(part);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            unique.Add(part);
        }

        if (unique.Count == 0)
        {
            return value;
        }

        return string.Join(" - ", unique);
    }
}
