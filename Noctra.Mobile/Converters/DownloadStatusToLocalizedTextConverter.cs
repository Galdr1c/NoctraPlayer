using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Mobile.Localization;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class DownloadStatusToLocalizedTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadItem item)
        {
            return string.Empty;
        }

        var key = item.Status switch
        {
            DownloadStatus.Queued => "Downloads.Status.Queued",
            DownloadStatus.Downloading => "Downloads.Status.Downloading",
            DownloadStatus.Paused => "Downloads.Status.Paused",
            DownloadStatus.Completed => "Downloads.Status.Completed",
            DownloadStatus.Failed => "Downloads.Status.Failed",
            DownloadStatus.Canceled => "Downloads.Status.Canceled",
            _ => null
        };

        if (key is null)
        {
            return "-";
        }

        var text = LocalizationSource.Instance[key];

        if ((item.Status == DownloadStatus.Failed || item.Status == DownloadStatus.Paused) &&
            !string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            return $"{text} - {item.ErrorMessage}";
        }

        return text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
