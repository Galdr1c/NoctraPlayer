using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Noctra.Mobile.Converters;

/// <summary>
/// Converts ActualThemeVariant to the appropriate theme-aware logo Bitmap.
/// Each asset is decoded at most once for the lifetime of the process.
/// ConverterParameter="Full" selects the compact/full logo variant.
/// </summary>
public sealed class LogoThemeConverter : IValueConverter
{
    private const string RegularDarkUri = "avares://Noctra.Mobile/Assets/Square150x150LogoTPLight.png";
    private const string RegularLightUri = "avares://Noctra.Mobile/Assets/Square150x150LogoTPDark.png";
    private const string FullDarkUri = "avares://Noctra.Mobile/Assets/Square150x150LogoTPFullLight.png";
    private const string FullLightUri = "avares://Noctra.Mobile/Assets/Square150x150LogoTPFullDark.png";

    private static readonly Lazy<Bitmap?> RegularDark = new(() => LoadBitmap(RegularDarkUri));
    private static readonly Lazy<Bitmap?> RegularLight = new(() => LoadBitmap(RegularLightUri));
    private static readonly Lazy<Bitmap?> FullDark = new(() => LoadBitmap(FullDarkUri));
    private static readonly Lazy<Bitmap?> FullLight = new(() => LoadBitmap(FullLightUri));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = value is ThemeVariant variant && variant == ThemeVariant.Dark;
        var isFull = parameter?.ToString() == "Full";

        var bitmap = isFull
            ? (isDark ? FullDark : FullLight)
            : (isDark ? RegularDark : RegularLight);

        return bitmap.Value;
    }

    private static Bitmap? LoadBitmap(string uri)
    {
        try
        {
            var assetUri = new Uri(uri);
            if (AssetLoader.Exists(assetUri))
            {
                using var stream = AssetLoader.Open(assetUri);
                var bitmap = new Bitmap(stream);
                return bitmap;
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
