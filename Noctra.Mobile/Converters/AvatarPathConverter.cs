using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Noctra.Mobile.Converters;

public sealed class AvatarPathConverter : IValueConverter
{
    private const string DefaultAvatarFileName = "avatar_1.png";
    private const string AvatarAssetPrefix = "avares://Noctra.Mobile/Assets/Avatars/";
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string avatar || string.IsNullOrWhiteSpace(avatar))
        {
            return GetOrLoadAvatar(DefaultAvatarFileName);
        }

        var normalized = NormalizeAvatarFileName(avatar);
        return GetOrLoadAvatar(normalized)
            ?? (!string.Equals(normalized, DefaultAvatarFileName, StringComparison.OrdinalIgnoreCase)
                ? GetOrLoadAvatar(DefaultAvatarFileName)
                : null);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;

    private static string NormalizeAvatarFileName(string avatar)
    {
        var normalized = avatar.Trim().Trim('"', '\'').Replace('\\', '/');
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

        if (!Uri.TryCreate($"{AvatarAssetPrefix}{avatarFileName}", UriKind.Absolute, out var uri) ||
            !AssetLoader.Exists(uri))
        {
            return null;
        }

        try
        {
            using var stream = AssetLoader.Open(uri);
            var bitmap = new Bitmap(stream);
            lock (CacheLock)
            {
                if (BitmapCache.TryGetValue(avatarFileName, out var existing))
                {
                    bitmap.Dispose();
                    return existing;
                }

                BitmapCache[avatarFileName] = bitmap;
                return bitmap;
            }
        }
        catch
        {
            return null;
        }
    }
}
