using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class WatchedProgressWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Channel channel || channel.Type == ChannelType.Live)
        {
            return 0d;
        }

        var maxWidth = 160d;
        if (parameter is not null &&
            double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth))
        {
            maxWidth = parsedWidth;
        }

        if (!channel.WatchedPosition.HasValue || channel.WatchedPosition.Value.TotalSeconds <= 0)
        {
            return 0d;
        }

        double percent;
        if (channel.Duration.HasValue && channel.Duration.Value.TotalSeconds > 0)
        {
            percent = channel.WatchedPosition.Value.TotalSeconds / channel.Duration.Value.TotalSeconds * 100d;
        }
        else
        {
            var watchedMinutes = channel.WatchedPosition.Value.TotalMinutes;
            percent = Math.Clamp(8d + watchedMinutes * 2.2d, 8d, 88d);
        }

        return maxWidth * (Math.Clamp(percent, 0d, 100d) / 100d);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
