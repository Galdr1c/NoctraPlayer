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
using Noctra.Services.Interfaces;

namespace Noctra.Mobile.Views;

public partial class MobileUpsellView : UserControl
{
    private IReadOnlyList<StoreProduct> _products = Array.Empty<StoreProduct>();
    private bool _isPurchasing;
    private ILicenseService? _licenseService;
    private bool _licenseSubscribed;

    public MobileUpsellView()
    {
        InitializeComponent();
    }

    public void Show()
    {
        IsVisible = true;
        HideStatusMessages();
        RetryPricingButton.IsVisible = false;
        ManageSubscriptionButton.IsVisible = false;

        EnsureLicenseSubscription();
        UpdatePlanCardVisibility();

        _ = RefreshProductPricingAsync();
    }

    public bool TryClose()
    {
        if (!IsVisible)
            return false;

        IsVisible = false;
        UnsubscribeLicense();
        return true;
    }

    /// <summary>
    /// Satın alma akışının GERÇEK sonucu için lisansa abone olur. Play penceresi
    /// açıldıktan sonra sheet AÇIK kalır (kapanmaz); ödeme gerçekten tamamlanınca
    /// (SubscriptionChanged → Premium) kapanır, pending ise "ödeme bekleniyor"
    /// bildirimi gösterilir, iptalde kullanıcı zaten sheet'e döner. Tekrar
    /// abone olmayı önler.
    /// </summary>
    private void EnsureLicenseSubscription()
    {
        if (_licenseSubscribed)
        {
            return;
        }

        if (Application.Current is App app)
        {
            _licenseService = app.EnsureServices()?.GetService<ILicenseService>();
        }

        if (_licenseService is not null)
        {
            _licenseService.SubscriptionChanged += OnLicenseSubscriptionChanged;
            _licenseSubscribed = true;
        }
    }

    private void UnsubscribeLicense()
    {
        if (!_licenseSubscribed || _licenseService is null)
        {
            return;
        }

        _licenseService.SubscriptionChanged -= OnLicenseSubscriptionChanged;
        _licenseService = null;
        _licenseSubscribed = false;
    }

    private void OnLicenseSubscriptionChanged()
    {
        if (!IsVisible || _licenseService is null)
        {
            return;
        }

        if (_licenseService.IsPremium)
        {
            // Kalıcı paket + aktif aylık abonelik birlikte: aylık abonelik
            // Google Play'de otomatik iptal olmaz, yenilenmeye devam eder.
            // Sheet kapanmaz; kullanıcıya iptal hatırlatması + abonelik
            // yönetimi eylemi gösterilir.
            if (_licenseService.HasLifetimePremium && _licenseService.HasActiveStoreSubscription)
            {
                ShowInfo(LocalizationSource.Instance["Upsell.Plan.Lifetime.CancelMonthlyReminder"]);
                ManageSubscriptionButton.IsVisible = true;
                return;
            }

            // Ödeme tamamlandı — sheet kapanır.
            TryClose();
        }
        else if (_licenseService.HasPendingStorePurchase)
        {
            // Ödeme onay bekliyor (operatör faturalaması vb.).
            ShowInfo(LocalizationSource.Instance["Settings.Store.PurchasePending"]);
        }
    }

    /// <summary>
    /// Kalıcı Premium paketi sahibine aylık satın alma sunulmaz — plan kartları
    /// gizlenir. (Sheet normalde yalnızca Free kullanıcıya gösterilir; bu
    /// savunmacı kontrol, doğrulama gecikmesi/önbellek tutarsızlığında yanlış
    /// kart gösterimini önler.)
    /// </summary>
    private void UpdatePlanCardVisibility()
    {
        PlanGrid.IsVisible = _licenseService?.HasLifetimePremium != true;
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
    /// İşlem sırasında plan kartlarını devre dışı bırakır ve spinner'ı
    /// gösterir — çift tıklama / eşzamanlı akış olmaz.
    /// </summary>
    private void SetPurchasing(bool purchasing)
    {
        _isPurchasing = purchasing;
        MonthlyBuyButton.IsEnabled = !purchasing;
        LifetimeBuyButton.IsEnabled = !purchasing;
        BuySpinner.IsVisible = purchasing;
    }

    /// <summary>
    /// Mağaza ürün fiyatlarını yükleyip plan kartlarına yazar. Mağaza
    /// desteklenmiyorsa (masaüstü) kartlar fiyatsız kalır. Yükleme başarısız
    /// olursa fiyatlar "—" ile gösterilir ve yeniden deneme butonu görünür.
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
        // Aktif aylık abonelik varken kalıcı paket alınırsa abonelik Google
        // Play'de otomatik iptal olmaz — kullanıcıya açık uyarı gösterilir.
        if (await ConfirmLifetimeWhileMonthlyActiveAsync())
        {
            await PurchaseAsync(StoreProductKind.Lifetime);
        }
    }

