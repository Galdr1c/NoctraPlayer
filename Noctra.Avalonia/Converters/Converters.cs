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
using Avalonia.Styling;
using Noctra.Models;
using Noctra.ViewModels;
using Material.Icons;
using Noctra.Avalonia.Localization;

namespace Noctra.Avalonia.Converters;

public class SleepModeToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Brushes.Transparent;

        bool isEqual = value.ToString() == parameter.ToString();

        if (isEqual)
        {
            if (Application.Current?.TryGetResource("AccentBrush", out var accent) == true && accent is IBrush brush)
            {
                return brush;
            }
            return new SolidColorBrush(Color.Parse("#8B5CF6")); // Fallback accent
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class DoubleToStarGridLengthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return new GridLength(Math.Max(0.0001, d), GridUnitType.Star);
        }
        return new GridLength(0.0001, GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

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

        if (width < 520) return 1;
        if (width < 780) return 2;
        if (width < 1080) return 3;
        if (width < 1380) return 4;
        if (width < 1680) return 5;
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

public class EpisodeWatchedProgressVisibilityConverter : IValueConverter
{
    private static readonly double MinWatchSeconds = TimeSpan.FromMinutes(1).TotalSeconds;
    private static readonly double CompletionTailSeconds = TimeSpan.FromMinutes(5).TotalSeconds;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Episode episode)
        {
            return false;
        }

        if (episode.IsCompleted)
        {
            return false;
        }

        var watchedSeconds = episode.WatchedPosition?.TotalSeconds ?? 0d;
        if (watchedSeconds < MinWatchSeconds)
        {
            return false;
        }

        if (episode.Duration is not TimeSpan duration || duration.TotalSeconds <= 0)
        {
            return true;
        }

        var totalSeconds = duration.TotalSeconds;
        var percent = (watchedSeconds / totalSeconds) * 100d;
        var remainingSeconds = Math.Max(0d, totalSeconds - watchedSeconds);

        return percent >= 90d || (totalSeconds > CompletionTailSeconds && remainingSeconds <= CompletionTailSeconds)
            ? false
            : true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EpisodeWatchedProgressWidthConverter : IValueConverter
{
    private static readonly double MinWatchSeconds = TimeSpan.FromMinutes(1).TotalSeconds;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Episode episode || episode.IsCompleted)
        {
            return 0d;
        }

        var maxWidth = 150d;
        if (parameter != null &&
            double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth))
        {
            maxWidth = parsedWidth;
        }

        var watchedSeconds = episode.WatchedPosition?.TotalSeconds ?? 0d;
        if (watchedSeconds < MinWatchSeconds)
        {
            return 0d;
        }

        double percent;
        if (episode.Duration is TimeSpan duration && duration.TotalSeconds > 0)
        {
            percent = (watchedSeconds / duration.TotalSeconds) * 100d;
        }
        else
        {
            var watchedMinutes = watchedSeconds / 60d;
            percent = Math.Clamp(8d + watchedMinutes * 2.2d, 8d, 88d);
        }

        return maxWidth * (Math.Clamp(percent, 0d, 100d) / 100d);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EpisodeCompletionBadgeVisibilityConverter : IValueConverter
{
    private static readonly double CompletionTailSeconds = TimeSpan.FromMinutes(5).TotalSeconds;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Episode episode)
        {
            return false;
        }

        if (episode.IsCompleted)
        {
            return true;
        }

        var watchedSeconds = episode.WatchedPosition?.TotalSeconds ?? 0d;
        if (watchedSeconds <= 0)
        {
            return false;
        }

        if (episode.Duration is not TimeSpan duration || duration.TotalSeconds <= 0)
        {
            return false;
        }

        var totalSeconds = duration.TotalSeconds;
        var percent = (watchedSeconds / totalSeconds) * 100d;
        var remainingSeconds = Math.Max(0d, totalSeconds - watchedSeconds);

        return percent >= 90d || (totalSeconds > CompletionTailSeconds && remainingSeconds <= CompletionTailSeconds);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class EpisodeIdentityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Episode episode)
        {
            return string.Empty;
        }

        if (episode.Id > 0)
        {
            return $"id:{episode.Id}";
        }

        return !string.IsNullOrWhiteSpace(episode.StreamUrl)
            ? $"url:{episode.StreamUrl.Trim()}"
            : string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class MediaSupportsFavoriteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Channel || value is Series;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class MediaSupportsMyListConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            Series => true,
            Channel channel => channel.Type != ChannelType.Live,
            _ => false
        };
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
            return LocalizationSource.Instance["Common.Unknown"];
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
        var trueText = LocalizationSource.Instance["Common.Yes"];
        var falseText = LocalizationSource.Instance["Common.No"];

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
        if (values.Count < 2 || values[0] == null || values[1] == null)
            return false;

        string v1 = values[0]?.ToString() ?? string.Empty;
        string v2 = values[1]?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(v1) || string.IsNullOrEmpty(v2))
            return false;

        return string.Equals(v1, v2, StringComparison.Ordinal);
    }
}

