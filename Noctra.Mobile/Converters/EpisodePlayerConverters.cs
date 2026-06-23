using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

/// <summary>
/// Produces the same stable episode identity that PlayerViewModel uses for current-episode highlighting.
/// </summary>
public sealed class EpisodeIdentityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Episode episode)
        {
            return string.Empty;
        }

        if (episode.Id > 0)
        {
            return $"id:{episode.Id}";
        }

        return !string.IsNullOrWhiteSpace(episode.StreamUrl)
            ? $"url:{episode.StreamUrl.Trim()}"
            : string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// MultiBinding helper used by the mobile episode overlay to compare item identity with the current episode.
/// </summary>
public sealed class EqualityToBoolMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return false;
        }

        var left = values[0]?.ToString();
        var right = values[1]?.ToString();

        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.Ordinal);
    }
}

/// <summary>
/// MultiBinding helper that returns an accent brush when two values match.
/// </summary>
public sealed class EqualityToBrushMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return Brushes.Transparent;
        }

        var left = values[0]?.ToString();
        var right = values[1]?.ToString();

        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right) ||
            !string.Equals(left, right, StringComparison.Ordinal))
        {
            return Brushes.Transparent;
        }

        if (parameter is string resourceKey &&
            Application.Current?.TryGetResource(resourceKey, ThemeVariant.Default, out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.MediumPurple;
    }
}

/// <summary>
/// Converts an episode watch percentage to a fixed-width progress segment.
/// </summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryGetDouble(value, out var percentage) ||
            parameter == null ||
            !double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var totalWidth))
        {
            return 0d;
        }

        return Math.Clamp(percentage, 0d, 100d) / 100d * totalWidth;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryGetDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            default:
                result = 0d;
                return false;
        }
    }
}
