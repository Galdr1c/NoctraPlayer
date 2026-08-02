using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Mobile.Localization;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class DownloadEtaTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadItem item ||
            !item.EstimatedSecondsRemaining.HasValue ||
            item.EstimatedSecondsRemaining.Value <= 0)
        {
            return string.Empty;
        }

        var ts = TimeSpan.FromSeconds(item.EstimatedSecondsRemaining.Value);
        var source = LocalizationSource.Instance;

        if (ts.TotalHours >= 1)
        {
            return string.Format(CultureInfo.CurrentCulture, source["Downloads.Eta.HoursFormat"], (int)ts.TotalHours, ts.Minutes);
        }

        if (ts.TotalMinutes >= 1)
        {
            return string.Format(CultureInfo.CurrentCulture, source["Downloads.Eta.MinutesFormat"], (int)ts.TotalMinutes, ts.Seconds);
        }

        return string.Format(CultureInfo.CurrentCulture, source["Downloads.Eta.SecondsFormat"], ts.Seconds);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