public class EqualityToThicknessMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] == null || values[1] == null) 
            return new Thickness(0);

        string v1 = values[0]?.ToString() ?? string.Empty;
        string v2 = values[1]?.ToString() ?? string.Empty;
        
        if (string.IsNullOrEmpty(v1) || string.IsNullOrEmpty(v2))
            return new Thickness(0);

        var isEqual = string.Equals(v1, v2, StringComparison.Ordinal);

        if (isEqual && parameter is string p && double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var thickness))
        {
            return new Thickness(thickness);
        }

        return new Thickness(0);
    }
}

public class EqualityToBrushMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] == null || values[1] == null)
            return Brushes.Transparent;

        string v1 = values[0]?.ToString() ?? string.Empty;
        string v2 = values[1]?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(v1) || string.IsNullOrEmpty(v2))
            return Brushes.Transparent;

        var isEqual = string.Equals(v1, v2, StringComparison.Ordinal);

        if (isEqual && parameter is string resourceKey)
        {
            if (Application.Current?.TryGetResource(resourceKey, out var resource) == true && resource is IBrush brush)
            {
                return brush;
            }

            if (resourceKey.Equals("AccentBrush", StringComparison.OrdinalIgnoreCase)) return Brushes.MediumPurple;
        }

        return Brushes.Transparent;
    }
}

public class BoolAndMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count == 0) return false;
        
        foreach (var value in values)
        {
            if (value is not bool b || !b) return false;
        }
        
        return true;
    }
}

public class BoolOrMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var val in values)
        {
            if (val is bool b && b) return true;
        }
        return false;
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
    private const string AvatarAssetPrefix = "avares://Noctra/Assets/Avatars/";
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

