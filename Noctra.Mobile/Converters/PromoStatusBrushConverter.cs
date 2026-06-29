using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noctra.Mobile.Converters;

public sealed class PromoStatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSuccess)
        {
            return FindBrush(isSuccess ? "SuccessBrush" : "WarningBrush", isSuccess ? Brushes.Green : Brushes.Orange);
        }

        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return FindBrush("TextMutedBrush", Brushes.Gray);
        }

        var normalized = text.ToLowerInvariant();
        if (normalized.Contains("error", StringComparison.Ordinal) ||
            normalized.Contains("failed", StringComparison.Ordinal) ||
            normalized.Contains("invalid", StringComparison.Ordinal) ||
            normalized.Contains("hata", StringComparison.Ordinal))
        {
            return FindBrush("WarningBrush", Brushes.Orange);
        }

        return FindBrush("SuccessBrush", Brushes.Green);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;

    private static IBrush FindBrush(string resourceKey, IBrush fallback)
        => Application.Current?.TryGetResource(resourceKey, ThemeVariant.Default, out var resource) == true && resource is IBrush brush
            ? brush
            : fallback;
}
