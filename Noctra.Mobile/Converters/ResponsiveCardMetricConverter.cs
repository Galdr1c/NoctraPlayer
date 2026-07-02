using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

/// <summary>
/// Calculates responsive card metrics from the available ItemsControl width.
/// The converter intentionally keeps the math simple and predictable:
/// - poster cards: 2 columns on phones, more columns as the viewport grows;
/// - continue cards: 1 column on phones, 2+ columns on larger/tablet widths.
/// - live cards: 1 column on phones, 2+ columns on tablets.
///
/// ConverterParameter values:
/// - "posterWidth" / "posterHeight"
/// - "liveWidth"
/// - "continueWidth" / "continueHeight"
/// - "moreShortcutWidth"
/// - "profileWidth" / "profileHeight"
/// </summary>
public sealed class ResponsiveCardMetricConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var availableWidth = ToDouble(value);
        var mode = parameter?.ToString() ?? "posterWidth";

        var profile = CardMetricProfile.For(mode);
        var width = CalculateWidth(availableWidth, profile);

        if (mode.EndsWith("Height", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(width * profile.HeightRatio);
        }

        return width;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double CalculateWidth(double availableWidth, CardMetricProfile profile)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return profile.DefaultWidth;
        }

        var usableWidth = Math.Max(0, availableWidth);
        var columns = Math.Max(1, (int)Math.Floor((usableWidth + profile.Gap) / (profile.MinWidth + profile.Gap)));
        columns = Math.Min(columns, profile.MaxColumns);

        var width = Math.Floor((usableWidth - profile.Gap * (columns - 1)) / columns);
        width = Math.Max(1, width);

        if (width > profile.MaxWidth)
        {
            width = profile.MaxWidth;
        }

        if (width < profile.MinWidth && usableWidth >= profile.MinWidth)
        {
            width = profile.MinWidth;
        }
        else if (usableWidth < profile.MinWidth)
        {
            width = Math.Max(1, usableWidth);
        }

        return width;
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            short s => s,
            string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d
        };
    }

    private readonly record struct CardMetricProfile(
        double MinWidth,
        double MaxWidth,
        double DefaultWidth,
        double HeightRatio,
        double Gap,
        int MaxColumns)
    {
        public static CardMetricProfile For(string mode)
        {
            if (mode.StartsWith("continue", StringComparison.OrdinalIgnoreCase))
            {
                // Landscape cards: one wide card on phones, two or three on larger screens.
                return new CardMetricProfile(250, 360, 300, 0.56, 16, 4);
            }

            if (mode.StartsWith("live", StringComparison.OrdinalIgnoreCase))
            {
                // Live rows stay full-width on phones, then split cleanly on tablets.
                return new CardMetricProfile(280, 420, 320, 0.30, 16, 3);
            }

            if (mode.StartsWith("profile", StringComparison.OrdinalIgnoreCase))
            {
                // Profile cards keep the avatar-heavy shape used in the desktop profile window.
                // MinWidth 150 ensures the 150x150 avatar Grid never overflows the card.
                return new CardMetricProfile(150, 200, 170, 1.18, 16, 5);
            }

            if (mode.StartsWith("moreShortcut", StringComparison.OrdinalIgnoreCase))
            {
                // More menu action tiles: two columns on phones, more on tablets.
                return new CardMetricProfile(150, 190, 160, 0.62, 12, 4);
            }

            // Poster cards: 2 columns on most phones, 3+ on foldables/tablets.
            return new CardMetricProfile(150, 190, 160, 1.50, 16, 6);
        }
    }
}
