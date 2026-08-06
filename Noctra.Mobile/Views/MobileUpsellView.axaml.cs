using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Localization;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.Mobile.Views;

public partial class MobileUpsellView : UserControl
{
    private IReadOnlyList<StoreProduct> _products = Array.Empty<StoreProduct>();

    public MobileUpsellView()
    {
        InitializeComponent();
    }

    public void Show()
    {
        IsVisible = true;
        _ = RefreshProductPricingAsync();
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

    /// <summary>
    /// Mağaza ürün fiyatlarını yükleyip plan kartlarına yazar. Mağaza
    /// desteklenmiyorsa (masaüstü) kartlar fiyatsız kalır ve tek "Buy"
    /// butonu mevcut akışı (URI) kullanır.
    /// </summary>
    private async Task RefreshProductPricingAsync()
    {
        try
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var store = app.EnsureServices()?.GetService<IStorePurchaseService>();
            if (store is not { IsSupported: true })
            {
                return;
            }

            _products = await store.GetProductsAsync();

            var monthly = _products.FirstOrDefault(p => p.Kind == StoreProductKind.Subscription);
            var lifetime = _products.FirstOrDefault(p => p.Kind == StoreProductKind.Lifetime);

            if (monthly is not null && !string.IsNullOrWhiteSpace(monthly.Price))
            {
                MonthlyPriceText.Text = monthly.Price;
            }
            else
            {
                MonthlyPriceText.Text = "—";
            }

            if (lifetime is not null && !string.IsNullOrWhiteSpace(lifetime.Price))
            {
                LifetimePriceText.Text = lifetime.Price;
            }
            else
            {
                LifetimePriceText.Text = "—";
            }

            PlanStatusText.IsVisible = _products.Count == 0;
            if (_products.Count == 0)
            {
                PlanStatusText.Text = LocalizationSource.Instance["Upsell.Plan.Unavailable"];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upsell] Product pricing refresh failed: {ex.Message}");
        }
    }

    private async void MonthlyBuy_Click(object? sender, RoutedEventArgs e)
    {
        await PurchaseAsync(StoreProductKind.Subscription);
    }

    private async void LifetimeBuy_Click(object? sender, RoutedEventArgs e)
    {
        await PurchaseAsync(StoreProductKind.Lifetime);
    }

    /// <summary>
    /// Seçilen türdeki mağaza ürünü için satın alma akışını başlatır;
    /// ürün bulunamazsa genel Premium akışına düşer.
    /// </summary>
    private async Task PurchaseAsync(StoreProductKind kind)
    {
        try
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var store = app.EnsureServices()?.GetService<IStorePurchaseService>();
            var product = _products.FirstOrDefault(p => p.Kind == kind);

            if (store is { IsSupported: true } && product is not null)
            {
                await store.LaunchPurchaseAsync(product);
            }
            else
            {
                var licenseService = app.EnsureServices()?.GetRequiredService<ILicenseService>();
                if (licenseService is not null)
                {
                    await licenseService.StartPurchaseFlowAsync(SubscriptionTier.Premium);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upsell] Purchase failed: {ex.Message}");
        }
        finally
        {
            TryClose();
        }
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
