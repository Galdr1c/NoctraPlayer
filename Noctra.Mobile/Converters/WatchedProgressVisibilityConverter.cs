using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class WatchedProgressVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Channel channel || channel.Type == ChannelType.Live)
        {
            return false;
        }

        if (!channel.WatchedPosition.HasValue || channel.WatchedPosition.Value.TotalSeconds <= 0)
        {
            return false;
        }

        if (!channel.Duration.HasValue || channel.Duration.Value.TotalSeconds <= 0)
        {
            return true;
        }

        var watchedSeconds = channel.WatchedPosition.Value.TotalSeconds;
        var totalSeconds = channel.Duration.Value.TotalSeconds;
        var percent = watchedSeconds / totalSeconds * 100d;
        var remainingSeconds = Math.Max(0d, totalSeconds - watchedSeconds);

        return percent < 90d && remainingSeconds > TimeSpan.FromMinutes(5).TotalSeconds;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
