using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Converters
{
    public class MediaSupportsMyListConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                Series => true,
                Channel channel => channel.Type != ChannelType.Live,
                _ => false
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => null;
    }
}
