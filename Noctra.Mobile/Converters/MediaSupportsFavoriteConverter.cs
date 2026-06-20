using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Converters
{
    public class MediaSupportsFavoriteConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is Channel || value is Series;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => null;
    }
}
