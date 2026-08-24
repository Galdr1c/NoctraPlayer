using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class WatermarkViewModel : ObservableObject, IDisposable
{
    private readonly ILicenseService _licenseService;
    
    private readonly IDispatcherService _dispatcherService;
    
    [ObservableProperty]
    private double _opacity = 0.24;

    [ObservableProperty]
    private bool _isVisible;

    // Constructor Injection
    public WatermarkViewModel(ILicenseService licenseService, IDispatcherService dispatcherService)
    {
        _licenseService = licenseService;
        _dispatcherService = dispatcherService;

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

    public void Dispose()
    {
        _licenseService.SubscriptionChanged -= OnSubscriptionChanged;
    }
}

