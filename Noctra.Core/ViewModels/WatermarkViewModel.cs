using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Timers; 
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

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
        _dispatcherService.BeginInvoke(() => 
        {
            IsVisible = !_licenseService.IsFeatureAvailable(LicenseService.Features.AdFree);
        });
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (value)
        {
            ShiftPosition();
            _shiftTimer.Start();
        }
        else
        {
            _shiftTimer.Stop();
        }
    }

    private void ShiftTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        ShiftPosition();
    }

    private void ShiftPosition()
    {
        // The watermark is anchored to the bottom-right, so only move it inward.
        if (!IsVisible)
        {
            return;
        }

        _dispatcherService.BeginInvoke(() =>
        {
            TranslateX = _random.Next(-20, 1);
            TranslateY = _random.Next(-20, 1);
        });
    }

    public void Dispose()
    {
        _shiftTimer.Stop();
        _shiftTimer.Dispose();
        _licenseService.SubscriptionChanged -= OnSubscriptionChanged;
    }
}

