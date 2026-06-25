using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;
using Noctra.ViewModels;

namespace Noctra.Mobile.Converters;

public sealed class FillModeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerViewModel.FillMode mode)
        {
            return mode switch
            {
                PlayerViewModel.FillMode.Fit => MaterialIconKind.AspectRatio,
                PlayerViewModel.FillMode.Fill => MaterialIconKind.CropFree,
                PlayerViewModel.FillMode.Stretch => MaterialIconKind.ArrowExpandAll,
                PlayerViewModel.FillMode.Original => MaterialIconKind.ImageSizeSelectActual,
                _ => MaterialIconKind.AspectRatio
            };
        }

        return MaterialIconKind.AspectRatio;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
