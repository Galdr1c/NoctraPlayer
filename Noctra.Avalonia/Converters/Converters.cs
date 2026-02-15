using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Noctra.Models;

namespace Noctra.Avalonia.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = value is bool b && b;
        if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        if (string.Equals(parameter?.ToString(), "OpacityOnly", StringComparison.OrdinalIgnoreCase))
        {
            return isVisible ? 1.0 : 0.0;
        }

        return isVisible ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value == null;
        if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isNull = !isNull;
        }

        return isNull ? false : true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class ProfileColorConverter : IValueConverter
{
    private static readonly IBrush[] Brushes =
    [
        CreateGradient("#E50914", "#B81D24"),
        CreateGradient("#1A73E8", "#0D47A1"),
        CreateGradient("#0F9D58", "#1B5E20"),
        CreateGradient("#F4B400", "#FF6F00"),
        CreateGradient("#AB47BC", "#6A1B9A"),
        CreateGradient("#26C6DA", "#0097A7"),
        CreateGradient("#FF7043", "#D84315")
    ];

    private static LinearGradientBrush CreateGradient(string startColor, string endColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse(startColor), 0.0),
                new GradientStop(Color.Parse(endColor), 1.0)
            ]
        };
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            var index = Math.Abs(name.GetHashCode()) % Brushes.Length;
            return Brushes[index];
        }

        return Brushes[0];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class WidthToColumnsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
        {
            return 4;
        }

        if (width < 600) return 1;
        if (width < 900) return 2;
        if (width < 1200) return 3;
        if (width < 1500) return 4;
        if (width < 1800) return 5;
        return 6;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class CountToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = value switch
        {
            int count => count > 0,
            long count => count > 0,
            double count => count > 0,
            float count => count > 0,
            decimal count => count > 0,
            ICollection collection => collection.Count > 0,
            _ => false
        };

        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class PercentageThresholdToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return false;
        }

        var threshold = 95d;
        if (parameter != null &&
            double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedThreshold))
        {
            threshold = parsedThreshold;
        }

        var percentage = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue)
                ? parsedValue
                : 0d
        };

        return percentage >= threshold ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class PercentageRangeToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return false;
        }

        var percentage = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            decimal m => (double)m,
            _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0d
        };

        var min = 0d;
        var max = 90d;
        if (parameter is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                _ = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min);
                _ = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max);
            }
        }

        return percentage > min && percentage < max ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class WatchedProgressVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Channel channel || channel.Type == ChannelType.Live)
        {
            return false;
        }

        if (!channel.WatchedPosition.HasValue || channel.WatchedPosition.Value.TotalSeconds <= 0)
        {
            return false;
        }

        if (!channel.Duration.HasValue || channel.Duration.Value.TotalSeconds <= 0)
        {
            return true;
        }

        var watchedSeconds = channel.WatchedPosition.Value.TotalSeconds;
        var totalSeconds = channel.Duration.Value.TotalSeconds;
        var percent = (watchedSeconds / totalSeconds) * 100d;
        var remainingSeconds = Math.Max(0d, totalSeconds - watchedSeconds);

        return percent >= 90d || remainingSeconds <= TimeSpan.FromMinutes(5).TotalSeconds
            ? false
            : true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class WatchedProgressWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Channel channel || channel.Type == ChannelType.Live)
        {
            return 0d;
        }

        var maxWidth = 160d;
        if (parameter != null &&
            double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth))
        {
            maxWidth = parsedWidth;
        }

        if (!channel.WatchedPosition.HasValue || channel.WatchedPosition.Value.TotalSeconds <= 0)
        {
            return 0d;
        }

        double percent;
        if (channel.Duration.HasValue && channel.Duration.Value.TotalSeconds > 0)
        {
            percent = (channel.WatchedPosition.Value.TotalSeconds / channel.Duration.Value.TotalSeconds) * 100d;
        }
        else
        {
            var watchedMinutes = channel.WatchedPosition.Value.TotalMinutes;
            percent = Math.Clamp(8d + watchedMinutes * 2.2d, 8d, 88d);
        }

        return maxWidth * (Math.Clamp(percent, 0d, 100d) / 100d);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BitrateDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int bitrate || bitrate <= 0)
        {
            return "Bilinmiyor";
        }

        if (bitrate >= 1_000_000) return $"{bitrate / 1_000_000.0:F2} Mbps";
        if (bitrate >= 1_000) return $"{bitrate / 1_000.0:F1} Kbps";
        return $"{bitrate} bps";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var trueText = "Yes";
        var falseText = "No";

        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                trueText = parts[0];
                falseText = parts[1];
            }
        }

        return value is bool b && b ? trueText : falseText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EqualityToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EqualityToBoolMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return false;
        }

        return string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = !string.IsNullOrEmpty(value as string);
        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class AvatarPathConverter : IValueConverter
{
    private const string DefaultAvatarFileName = "avatar_1.png";
    private const string AvatarAssetPrefix = "avares://Noctra.Avalonia/Assets/Avatars/";
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string fileName || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalized = NormalizeAvatarFileName(fileName);
        var bitmap = GetOrLoadAvatar(normalized);
        if (bitmap != null)
        {
            return bitmap;
        }

        return !string.Equals(normalized, DefaultAvatarFileName, StringComparison.OrdinalIgnoreCase)
            ? GetOrLoadAvatar(DefaultAvatarFileName)
            : null;
    }

    private static string NormalizeAvatarFileName(string input)
    {
        var normalized = input.Trim().Trim('"', '\'');

        if (normalized.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath;
            var lastSlashIndex = path.LastIndexOf('/');
            normalized = lastSlashIndex >= 0 ? path[(lastSlashIndex + 1)..] : path;
        }

        normalized = normalized.Replace('\\', '/');
        var finalSlashIndex = normalized.LastIndexOf('/');
        if (finalSlashIndex >= 0)
        {
            normalized = normalized[(finalSlashIndex + 1)..];
        }

        if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".png";
        }

        return string.Equals(normalized, "default.png", StringComparison.OrdinalIgnoreCase)
            ? DefaultAvatarFileName
            : normalized;
    }

    private static Bitmap? GetOrLoadAvatar(string avatarFileName)
    {
        lock (CacheLock)
        {
            if (BitmapCache.TryGetValue(avatarFileName, out var cached))
            {
                return cached;
            }
        }

        var loaded = LoadFromAssets(avatarFileName) ?? LoadFromDisk(avatarFileName);
        if (loaded == null)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (BitmapCache.TryGetValue(avatarFileName, out var existing))
            {
                loaded.Dispose();
                return existing;
            }

            BitmapCache[avatarFileName] = loaded;
            return loaded;
        }
    }

    private static Bitmap? LoadFromAssets(string avatarFileName)
    {
        if (!Uri.TryCreate($"{AvatarAssetPrefix}{avatarFileName}", UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!AssetLoader.Exists(uri))
        {
            return null;
        }

        try
        {
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadFromDisk(string avatarFileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Avatars", avatarFileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "Avatars", avatarFileName))
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return new Bitmap(candidate);
                }
            }
            catch
            {
                // Continue trying fallback paths.
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class AvatarExpressionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        if (string.IsNullOrWhiteSpace(name)) return "NoctraFaceSmile";

        var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+");
        var id = match.Success && int.TryParse(match.Value, out var parsed) ? parsed : 0;

        return (id % 4) switch
        {
            1 => "NoctraFaceCool",
            2 => "NoctraFaceWink",
            3 => "NoctraFaceSurprise",
            _ => "NoctraFaceSmile"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class ErrorColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasError = value is bool b && b;
        if (hasError && Application.Current?.TryGetResource("NoctraRedBrush", out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class ObjectToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = value != null;
        if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class FavoriteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "❤" : "♡";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class StringContainsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string source && parameter is string part)
        {
            return source.Contains(part, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BoolToPlayPauseIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isPlaying && isPlaying ? "⏸" : "▶";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class MediaThumbnailConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Channel channel)
        {
            return NormalizeImageUrl(channel.CoverUrl) ?? NormalizeImageUrl(channel.LogoUrl);
        }

        if (value is Series series)
        {
            return NormalizeImageUrl(series.CoverUrl);
        }

        return null;
    }

    private static string? NormalizeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalized = url.Trim().Trim('"', '\'');
        if (normalized.Equals("logo n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class DoubleToFloatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value != null && float.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        if (parameter != null && float.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var fallback))
        {
            return fallback;
        }

        return 1.0f;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class PercentToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percentage &&
            parameter != null &&
            double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var totalWidth))
        {
            return (percentage / 100.0) * totalWidth;
        }

        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BoolToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isInList && isInList ? "✓" : "+";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class PlayPauseIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isPlaying = value is bool b && b;
        return isPlaying ? "M6 19h4V5H6v14zm8-14v14h4V5h-4z" : "M8 5v14l11-7z";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class VolumeIconConverter : IValueConverter
{
    private const string VolumeOnPath = "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z";
    private const string VolumeMutedPath = "M3 9v6h4l5 5V4L7 9H3zm10.5 3l2.5 2.5 1.5-1.5-2.5-2.5 2.5-2.5-1.5-1.5-2.5 2.5-2.5-2.5-1.5 1.5 2.5 2.5-2.5 2.5 1.5 1.5 2.5-2.5z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMuted = value is bool b && b;
        return isMuted ? VolumeMutedPath : VolumeOnPath;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class ChannelTypeToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChannelType type && parameter is string targetTypeStr)
        {
            var matches = type.ToString().Equals(targetTypeStr, StringComparison.OrdinalIgnoreCase);
            return matches ? true : false;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BoolToDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var trueValue = 1d;
        var falseValue = 0d;

        if (parameter is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                _ = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out trueValue);
                _ = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out falseValue);
            }
        }

        return boolValue ? trueValue : falseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EqualityToBrushConverter : IValueConverter
{
    public IBrush TrueBrush { get; set; } = Brushes.Transparent;
    public IBrush FalseBrush { get; set; } = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEqual = value == null && parameter == null || value?.Equals(parameter) == true;
        return isEqual ? TrueBrush : FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EqualityToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEqual = value == null && parameter == null || value?.Equals(parameter) == true;
        return isEqual ? true : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}




