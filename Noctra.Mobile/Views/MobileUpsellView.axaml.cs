using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.Mobile.Views;

public partial class MobileUpsellView : UserControl
{
    public MobileUpsellView()
    {
        InitializeComponent();
    }

    public void Show()
    {
        IsVisible = true;
    }

    public bool TryClose()
    {
        if (!IsVisible)
            return false;

        IsVisible = false;
        return true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        TryClose();
    }

    private async void Buy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Application.Current is App app)
            {
                var licenseService = app.EnsureServices()?.GetRequiredService<ILicenseService>();
                if (licenseService is not null)
                {
                    await licenseService.StartPurchaseFlowAsync(SubscriptionTier.Premium);
                }
            }
        }
        finally
        {
            TryClose();
        }
    }
}
