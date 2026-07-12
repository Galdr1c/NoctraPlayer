using System;
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
///   Desktop  : >= 900 dp (büyük tablet landscape, desktop)
///
/// Kullanım:
///   MainView.OnSizeChanged → DeviceMetricsService.Instance.ApplySize(w, h)
///   View'lar: {Binding (DeviceMetricsService.Instance.IsTablet)} veya
///             DeviceMetricsService.Instance.Current ile kod-tarafı karar.
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
    /// tablette 1.4'e kadar çıkar. Clamp erişilebilirlik için.
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

        // 360 dp referans. Compact'ta 0.85, tablette 1.4'e kadar.
        // Clamp çok önemli: çok küçük olursa dokunma hedefleri ezilir.
        DensityScale = Math.Clamp(shortSide / 360.0, 0.85, 1.4);
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
