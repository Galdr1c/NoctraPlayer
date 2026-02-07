using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IPTVPlayer.Converters;

public class EqualityToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Transparent;
    public Brush FalseBrush { get; set; } = Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isEqual = false;
        if (value == null && parameter == null) isEqual = true;
        else if (value != null) isEqual = value.Equals(parameter);

        return isEqual ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
