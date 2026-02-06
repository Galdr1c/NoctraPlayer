using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IPTVPlayer.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = (bool)value;
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
        CreateGradient("#E50914", "#B81D24"), // Netflix Red
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
        if (value is int count) return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
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
                var uri = new Uri($"pack://application:,,,/IPTVPlayer;component/Assets/Avatars/{fileName}", UriKind.Absolute);
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
        if (string.IsNullOrEmpty(name)) return "NetflixFaceSmile";

        int id = 0;
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+");
        if (match.Success) id = int.Parse(match.Value);

        return (id % 4) switch
        {
            1 => "NetflixFaceCool",
            2 => "NetflixFaceWink",
            3 => "NetflixFaceSurprise",
            _ => "NetflixFaceSmile"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ErrorColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasError = value is bool b && b;
        return hasError ? (SolidColorBrush)Application.Current.Resources["NetflixRedBrush"] : Brushes.White;
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
