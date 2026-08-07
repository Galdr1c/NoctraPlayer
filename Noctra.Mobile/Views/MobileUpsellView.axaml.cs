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
    private bool _isPurchasing;

    public MobileUpsellView()
    {
        InitializeComponent();
    }

    public void Show()
    {
        IsVisible = true;
        HideStatusMessages();
        RetryPricingButton.IsVisible = false;
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

    private void HideStatusMessages()
    {
        PlanStatusText.IsVisible = false;
        PurchaseStatusText.IsVisible = false;
        PurchaseInfoText.IsVisible = false;
    }

    /// <summary>
    /// İşlem sırasında tüm satın alma butonlarını devre dışı bırakır ve
    /// ana butonda spinner gösterir — çift tıklama / eşzamanlı akış olmaz.
    /// </summary>
    private void SetPurchasing(bool purchasing)
    {
        _isPurchasing = purchasing;
        BuyButton.IsEnabled = !purchasing;
        MonthlyBuyButton.IsEnabled = !purchasing;
        LifetimeBuyButton.IsEnabled = !purchasing;
        BuySpinner.IsVisible = purchasing;
    }

    /// <summary>
    /// Mağaza ürün fiyatlarını yükleyip plan kartlarına yazar. Mağaza
    /// desteklenmiyorsa (masaüstü) kartlar fiyatsız kalır ve tek "Buy"
    /// butonu mevcut akışı (URI) kullanır. Yükleme başarısız olursa fiyatlar
    /// "—" ile gösterilir ve yeniden deneme butonu görünür.
    /// </summary>
    private async Task RefreshProductPricingAsync()
    {
        HideStatusMessages();
        RetryPricingButton.IsVisible = false;

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

            MonthlyPriceText.Text = monthly is not null && !string.IsNullOrWhiteSpace(monthly.Price)
                ? monthly.Price
                : "—";

            LifetimePriceText.Text = lifetime is not null && !string.IsNullOrWhiteSpace(lifetime.Price)
                ? lifetime.Price
                : "—";

            if (_products.Count == 0)
            {
                PlanStatusText.Text = LocalizationSource.Instance["Upsell.Plan.Unavailable"];
                PlanStatusText.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upsell] Product pricing refresh failed: {ex.Message}");
            MonthlyPriceText.Text = "—";
            LifetimePriceText.Text = "—";
            PlanStatusText.Text = LocalizationSource.Instance["Upsell.Plan.Unavailable"];
            PlanStatusText.IsVisible = true;
            RetryPricingButton.IsVisible = true;
        }
    }

    private async void RetryPricing_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshProductPricingAsync();
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
    /// ürün bulunamazsa genel Premium akışına düşer. Sonuç değerlendirilir:
    ///  - Başarılı / kullanıcı iptali → sheet kapanır (Play penceresi açıldı).
    ///  - Zaten sahip / teknik hata → sheet AÇIK kalır ve yerelleştirilmiş
    ///    mesaj gösterilir; teknik ayrıntı yalnız loglanır, retry mümkündür.
    /// </summary>
    private async Task PurchaseAsync(StoreProductKind kind)
    {
        if (_isPurchasing)
        {
            return;
        }

        HideStatusMessages();
        SetPurchasing(true);
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
                var result = await store.LaunchPurchaseAsync(product);

                if (result.Success)
                {
                    // Satın alma tamamlandı; hak EntitlementChanged ile yenilenir.
                    TryClose();
                }
                else if (result.CancelledByUser)
                {
                    // Kullanıcı Play penceresinde bilinçli olarak iptal etti.
                    TryClose();
                }
                else if (result.AlreadyOwned)
                {
                    ShowInfo(LocalizationSource.Instance["Upsell.Plan.AlreadyOwned"]);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Upsell] Purchase failed: {result.ErrorMessage}");
                    ShowError(LocalizationSource.Instance["Upsell.Error.PurchaseFailed"]);
                }
            }
            else
            {
                var licenseService = app.EnsureServices()?.GetRequiredService<ILicenseService>();
                if (licenseService is not null)
                {
                    var started = await licenseService.StartPurchaseFlowAsync(SubscriptionTier.Premium);
                    if (started)
                    {
                        TryClose();
                    }
                    else
                    {
                        ShowError(LocalizationSource.Instance["Upsell.Error.PurchaseFailed"]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upsell] Purchase failed: {ex}");
            ShowError(LocalizationSource.Instance["Upsell.Error.PurchaseFailed"]);
        }
        finally
        {
            SetPurchasing(false);
        }
    }

    private void ShowError(string message)
    {
        PurchaseStatusText.Text = message;
        PurchaseStatusText.IsVisible = true;
    }

    private void ShowInfo(string message)
    {
        PurchaseInfoText.Text = message;
        PurchaseInfoText.IsVisible = true;
    }

    private async void Buy_Click(object? sender, RoutedEventArgs e)
    {
        // Ana CTA: mağaza destekliyorsa aylık aboneliği başlatır; değilse
        // genel Premium akışına (URI) düşer.
        await PurchaseAsync(StoreProductKind.Subscription);
    }
}
