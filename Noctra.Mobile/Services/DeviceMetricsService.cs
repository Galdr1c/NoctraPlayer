using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Noctra.Mobile.Services;

/// <summary>
/// Cihaz sınıfını tespit eder ve responsive layout kararları için merkezi kaynak sağlar.
/// Responsive Audit (47 bulgu) çözümü: Katman 1 — Device-class tespiti.
///
/// 4 cihaz sınıfı (Material Design 3 + Apple HIG referans):
///   Compact  : < 360 dp  (iPhone SE 1st, eski telefonlar)
///   Phone    : < 600 dp  (standart telefonlar, 5.5" - 6.7")
///   Tablet   : < 900 dp  (7-10" tablet portrait)
///   Desktop  : >= 900 dp (büyük tablet landscape, desktop, Android TV)
///
/// Kullanım:
///   MainView.OnSizeChanged → DeviceMetricsService.Instance.ApplySize(w, h)
///   View'lar: {DynamicResource FBody} gibi token'lar otomatik ölçeklenir.
/// </summary>
public sealed class DeviceMetricsService : INotifyPropertyChanged
{
    public static DeviceMetricsService Instance { get; } = new();

    public enum DeviceClass
    {
        Compact,
        Phone,
        Tablet,
        Desktop
    }

    private DeviceClass _current = DeviceClass.Phone;
    private double _densityScale = 1.0;
    private double _widthDip = 360;
    private double _heightDip = 640;

