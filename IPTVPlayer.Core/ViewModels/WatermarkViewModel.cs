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
    
    private readonly IDispatcherService _dispatcherService;
    
    [ObservableProperty]
    private double _opacity = 0.12;
    
    [ObservableProperty]
    private double _translateX = 0;
    
    [ObservableProperty]
    private double _translateY = 0;

    [ObservableProperty]
    private bool _isVisible;

    // Constructor Injection
    public WatermarkViewModel(ILicenseService licenseService, IDispatcherService dispatcherService)
    {
        _licenseService = licenseService;
        _dispatcherService = dispatcherService;

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
        _dispatcherService.Invoke(() => 
        {
            IsVisible = !_licenseService.IsFeatureAvailable(LicenseService.Features.AdFree);
        });
    }

    private void ShiftTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        ShiftPosition();
    }

    private void ShiftPosition()
    {
        // Shift within a small range (-20 to +20 px)
        _dispatcherService.Invoke(() =>
        {
            TranslateX = _random.Next(-20, 21);
            TranslateY = _random.Next(-20, 21);
        });
    }

    public void Dispose()
    {
        _shiftTimer.Stop();
        _shiftTimer.Dispose();
        _licenseService.SubscriptionChanged -= OnSubscriptionChanged;
    }
}
