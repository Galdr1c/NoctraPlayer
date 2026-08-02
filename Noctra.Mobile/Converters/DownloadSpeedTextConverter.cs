using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Mobile.Localization;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class DownloadSpeedTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadItem item || item.SpeedBytesPerSecond <= 0)
        {
            return "-";
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationSource.Instance["Downloads.Speed.PerSecondFormat"],
            DownloadItem.FormatBytes((long)item.SpeedBytesPerSecond));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
