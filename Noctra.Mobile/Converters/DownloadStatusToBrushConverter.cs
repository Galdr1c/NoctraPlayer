using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class DownloadStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadStatus status)
        {
            return Brushes.Transparent;
        }

        return status switch
        {
            DownloadStatus.Paused => FindBrush("WarningBrush", Brushes.Orange),
            DownloadStatus.Failed => FindBrush("ErrorBrush", Brushes.Red),
            _ => FindBrush("AccentBrush", Brushes.Purple)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;

    private static IBrush FindBrush(string resourceKey, IBrush fallback)
        => Application.Current?.TryGetResource(resourceKey, ThemeVariant.Default, out var resource) == true && resource is IBrush brush
            ? brush
            : fallback;
}
