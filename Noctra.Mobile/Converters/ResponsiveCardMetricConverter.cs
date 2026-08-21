using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

/// <summary>
/// Calculates responsive card metrics from the available ItemsControl width.
/// The converter intentionally keeps the math simple and predictable:
/// - poster cards: 2 columns on phones, more columns as the viewport grows;
/// - continue cards: 1 column on phones, 2+ columns on larger/tablet widths.
/// - live cards: 1 column on phones, 2+ columns on tablets.
///
/// ConverterParameter values:
/// - "posterWidth" / "posterHeight"
/// - "liveWidth"
/// - "continueWidth" / "continueHeight"
/// - "moreShortcutWidth"
/// - "profileWidth" / "profileHeight"
/// </summary>
public sealed class ResponsiveCardMetricConverter : IValueConverter
{
    // Hysteresis: scroll sırasında scrollbar görünür/gizlenir veya küçük layout
    // titreşimleri Bounds.Width'i 1-15px oynatabilir. Aynı column sayısı içinde
    // kart genişliğinin sürekli değişip "shimmer" etmesini engellemek için, son
    // hesaplanan genişliği mode bazında cache'ler ve sadece column sayısı gerçekten
    // değiştiğinde veya anlamlı bir genişlik farkı (>8px) oluştuğunda günceller.
    private static double? _lastAvailableWidth;
    private static double _lastComputedWidth;
    private static string? _lastMode;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var availableWidth = ToDouble(value);
        var mode = parameter?.ToString() ?? "posterWidth";

        var profile = CardMetricProfile.For(mode);

        // KRİTİK: ItemsControl.Bounds.Width ilk measure pass'ta NaN/0 gelebilir.
        // Bu durumda converter DefaultWidth döner, WrapPanel bunu content olarak
        // ölçer, ItemsControl content'e göre küçülür → KİLİTLENME (1 kart kalır).
        // Çözüm: NaN/0 ise TopLevel (main window) genişliğini oku, margin düşür.
        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            availableWidth = GetTopLevelWidth();
        }

        // Aynı mode ve yaklaşık aynı genişlik: cache'lenen değeri döndür (shimmer önler).
        // 8px hysteresis bandı: scrollbar/aşırı render titreşimlerini filtreler.
        if (_lastMode == mode &&
            _lastAvailableWidth.HasValue &&
            Math.Abs(_lastAvailableWidth.Value - availableWidth) <= 8)
        {
            return mode.EndsWith("Height", StringComparison.OrdinalIgnoreCase)
                ? Math.Round(_lastComputedWidth * profile.HeightRatio)
                : _lastComputedWidth;
        }

        var width = CalculateWidth(availableWidth, profile);

        // Quantization: kart genişliğini 2px'lik birimlere yuvarla. Bu, sub-pixel
        // titreşimlerini ve WrapPanel'in sürekli yeniden düzenlenmesini engeller.
        width = Math.Floor(width / 2) * 2;

        _lastAvailableWidth = availableWidth;
        _lastComputedWidth = width;
        _lastMode = mode;

        if (mode.EndsWith("Height", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(width * profile.HeightRatio);
        }

        return width;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double CalculateWidth(double availableWidth, CardMetricProfile profile)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return profile.DefaultWidth;
        }

        var usableWidth = Math.Max(0, availableWidth);
        var columns = Math.Max(1, (int)Math.Floor((usableWidth + profile.Gap) / (profile.MinWidth + profile.Gap)));
        columns = Math.Min(columns, profile.MaxColumns);

        var width = Math.Floor((usableWidth - profile.Gap * (columns - 1)) / columns);
        width = Math.Max(1, width);

        if (width > profile.MaxWidth)
        {
            width = profile.MaxWidth;
        }

        if (width < profile.MinWidth && usableWidth >= profile.MinWidth)
        {
            width = profile.MinWidth;
        }
        else if (usableWidth < profile.MinWidth)
        {
            width = Math.Max(1, usableWidth);
        }

        return width;
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            short s => s,
            string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d
        };
    }

    /// <summary>
    /// ItemsControl.Bounds.Width ilk measure pass'ta NaN/0 geldiğinde fallback.
    /// TopLevel (main window) genişliğini okur, mobil StackPanel margin (48px) düşer.
    /// Phone ~390, tablet portrait ~768, tablet landscape ~1024.
    /// Mobilde TopLevel erişimi her zaman mümkün değil; bu yüzden 768 güvenli default.
    /// </summary>
    private static double GetTopLevelWidth()
    {
        try
        {
            var app = Application.Current;
            if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { Bounds.Width: > 0 } window)
            {
                return Math.Max(0, window.Bounds.Width - 48);
            }
        }
        catch
        {
            // fallback aşağıda
        }

        // Mobil (Android/iOS) lifetime: TopLevel static bulunamaz.
        // 720px (768-48) tablet portrait için en güvenli orta yol.
        return 720;
    }

    private readonly record struct CardMetricProfile(
        double MinWidth,
        double MaxWidth,
        double DefaultWidth,
        double HeightRatio,
        double Gap,
        int MaxColumns)
    {
        public static CardMetricProfile For(string mode)
        {
            if (mode.StartsWith("continue", StringComparison.OrdinalIgnoreCase))
            {
                // Landscape cards: one wide card on phones, two or three on larger screens.
                return new CardMetricProfile(220, 410, 270, 0.56, 16, 4);
            }

            if (mode.StartsWith("live", StringComparison.OrdinalIgnoreCase))
            {
                // Live cards: tablet portrait'da en az 2, yatayda 3-4 kart.
                // MinWidth 200: daha dar ekranlarda 2. kart rahatça sığsın.
                // MaxColumns 4: yatay tablette 4 kart.
                // MaxWidth 420: büyük ekranda kartlar aşırı büyümesin.
                return new CardMetricProfile(220, 410, 270, 0.30, 16, 4);
            }

            if (mode.StartsWith("avatarTile", StringComparison.OrdinalIgnoreCase))
            {
                // Avatar picker için ayrı mod (K-3 çözümü): daha küçük kareler,
                // telefonda 4-6 sütun sığar. Eski profileWidth paylaşımı kalktı.
                return new CardMetricProfile(128, 192, 128, 1.0, 8, 6);
            }

            if (mode.StartsWith("profile", StringComparison.OrdinalIgnoreCase))
            {
                // Profile cards: min 2 per row on5.5" phones (~360px).
                // Gap 32 accounts for button Margin="8,8" (8px left + 8px right per card = 16px,
                // plus 8px edge margin on each side = total 32px per card horizontal space).
                // MinWidth 120 ensures 2 cards fit on 360px screens: (336-32)/2 = 152.
                return new CardMetricProfile(120, 200, 170, 1.0, 32, 5);
            }

            if (mode.StartsWith("moreShortcut", StringComparison.OrdinalIgnoreCase))
            {
                // More menu action tiles: two columns on phones, more on tablets.
                return new CardMetricProfile(150, 190, 160, 0.62, 12, 4);
            }

            // Poster cards: 2 columns on most phones, 3+ on foldables/tablets.
            // MinWidth 135: dar ekranlarda 2. kart rahatça sığsın.
            // MaxWidth 175: büyük ekranda kartlar aşırı büyümesin.
            return new CardMetricProfile(150, 180, 150, 1.50, 16, 6);
        }
    }
}
