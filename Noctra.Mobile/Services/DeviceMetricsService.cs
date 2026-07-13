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
    /// 360 dp referans genişliğine göre, sadece FONT token'larına uygulanan ölçek.
    /// Spacing/padding/dimension artık bu değerden etkilenmiyor (bkz. UpdateResourceTokens).
    /// Piecewise-linear: 306dp'de 0.85 taban, 360dp'de 1.0, 600dp'de 1.08,
    /// 900dp'de 1.25, 1600dp+'da 1.4 tavan — eskisi gibi 576dp'de aniden
    /// tavana zıplamaz, Tablet ve Desktop/TV aralığının tamamına kademeli yayılır.
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

        // 360 dp referans, kademeli eğri (bkz. ComputeFontScale).
        DensityScale = ComputeFontScale(shortSide);

        // Sadece font token'larını güncelle (spacing/padding/dimension sabit).
        UpdateResourceTokens();
    }

    /// <summary>
    /// Sadece font ölçeklemesi için kullanılan piecewise-linear eğri.
    /// Sabit breakpoint'ler arasında lineer interpolasyon yapar, böylece
    /// Tablet (600-900dp) ve Desktop/TV (900dp+) aralıklarının tamamında
    /// kademeli bir artış olur; tek bir noktada tavana zıplama olmaz.
    /// </summary>
    private static double ComputeFontScale(double shortSide)
    {
        Span<(double Side, double Scale)> anchors =
        [
            (306, 0.85),   // Compact taban (360 * 0.85)
            (360, 1.00),   // Telefon referansı
            (600, 1.08),   // Phone sınıfının üst ucu — hafif büyüme
            (900, 1.25),   // Tablet sınıfının üst ucu
            (1600, 1.40),  // Desktop/TV — geniş ekranlarda kademeli tavan
        ];

        if (shortSide <= anchors[0].Side) return anchors[0].Scale;
        if (shortSide >= anchors[^1].Side) return anchors[^1].Scale;

        for (var i = 0; i < anchors.Length - 1; i++)
        {
            var (s0, v0) = anchors[i];
            var (s1, v1) = anchors[i + 1];
            if (shortSide > s1) continue;

            var t = (shortSide - s0) / (s1 - s0);
            return v0 + t * (v1 - v0);
        }

        return anchors[^1].Scale;
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

    // NOT: Spacing (S1-S8) ve dimension (BottomNavHeight, HeaderHeight, AvatarSizeS/M/L,
    // TouchTarget, ThumbnailWidth/Height, SheetMaxHeight*, PagePadding, CardPadding,
    // SheetPadding vb.) token'ları artık DEVICE SCALE'DEN ETKİLENMİYOR — bilinçli karar:
    // margin/padding/chrome ölçeklemesi kademeli olmayan bir tavana çok erken ulaşıyordu
    // (ör. Tablet sınıfının tamamı ve Desktop/TV aynı maksimum değeri paylaşıyordu) ve
    // layout'u bozuyordu. Bu token'lar artık sadece Tokens.axaml'daki statik (telefon
    // referanslı) değerlerini kullanır. Kart genişliği ve satır başına öğe sayısı gibi
    // gerçekten responsive olması gereken ölçüler zaten ayrı ve bağımsız bir mekanizma
    // olan Converters/ResponsiveCardMetricConverter.cs üzerinden, sabit bir gap ile
    // hesaplanıyor — o dosyaya bu değişiklikle dokunulmadı.

    /// <summary>
    /// DensityScale'e göre SADECE font token'larını yeniden hesaplar ve
    /// Application.Current.Resources'a yazar. DynamicResource binding'leri
    /// otomatik olarak güncellenir. Spacing/dimension/padding artık burada yok.
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
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
