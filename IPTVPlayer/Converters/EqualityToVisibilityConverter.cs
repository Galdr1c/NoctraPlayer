using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IPTVPlayer.Converters;

public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isEqual = false;
        if (value == null && parameter == null) isEqual = true;
        else if (value != null) isEqual = value.Equals(parameter);

        return isEqual ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