public class BoolToMaterialIconKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var param = parameter as string;
        if (string.IsNullOrEmpty(param)) return null;

        var parts = param.Split('|');
        if (parts.Length != 2) return null;

        var iconName = boolValue ? parts[0] : parts[1];
        if (Enum.TryParse<MaterialIconKind>(iconName, true, out var kind))
        {
            return kind;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
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

public class BoolToBrushConverter : IValueConverter
{
    public IBrush? TrueBrush { get; set; }
    public IBrush? FalseBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? TrueBrush : FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class BoolToThicknessConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var trueValue = new Thickness(0);
        var falseValue = new Thickness(0);

        if (parameter is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                trueValue = Thickness.Parse(parts[0]);
                falseValue = Thickness.Parse(parts[1]);
            }
        }

        return boolValue ? trueValue : falseValue;
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

public class EqualityToResourceBrushConverter : IValueConverter
{
    public string ResourceKey { get; set; } = "Surface1Brush";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Brushes.Transparent;

        bool isEqual = value.ToString() == parameter.ToString();

        if (isEqual && Application.Current?.TryGetResource(ResourceKey, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
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



public class ConnectionHealthToBrushConverter : IValueConverter
{
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse("#4CAF50")); // Green
    private static readonly IBrush WeakBrush = new SolidColorBrush(Color.Parse("#FFC107")); // Amber
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#FF5722"));  // Deep Orange
    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#F44336")); // Red
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#9E9E9E")); // Grey

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConnectionHealth health)
        {
            return health switch
            {
                ConnectionHealth.Good => GoodBrush,
                ConnectionHealth.Weak => WeakBrush,
                ConnectionHealth.Bad => BadBrush,
                ConnectionHealth.Critical => CriticalBrush,
                _ => UnknownBrush
            };
        }
        return UnknownBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class ConnectionHealthToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConnectionHealth health)
        {
            return health switch
            {
                ConnectionHealth.Good => "CheckCircle",
                ConnectionHealth.Weak => "AlertCircle",
                ConnectionHealth.Bad => "Alert",
                ConnectionHealth.Critical => "CloseCircle",
                _ => "HelpCircle"
            };
        }
        return "HelpCircle";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>
/// Converts ActualThemeVariant to the appropriate theme-aware logo Bitmap.
/// ConverterParameter="Full" selects the full/transparent variant.
/// </summary>
public class LogoThemeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = value is ThemeVariant variant && variant == ThemeVariant.Dark;
        var isFull = parameter?.ToString() == "Full";

        var uri = isFull
            ? (isDark
                ? "avares://Noctra/Assets/Square150x150LogoTPFullLight.png"
                : "avares://Noctra/Assets/Square150x150LogoTPFullDark.png")
            : (isDark
                ? "avares://Noctra/Assets/Square150x150LogoTPLight.png"
                : "avares://Noctra/Assets/Square150x150LogoTPDark.png");

        try
        {
            var assetUri = new Uri(uri);
            if (AssetLoader.Exists(assetUri))
            {
                using var stream = AssetLoader.Open(assetUri);
                return new Bitmap(stream);
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

public class ConnectionHealthToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConnectionHealth health)
        {
            return health != ConnectionHealth.Unknown;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class StringNotEmptyToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BytesToHumanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long bytes = 0;
        if (value is long l) bytes = l;
        else if (value is int i) bytes = i;
        else if (value is double d) bytes = (long)d;
        else if (value != null && long.TryParse(value.ToString(), out var parsed)) bytes = parsed;

        if (bytes <= 0) return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var doubleValue = (double)bytes;
        var unitIndex = 0;
        while (doubleValue >= 1024 && unitIndex < units.Length - 1)
        {
            doubleValue /= 1024;
            unitIndex++;
        }

        return $"{doubleValue:N2} {units[unitIndex]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class DownloadStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DownloadStatus status)
        {
            return status switch
            {
                DownloadStatus.Paused => Application.Current?.TryGetResource("WarningBrush", out var warning) == true ? warning : Brushes.Orange,
                DownloadStatus.Failed => Application.Current?.TryGetResource("ErrorBrush", out var error) == true ? error : Brushes.Red,
                _ => Application.Current?.TryGetResource("AccentBrush", out var accent) == true ? accent : Brushes.Purple
            };
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
public class DownloadStatusToLocalizedTextConverter : IValueConverter
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
public class DownloadEtaTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadItem item ||
            !item.EstimatedSecondsRemaining.HasValue ||
            item.EstimatedSecondsRemaining.Value <= 0)
        {
            return string.Empty;
        }

        var ts = TimeSpan.FromSeconds(item.EstimatedSecondsRemaining.Value);
        var source = LocalizationSource.Instance;

        if (ts.TotalHours >= 1)
        {
            return string.Format(CultureInfo.CurrentCulture, source["Downloads.Eta.HoursFormat"], (int)ts.TotalHours, ts.Minutes);
        }

        if (ts.TotalMinutes >= 1)
        {
            return string.Format(CultureInfo.CurrentCulture, source["Downloads.Eta.MinutesFormat"], (int)ts.TotalMinutes, ts.Seconds);
        }

        return string.Format(CultureInfo.CurrentCulture, source["Downloads.Eta.SecondsFormat"], ts.Seconds);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
public class DownloadSpeedTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadItem item || item.SpeedBytesPerSecond <= 0)
        {
            return "-";
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationSource.Instance["Downloads.Speed.PerSecondFormat"],
            DownloadItem.FormatBytes((long)item.SpeedBytesPerSecond));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
public class FillModeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerViewModel.FillMode mode)
        {
            return mode switch
            {
                PlayerViewModel.FillMode.Fit => MaterialIconKind.AspectRatio,
                PlayerViewModel.FillMode.Fill => MaterialIconKind.CropFree,
                PlayerViewModel.FillMode.Stretch => MaterialIconKind.ArrowExpandAll,
                PlayerViewModel.FillMode.Original => MaterialIconKind.ImageSizeSelectActual,
                _ => MaterialIconKind.AspectRatio
            };
        }
        return MaterialIconKind.AspectRatio;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BoolToFavoriteIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFavorite)
        {
            return isFavorite ? MaterialIconKind.Heart : MaterialIconKind.HeartOutline;
        }
        return MaterialIconKind.HeartOutline;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class PinDotConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pinLen = value is int i ? i : 0;
        var dotIndex = 0;
        if (parameter != null) int.TryParse(parameter.ToString(), out dotIndex);

        var filled = pinLen >= dotIndex;

        if (filled && Application.Current?.TryGetResource("AccentBrush", out var accentRes) == true && accentRes is IBrush accentBrush)
            return accentBrush;

        if (Application.Current?.TryGetResource("BorderBrush", out var borderRes) == true && borderRes is IBrush borderBrush)
            return borderBrush;

        return filled
            ? new SolidColorBrush(Color.Parse("#8B5CF6"))
            : new SolidColorBrush(Color.Parse("#333333"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class UrgencyToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush OrangeBrush = new(Color.Parse("#FF8C00"));
    private static readonly SolidColorBrush RedOrangeBrush = new(Color.Parse("#FF4500"));
    private static readonly SolidColorBrush RedBrush = new(Color.Parse("#FF1744"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var urgency = value is double d ? d : 1.0;

        if (urgency >= 0.6) return OrangeBrush;
        if (urgency >= 0.3) return RedOrangeBrush;
        return RedBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class TimeSpanToCountdownConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan ts || ts <= TimeSpan.Zero)
            return LocalizationSource.Instance["Profile.Deletion.Deleting"];

        if (ts.TotalDays >= 1)
            return string.Format(LocalizationSource.Instance["Profile.Deletion.DaysLeftFormat"], (int)ts.TotalDays, ts.Hours);

        if (ts.TotalHours >= 1)
            return string.Format(LocalizationSource.Instance["Profile.Deletion.HoursLeftFormat"], (int)ts.TotalHours, ts.Minutes);

        return string.Format(LocalizationSource.Instance["Profile.Deletion.MinutesLeftFormat"], ts.Minutes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class StringFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter == null) return value?.ToString();

        var format = parameter.ToString();
        if (string.IsNullOrEmpty(format)) return value?.ToString();

        // If format looks like a localization key, try to translate it
        // Keys usually don't have spaces and often contain dots
        if (!format.Contains(' ') && format.Contains('.'))
        {
            var translated = LocalizationSource.Instance[format];
            if (translated != format)
            {
                format = translated;
            }
        }

        try
        {
            // Handle collection count automatically
            if (value is ICollection collection)
            {
                return string.Format(culture, format, collection.Count);
            }

            return string.Format(culture, format, value);
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class BooleanToSuccessWarningBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSuccess = value is bool b && b;
        var resourceKey = isSuccess ? "SuccessBrush" : "WarningBrush";

        if (Application.Current?.TryGetResource(resourceKey, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return isSuccess ? Brushes.Green : Brushes.Red;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

// ═══════════════════════════════════════════════════════════════════════════
// EPG Timeline Converters
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// double pixelLeft → Thickness(pixelLeft, 0, 0, 0)
/// EPG program bloklarını Canvas yerine Grid+Margin ile mutlak konumlandırır.
/// </summary>
public class DoubleToLeftMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? new Thickness(Math.Max(0, d), 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool isCurrentProgram → Color (şu an yayında ise vurgulu, değilse nötr)
/// </summary>
public class BoolToEpgBlockColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb(50, 123, 47, 190)   // Accent rengi yarı saydam
            : Color.FromArgb(30, 255, 255, 255);  // Beyaz çok şeffaf

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool isCurrentProgram → Border sol çizgi rengi
/// </summary>
public class BoolToEpgBorderColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb(220, 123, 47, 190)   // Accent tam opak
            : Color.FromArgb(25, 255, 255, 255);  // Çok şeffaf

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool isCurrentChannel → hafif vurgu fırçası (şu an izlenen kanal için satır arkaplanı)
/// </summary>
public class BoolToSelectionBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.FromArgb(20, 123, 47, 190))
            : new SolidColorBrush(Colors.Transparent);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool + parameter(opacity) → double opacity (past programs soluk görünür)
/// ConverterParameter="0.45" → isPast=true ise 0.45, false ise 1.0
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool b) return 1.0;
        if (!b) return 1.0;
        if (parameter is string s && double.TryParse(s, System.Globalization.NumberStyles.Float,
            CultureInfo.InvariantCulture, out var d))
            return d;
        return 0.45;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool + parameter(translateY_px) → TranslateY RenderTransform string
/// IsEpgPanelOpen=false → "translateY(480px)", true → "translateY(0)"
/// </summary>
public class BoolToTranslateYConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOpen = value is true;
        return isOpen ? "translateY(0)" : $"translateY({parameter ?? 480}px)";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool isOpen + ConverterParameter=panelHeight →
///   false: Margin(0, panelHeight, 0, -panelHeight)  panel ekran altında
///   true:  Margin(0, 0, 0, 0)                        panel görünür
/// ThicknessTransition ile kayma animasyonu çalışır.
/// </summary>
public class BoolToEpgPanelMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOpen = value is true;
        if (isOpen) return new Thickness(0);

        double h = 480;
        if (parameter is string s && double.TryParse(s, System.Globalization.NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed))
            h = parsed;

        return new Thickness(0, h, 0, -h);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
