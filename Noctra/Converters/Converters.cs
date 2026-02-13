using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Noctra.Models;

namespace Noctra.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = false;
        if (value is bool b) 
            isVisible = b;
        else if (value is bool?) 
            isVisible = ((bool?)value).GetValueOrDefault();
            
        if (parameter?.ToString() == "Invert") isVisible = !isVisible;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return value;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        if (parameter?.ToString() == "Invert") isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ProfileColorConverter : IValueConverter
{
    private static readonly Brush[] _brushes = new Brush[]
    {
        CreateGradient("#E50914", "#B81D24"), // Noctra Red
        CreateGradient("#1A73E8", "#0D47A1"), // Blue
        CreateGradient("#0F9D58", "#1B5E20"), // Green
        CreateGradient("#F4B400", "#FF6F00"), // Yellow/Orange
        CreateGradient("#AB47BC", "#6A1B9A"), // Purple
        CreateGradient("#26C6DA", "#0097A7"), // Cyan
        CreateGradient("#FF7043", "#D84315"), // Deep Orange
    };

    private static LinearGradientBrush CreateGradient(string startColor, string endColor)
    {
        var gradient = new LinearGradientBrush();
        gradient.StartPoint = new System.Windows.Point(0, 0);
        gradient.EndPoint = new System.Windows.Point(1, 1);
        gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(startColor), 0.0));
        gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(endColor), 1.0));
        return gradient;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            int index = Math.Abs(name.GetHashCode()) % _brushes.Length;
            return _brushes[index];
        }
        return _brushes[0];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is int count && count > 0;
        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string trueText = "Yes";
        string falseText = "No";

        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                trueText = parts[0];
                falseText = parts[1];
            }
        }

        if (value is bool b) return b ? trueText : falseText;
        return falseText;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualityToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualityToBoolMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        return values[0]?.ToString() == values[1]?.ToString();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = !string.IsNullOrEmpty(value as string);
        if (parameter?.ToString() == "invert") isVisible = !isVisible;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AvatarPathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string fileName && !string.IsNullOrEmpty(fileName))
        {
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            try
            {
                var uri = new Uri($"pack://application:,,,/Noctra;component/Assets/Avatars/{fileName}", UriKind.Absolute);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Optimization for cross-thread access
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AvatarExpressionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? name = value as string;
        if (string.IsNullOrEmpty(name)) return "NoctraFaceSmile";

        int id = 0;
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+");
        if (match.Success) id = int.Parse(match.Value);

        return (id % 4) switch
        {
            1 => "NoctraFaceCool",
            2 => "NoctraFaceWink",
            3 => "NoctraFaceSurprise",
            _ => "NoctraFaceSmile"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ErrorColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasError = value is bool b && b;
        return hasError ? (SolidColorBrush)Application.Current.Resources["NoctraRedBrush"] : Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ObjectToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = value != null;
        if (parameter?.ToString() == "Invert") isVisible = !isVisible;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class FavoriteConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringContainsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && parameter is string search)
        {
            return str.Contains(search, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToPlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // True = IsPlaying -> Show Pause icon
        // False = IsPaused -> Show Play icon
        if (value is bool isPlaying && isPlaying)
            return "⏸"; // Pause
        return "▶"; // Play
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class MediaThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Channel channel) return channel.LogoUrl;
        if (value is Series series) return series.CoverUrl;
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
public class DoubleToFloatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value != null && float.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
            return result;
        if (parameter != null && float.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float pResult))
            return pResult;
        return 1.0f;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percentage && parameter != null && double.TryParse(parameter.ToString(), out double totalWidth))
        {
            return (percentage / 100.0) * totalWidth;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isInList)
            return isInList ? "✓" : "+";
        return "+";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isPlaying = value is bool b && b;
        
        // Play icon path
        string playIcon = "M8 5v14l11-7z";
        
        // Pause icon path
        string pauseIcon = "M6 19h4V5H6v14zm8-14v14h4V5h-4z";
        
        return isPlaying ? pauseIcon : playIcon;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}

public class VolumeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isMuted = value is bool b && b;
        
        if (isMuted)
        {
            // Muted icon path
            return "M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z";
        }
        
        return "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}

public class ChannelTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ChannelType type && parameter is string targetTypeStr)
        {
            bool matches = type.ToString().Equals(targetTypeStr, StringComparison.OrdinalIgnoreCase);
            return matches ? Visibility.Visible : Visibility.Collapsed;
        }
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}


