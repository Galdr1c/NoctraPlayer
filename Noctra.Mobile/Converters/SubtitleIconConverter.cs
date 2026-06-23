using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace Noctra.Mobile.Converters;

/// <summary>
/// Altyazı track'i seçiliyse dolu ikon, değilse outline ikon döndürür.
/// PlayerViewModel.SelectedSubtitleTrack (int) için kullanılır.
/// </summary>
public sealed class SubtitleIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrackSelected = value switch
        {
            int id => id >= 0,
            _ => false
        };

        return isTrackSelected ? MaterialIconKind.Subtitles : MaterialIconKind.SubtitlesOutline;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
