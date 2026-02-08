using System.Globalization;
using System.Windows.Data;

namespace IPTVPlayer.Converters;

public class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width)
        {
            if (width < 600) return 1;
            if (width < 900) return 2;
            if (width < 1200) return 3;
            if (width < 1500) return 4;
            if (width < 1800) return 5;
            return 6;
        }
        return 4; // Default fallback
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
