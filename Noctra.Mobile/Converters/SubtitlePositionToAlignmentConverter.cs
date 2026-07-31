using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public class SubtitlePositionToAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SubtitleVerticalPosition position)
        {
            return position switch
            {
                SubtitleVerticalPosition.Top or SubtitleVerticalPosition.UpperMiddle
                    => Avalonia.Layout.VerticalAlignment.Top,
                _ => Avalonia.Layout.VerticalAlignment.Bottom
            };
        }
        return Avalonia.Layout.VerticalAlignment.Bottom;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
