using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Mobile.Localization;

namespace Noctra.Mobile.Converters;

public sealed class TimeSpanToCountdownConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan timeSpan || timeSpan <= TimeSpan.Zero)
        {
            return LocalizationSource.Instance["Profile.Deletion.Deleting"];
        }

        if (timeSpan.TotalDays >= 1)
        {
            return string.Format(culture, LocalizationSource.Instance["Profile.Deletion.DaysLeftFormat"], (int)timeSpan.TotalDays, timeSpan.Hours);
        }

        if (timeSpan.TotalHours >= 1)
        {
            return string.Format(culture, LocalizationSource.Instance["Profile.Deletion.HoursLeftFormat"], (int)timeSpan.TotalHours, timeSpan.Minutes);
        }

        return string.Format(culture, LocalizationSource.Instance["Profile.Deletion.MinutesLeftFormat"], timeSpan.Minutes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