    /// <summary>
    /// Aktif aylık abonelik varken kalıcı paket satın alma onayı ister.
    /// Uygulama aboneliği asla otomatik "iptal edildi" varsaymaz; yalnızca
    /// kullanıcının Google Play'de iptal etmesi için açık uyarı gösterilir.
    /// Aylık abonelik yoksa (veya onay servisi yoksa) doğrudan onaylanır.
    /// </summary>
    private async Task<bool> ConfirmLifetimeWhileMonthlyActiveAsync()
    {
        if (_licenseService?.HasActiveStoreSubscription != true)
        {
            return true;
        }

        if (Application.Current is not App app)
        {
            return true;
        }

        var dialog = app.EnsureServices()?.GetService<IDialogService>();
        if (dialog is null)
        {
            return true;
        }

        return await dialog.ShowConfirmationAsync(
            LocalizationSource.Instance["Upsell.Plan.Lifetime.ConfirmTitle"],
            LocalizationSource.Instance["Upsell.Plan.Lifetime.ConfirmMessage"]);
    }

    /// <summary>
    /// Seçilen türdeki mağaza ürünü için satın alma akışını başlatır.
    /// Sheet YALNIZCA gerçek satın alma tamamlandığında kapanır
    /// (OnPurchasesUpdated → EntitlementChanged → SubscriptionChanged →
    /// Premium). Play penceresinin açılması (Success) kapatmak için yeterli
    /// DEĞİLDİR: iptalde kullanıcı sheet'e döner, pending'de bildirim görür,
    /// teknik hatada hata gösterilir — hiçbirinde sheet erken kapanmaz.
    ///
    /// KRİTİK: Seçilen plan Play'de yoksa (örn. lifetime henüz yayınlanmadı)
    /// BAŞKA plana düşülmez — genel StartPurchaseFlowAsync aylık aboneliği
    /// öncelediği için Lifetime'a basan kullanıcıya yanlışlıkla aylık ödeme
    /// ekranı açılırdı. Eksik plan → "plan kullanılamıyor" gösterilir.
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

            if (store is not { IsSupported: true })
            {
                // Mağaza desteklenmiyor (ör. masaüstü barındırma): genel Premium
                // akışı (URI) kullanılır — burada plan ayrımı yoktur.
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

                return;
            }

            // Seçilen türde ürün Play'de yoksa (örn. lifetime ürünü henüz
            // yayınlanmadı veya sorgu dönmedi) BAŞKA plana fallback YAPILMAZ;
            // aylık ödeme ekranı açılmaz, planın kullanılamadığı gösterilir.
            // Mesaj "ürünler yüklenemedi" DEĞİLDİR — ürünler yüklenmiş olabilir,
            // yalnızca bu plan yoktur.
            var product = _products.FirstOrDefault(p => p.Kind == kind);
            if (product is null)
            {
                ShowError(LocalizationSource.Instance["Upsell.Plan.UnavailableKind"]);
                return;
            }

            var result = await store.LaunchPurchaseAsync(product);

            if (result.Success)
            {
                // Play penceresi açıldı — sheet KAPANMAZ. Gerçek sonuç
                // OnPurchasesUpdated → EntitlementChanged → SubscriptionChanged
                // ile gelir: ödeme tamamlanınca sheet kapanır (OnLicenseSubscriptionChanged),
                // iptal edilirse kullanıcı sheet'e döner, pending ise bildirim görür.
                return;
            }
            if (result.CancelledByUser)
            {
                // Play penceresi başlatılmadan iptal — sheet açık kalır,
                // kullanıcı başka plan seçebilir veya yeniden deneyebilir.
                return;
            }
            if (result.AlreadyOwned)
            {
                ShowInfo(LocalizationSource.Instance["Upsell.Plan.AlreadyOwned"]);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Upsell] Purchase failed: {result.ErrorMessage}");
            ShowError(LocalizationSource.Instance["Upsell.Error.PurchaseFailed"]);
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

    private async void ManageSubscription_Click(object? sender, RoutedEventArgs e)
    {
        // Google Play abonelik yönetimi — aktif aylık plan buradan iptal edilir.
        const string subscriptionsUrl = "https://play.google.com/store/account/subscriptions";

        try
        {
            if (Application.Current is App app)
            {
                var platformActions = app.EnsureServices()?.GetService<IPlatformActionService>();
                if (platformActions is not null)
                {
                    await platformActions.OpenUrlAsync(subscriptionsUrl);
                    return;
                }
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = subscriptionsUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upsell] Open subscriptions failed: {ex}");
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

}
