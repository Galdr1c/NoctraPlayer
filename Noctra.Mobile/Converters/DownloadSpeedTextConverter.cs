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
        if (value is not double speedBytesPerSecond || speedBytesPerSecond <= 0)
        {
            return "-";
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationSource.Instance["Downloads.Speed.PerSecondFormat"],
            DownloadItem.FormatBytes((long)speedBytesPerSecond));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
