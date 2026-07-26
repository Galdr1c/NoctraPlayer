using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Noctra.Avalonia.Localization;

namespace Noctra.Avalonia.Views;

internal static class DesktopSettingsSelectionCatalog
{
    private static readonly (string Value, string Label)[] PlaybackLanguages =
    [
        ("tr", "Turkish"),
        ("en", "English"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("es", "Español"),
        ("it", "Italiano"),
        ("pt", "Português"),
        ("ru", "Русский"),
        ("ar", "العربية"),
        ("nl", "Nederlands")
    ];

    private static readonly (string Value, string LabelKey)[] AppLanguages =
    [
        ("tr", "Language.Turkish"),
        ("en", "Language.English"),
        ("de", "Language.German"),
        ("fr", "Language.French"),
        ("es", "Language.Spanish")
    ];

    private static readonly string[] DataUsageLabelKeys =
    [
        "Settings.Playback.Quality.Low",
        "Settings.Playback.Quality.Medium",
        "Settings.Playback.Quality.High",
        "Settings.Playback.Quality.Auto"
    ];

    private static readonly string[] DownloadQualityLabelKeys =
    [
        "Settings.Download.Quality.Standard",
        "Settings.Download.Quality.High"
    ];

    private static readonly string[] RefreshFrequencyLabelKeys =
    [
        "Settings.Channels.Frequency.Off",
        "Settings.Channels.Frequency.1h",
        "Settings.Channels.Frequency.3h",
        "Settings.Channels.Frequency.12h",
        "Settings.Channels.Frequency.24h",
        "Settings.Channels.Frequency.2d",
        "Settings.Channels.Frequency.3d",
        "Settings.Channels.Frequency.7d"
    ];

    private static readonly string[] HistoryRetentionLabelKeys =
    [
        "Settings.Privacy.Retention.Forever",
        "Settings.Privacy.Retention.3d",
        "Settings.Privacy.Retention.7d",
        "Settings.Privacy.Retention.14d",
        "Settings.Privacy.Retention.30d"
    ];

    public static IReadOnlyList<DesktopSelectionOption> BuildDataUsage(int selectedIndex)
        => BuildIndexedOptions(DataUsageLabelKeys, selectedIndex);

    public static string GetDataUsageLabel(int selectedIndex)
        => GetIndexedLabel(DataUsageLabelKeys, selectedIndex);

    public static IReadOnlyList<DesktopSelectionOption> BuildDownloadQualities(int selectedIndex)
        => BuildIndexedOptions(DownloadQualityLabelKeys, selectedIndex);

    public static string GetDownloadQualityLabel(int selectedIndex)
        => GetIndexedLabel(DownloadQualityLabelKeys, selectedIndex);

    public static IReadOnlyList<DesktopSelectionOption> BuildPlaybackLanguages(string? selectedValue)
        => PlaybackLanguages
            .Select(item => new DesktopSelectionOption(
                item.Value,
                item.Label,
                string.Equals(item.Value, selectedValue, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    public static string GetPlaybackLanguageLabel(string? selectedValue)
    {
        var match = PlaybackLanguages.FirstOrDefault(item =>
            string.Equals(item.Value, selectedValue, StringComparison.OrdinalIgnoreCase));
        return match.Value is not null
            ? match.Label
            : selectedValue ?? PlaybackLanguages[1].Label;
    }

    public static IReadOnlyList<DesktopSelectionOption> BuildRefreshFrequencies(
        int selectedIndex,
        bool isPremium)
        => BuildIndexedOptions(
            RefreshFrequencyLabelKeys,
            selectedIndex,
            index => index > 0 && !isPremium);

    public static string GetRefreshFrequencyLabel(int selectedIndex)
        => GetIndexedLabel(RefreshFrequencyLabelKeys, selectedIndex);

    public static IReadOnlyList<DesktopSelectionOption> BuildTimezones(int selectedIndex)
    {
        var normalizedIndex = Math.Clamp(selectedIndex, 0, 24);
        return Enumerable.Range(0, 25)
            .Select(index => new DesktopSelectionOption(
                index,
                GetTimezoneLabel(index),
                index == normalizedIndex))
            .ToArray();
    }

    public static string GetTimezoneLabel(int selectedIndex)
    {
        var normalizedIndex = Math.Clamp(selectedIndex, 0, 24);
        var offset = normalizedIndex - 12;
        if (offset == 0)
        {
            return Text("Settings.Epg.Timezone.Auto");
        }

        var formatKey = offset > 0
            ? "Settings.Epg.Timezone.Format.Positive"
            : "Settings.Epg.Timezone.Format.Negative";
        return string.Format(CultureInfo.CurrentCulture, Text(formatKey), offset);
    }

    public static IReadOnlyList<DesktopSelectionOption> BuildHistoryRetention(int selectedIndex)
        => BuildIndexedOptions(HistoryRetentionLabelKeys, selectedIndex);

    public static string GetHistoryRetentionLabel(int selectedIndex)
        => GetIndexedLabel(HistoryRetentionLabelKeys, selectedIndex);

    public static IReadOnlyList<DesktopSelectionOption> BuildAppLanguages(string? selectedValue)
        => AppLanguages
            .Select(item => new DesktopSelectionOption(
                item.Value,
                Text(item.LabelKey),
                string.Equals(item.Value, selectedValue, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    public static string GetAppLanguageLabel(string? selectedValue)
    {
        var match = AppLanguages.FirstOrDefault(item =>
            string.Equals(item.Value, selectedValue, StringComparison.OrdinalIgnoreCase));
        return match.Value is not null
            ? Text(match.LabelKey)
            : selectedValue ?? Text("Language.English");
    }

    public static string Text(string key) => LocalizationSource.Instance[key];

    private static IReadOnlyList<DesktopSelectionOption> BuildIndexedOptions(
        IReadOnlyList<string> labelKeys,
        int selectedIndex,
        Func<int, bool>? isLocked = null)
    {
        var normalizedIndex = Math.Clamp(selectedIndex, 0, labelKeys.Count - 1);
        return labelKeys
            .Select((key, index) => new DesktopSelectionOption(
                index,
                Text(key),
                index == normalizedIndex,
                isLocked?.Invoke(index) == true))
            .ToArray();
    }

    private static string GetIndexedLabel(
        IReadOnlyList<string> labelKeys,
        int selectedIndex)
    {
        var normalizedIndex = Math.Clamp(selectedIndex, 0, labelKeys.Count - 1);
        return Text(labelKeys[normalizedIndex]);
    }
}
