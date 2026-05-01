using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Views;

public partial class UpsellWindow : Window
{
    private readonly ILicenseService _licenseService;
    private readonly IAppEditionService _appEditionService;

    public UpsellWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<ILicenseService>(),
            ((App)Application.Current!).Services.GetRequiredService<IAppEditionService>())
    {
    }

    public UpsellWindow(ILicenseService licenseService, IAppEditionService appEditionService)
    {
        _licenseService = licenseService;
        _appEditionService = appEditionService;
        InitializeComponent();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void Buy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_appEditionService.IsFreeEdition)
            {
                await _licenseService.StartPurchaseFlowAsync(SubscriptionTier.Premium);
            }
        }
        finally
        {
            Close(true);
        }
    }

    private void ContinueFree_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
