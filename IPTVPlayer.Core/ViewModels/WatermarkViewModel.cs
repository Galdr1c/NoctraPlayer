using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Timers; 
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

public partial class WatermarkViewModel : ObservableObject, IDisposable
{
    private readonly System.Timers.Timer _shiftTimer;
    private readonly Random _random = new();
    private readonly ILicenseService _licenseService;
    
    [ObservableProperty]
    private double _opacity = 0.12;
    
    [ObservableProperty]
    private double _translateX = 0;
    
    [ObservableProperty]
    private double _translateY = 0;

    [ObservableProperty]
    private bool _isVisible;

    // Constructor Injection
    public WatermarkViewModel(ILicenseService licenseService)
    {
        _licenseService = licenseService;

        _shiftTimer = new System.Timers.Timer(TimeSpan.FromSeconds(60).TotalMilliseconds);
        _shiftTimer.Elapsed += ShiftTimer_Elapsed;
        _shiftTimer.Start();
        
        // Initial random position
        ShiftPosition();

        // Check license status
        UpdateVisibility();

        // Subscribe to changes
        _licenseService.SubscriptionChanged += OnSubscriptionChanged;
    }

    private void OnSubscriptionChanged()
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        IsVisible = !_licenseService.IsFeatureAvailable(LicenseService.Features.AdFree);
    }

    private void ShiftTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        ShiftPosition();
    }

    private void ShiftPosition()
    {
        // Shift within a small range (-20 to +20 px)
        // Note: In WinUI/WPF bound properties generally notify on UI thread automatically if updated from View, 
        // but updating form VM background thread might need dispatching. 
        // For Core, we just set the property. The UI framework binding engine usually handles it or we need a dispatcher service.
        // For now, we leave it as simple assignment.
        TranslateX = _random.Next(-20, 21);
        TranslateY = _random.Next(-20, 21);
    }

    public void Dispose()
    {
        _shiftTimer.Stop();
        _shiftTimer.Dispose();
        _licenseService.SubscriptionChanged -= OnSubscriptionChanged;
    }
}