    /// <summary>Mevcut cihaz sınıfı.</summary>
    public DeviceClass Current
    {
        get => _current;
        private set
        {
            if (_current == value) return;
            _current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCompact));
            OnPropertyChanged(nameof(IsPhone));
            OnPropertyChanged(nameof(IsTablet));
            OnPropertyChanged(nameof(IsDesktop));
        }
    }

    /// <summary>
    /// 360 dp referans genişliğine göre density scale.
    /// Compact'ta 0.85'e kadar düşer (ama touch target'lar korunur),
    /// tablette 1.4, desktop/TV'de 1.6'ya kadar çıkar.
    /// </summary>
    public double DensityScale
    {
        get => _densityScale;
        private set
        {
            if (Math.Abs(_densityScale - value) < 0.001) return;
            _densityScale = value;
            OnPropertyChanged();
        }
    }

    public double WidthDip
    {
        get => _widthDip;
        private set { if (Math.Abs(_widthDip - value) < 0.5) return; _widthDip = value; OnPropertyChanged(); }
    }

    public double HeightDip
    {
        get => _heightDip;
        private set { if (Math.Abs(_heightDip - value) < 0.5) return; _heightDip = value; OnPropertyChanged(); }
    }

    // Convenience flags (XAML binding için)
    public bool IsCompact => Current == DeviceClass.Compact;
    public bool IsPhone => Current == DeviceClass.Phone;
    public bool IsTablet => Current == DeviceClass.Tablet;
    public bool IsDesktop => Current == DeviceClass.Desktop;

    /// <summary>
    /// MainView.OnSizeChanged'tan çağrılır. Pencere boyutu değişince
    /// cihaz sınıfını günceller ve tüm bağlı view'ları otomatik yeniler.
    /// </summary>
    public void ApplySize(double widthDip, double heightDip)
    {
        WidthDip = widthDip;
        HeightDip = heightDip;

        // Kısa kenar (compact telefon, landscape tablet hepsi buraya girer)
        var shortSide = Math.Min(widthDip, heightDip);

        Current = shortSide switch
        {
            < 360 => DeviceClass.Compact,
            < 600 => DeviceClass.Phone,
            < 900 => DeviceClass.Tablet,
            _ => DeviceClass.Desktop
        };

        // 360 dp referans. Compact'ta 0.85, tablette 1.4, TV/desktop'ta 1.6.
        DensityScale = Math.Clamp(shortSide / 360.0, 0.85, 1.6);

        // Tüm DynamicResource token'larını güncelle
        UpdateResourceTokens();
    }

    /// <summary>
    /// Uygulama başlangıcında TopLevel'dan ilk boyutu okur.
    /// App.axaml.cs OnFrameworkInitializationCompleted'ta çağrılır.
    /// </summary>
    public void InitializeFromTopLevel()
    {
        try
        {
            var app = Application.Current;
            if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { Bounds.Width: > 0 } window)
            {
                ApplySize(window.Bounds.Width, window.Bounds.Height);
                return;
            }

            if (app?.ApplicationLifetime is ISingleViewApplicationLifetime single
                && single.MainView is { Bounds.Width: > 0 } mainView)
            {
                ApplySize(mainView.Bounds.Width, mainView.Bounds.Height);
                return;
            }
        }
        catch
        {
            // Fallback: telefon varsayılan
        }

        // Mobil default: telefon portrait
        ApplySize(360, 640);
    }

    // ─── Dinamik Token Güncelleme ─────────────────────────────────────────

    // Baz değerler (360dp telefon referansı, Tokens.axaml ile eşleşir)
    // Font token'larının baz değerleri
    private static readonly Dictionary<string, double> BaseFontTokens = new()
    {
        ["FDisplayLarge"] = 36,
        ["FDisplay"]      = 32,
        ["FDisplaySmall"] = 28,
        ["FHeadline"]     = 22,
        ["FTitleL"]       = 18,
        ["FTitleM"]       = 16,
        ["FBodyL"]        = 15,
        ["FBody"]         = 14,
        ["FCaption"]      = 12,
        ["FOverline"]     = 10,
    };

    // Spacing token'larının baz değerleri
    private static readonly Dictionary<string, double> BaseSpacingTokens = new()
    {
        ["S1"] = 4,
        ["S2"] = 8,
        ["S3"] = 12,
        ["S4"] = 16,
        ["S5"] = 20,
        ["S6"] = 24,
        ["S7"] = 32,
        ["S8"] = 40,
    };

    // Dimension token'larının baz değerleri
    private static readonly Dictionary<string, double> BaseDimensionTokens = new()
    {
        ["BottomNavHeight"]   = 62,
        ["HeaderHeight"]      = 56,
        ["NavRailWidth"]      = 72,
        ["AvatarSizeS"]       = 40,
        ["AvatarSizeM"]       = 56,
        ["AvatarSizeL"]       = 80,
        ["TouchTarget"]       = 44,
        ["CardMinHeight"]     = 120,
        ["ThumbnailWidth"]    = 48,
        ["ThumbnailHeight"]   = 64,
        ["SheetMaxHeight"]    = 560,
        ["SheetMaxHeightLarge"]       = 620,
        ["SheetScrollMaxHeight"]      = 220,
        ["SheetScrollMaxHeightMedium"] = 330,
    };

    /// <summary>
    /// DensityScale'e göre tüm token'ları yeniden hesaplar ve
    /// Application.Current.Resources'a yazar. DynamicResource binding'leri
    /// otomatik olarak güncellenir.
    /// </summary>
    private void UpdateResourceTokens()
    {
        var app = Application.Current;
        if (app is null) return;

        var resources = app.Resources;
        var scale = DensityScale;

        // Font token'ları — ölçekle ama minimum okunabilirlik için alt sınır koy
        foreach (var (key, baseValue) in BaseFontTokens)
        {
            var scaled = Math.Round(baseValue * scale);
            // Font minimum 9dp altına düşmesin (erişilebilirlik)
            scaled = Math.Max(scaled, 9);
            resources[key] = scaled;
        }

        // Spacing token'ları — ölçekle ama 2dp'den küçük olmasın
        foreach (var (key, baseValue) in BaseSpacingTokens)
        {
            var scaled = Math.Round(baseValue * scale);
            scaled = Math.Max(scaled, 2);
            resources[key] = scaled;
        }

        // Dimension token'ları — ölçekle ama touch target minimum 40dp
        foreach (var (key, baseValue) in BaseDimensionTokens)
        {
            var scaled = Math.Round(baseValue * scale);
            if (key == "TouchTarget")
                scaled = Math.Max(scaled, 40);
            resources[key] = scaled;
        }

        // Thickness token'ları — yeniden hesapla
        var ps = Math.Round(16 * scale);
        var ps2 = Math.Round(12 * scale);
        var ps3 = Math.Round(24 * scale);
        var ps4 = Math.Round(18 * scale);
        var ps5 = Math.Round(28 * scale);
        resources["PagePadding"] = new Avalonia.Thickness(ps, ps2, ps, ps);
        resources["PagePaddingLarge"] = new Avalonia.Thickness(ps3, ps4, ps3, ps5);
        resources["CardPadding"] = new Avalonia.Thickness(Math.Round(14 * scale));
        resources["CardPaddingLarge"] = new Avalonia.Thickness(Math.Round(20 * scale));
        resources["SheetPadding"] = new Avalonia.Thickness(ps, ps2);
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
