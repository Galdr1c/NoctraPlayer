using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class FillModeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VideoScaleMode mode)
        {
            return mode switch
            {
                VideoScaleMode.Fit => MaterialIconKind.AspectRatio,
                VideoScaleMode.Fill => MaterialIconKind.CropFree,
                VideoScaleMode.Stretch => MaterialIconKind.ArrowExpandAll,
                _ => MaterialIconKind.AspectRatio
            };
        }

        return MaterialIconKind.AspectRatio;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
