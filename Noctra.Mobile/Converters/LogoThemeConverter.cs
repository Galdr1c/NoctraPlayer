using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Noctra.Mobile.Converters;

/// <summary>
/// Converts ActualThemeVariant to the appropriate theme-aware logo Bitmap.
/// Uses the Mobile project's logo assets:
///   Dark mode: Square150x150Logo.png / Square150x150Logo.Gray.png
///   Light mode: Square150x150LogoLight.png / Square150x150Logo.GrayLight.png
/// ConverterParameter="Full" selects the gray/full variant.
/// </summary>
public sealed class LogoThemeConverter : IValueConverter
{
    private const string RegularDark = "avares://Noctra.Mobile/Assets/Square150x150Logo.png";
    private const string RegularLight = "avares://Noctra.Mobile/Assets/Square150x150LogoLight.png";
    private const string FullDark = "avares://Noctra.Mobile/Assets/Square150x150Logo.Gray.png";
    private const string FullLight = "avares://Noctra.Mobile/Assets/Square150x150Logo.GrayLight.png";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = value is ThemeVariant variant && variant == ThemeVariant.Dark;
        var isFull = parameter?.ToString() == "Full";

        var uri = isFull
            ? (isDark ? FullDark : FullLight)
            : (isDark ? RegularDark : RegularLight);

        try
        {
            var assetUri = new Uri(uri);
            if (AssetLoader.Exists(assetUri))
            {
                using var stream = AssetLoader.Open(assetUri);
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Fall through to fallback
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
