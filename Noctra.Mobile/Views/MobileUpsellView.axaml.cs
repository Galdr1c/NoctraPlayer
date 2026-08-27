using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    private bool _isRestoring;
    private ILicenseService? _licenseService;
    private bool _licenseSubscribed;
    private CancellationTokenSource? _purchaseCompletionCts;
    private bool _purchaseFlowActive;

    // Play Billing may deliver the purchase callback a little after the
    // billing Activity closes. Keep the fallback bounded so a missed callback
    // cannot leave an unbounded network loop running in the background.
    private static readonly TimeSpan[] PurchaseRefreshDelays =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(12)
    };

    public MobileUpsellView()
    {
        InitializeComponent();
    }

    public void Show()
    {
        // A Play purchase restore may still be running. Do not reopen the
        // sheet until it finishes.
        if (_isRestoring)
        {
            return;
        }

        // If a purchase flow was active (Play Activity was launched and the
        // user navigated back after cancelling), stop the background watcher
        // and allow the sheet to reopen.
        StopPurchaseCompletionWatch();
        _purchaseFlowActive = false;

        IsVisible = true;
        HideStatusMessages();
        RetryPricingButton.IsVisible = false;
        ManageSubscriptionButton.IsVisible = false;

        EnsureLicenseSubscription();

        // A purchase may have completed while this host was being resumed or
        // recreated. Do not render an upsell over an already-paid account.
        if (_licenseService?.IsPremium == true)
        {
            TryClose();
            return;
        }

        UpdatePlanCardVisibility();

        // Android cold-start can create the singleton before an Activity is
        // available, so its deferred initial refresh may be skipped. Refresh
        // when the sheet is actually requested as well; this also restores a
        // purchase made on another device without requiring an app restart.
        _ = RefreshLicenseStatusAsync();
        _ = RefreshProductPricingAsync();
    }

    public bool TryClose()
    {
        var wasVisible = IsVisible;
        StopPurchaseCompletionWatch();
        _purchaseFlowActive = false;
        IsVisible = false;
        UnsubscribeLicense();
        return wasVisible;
    }

    /// <summary>
    /// Hides the sheet only after Play Billing confirms that its purchase
    /// Activity was successfully launched. License subscription remains alive
    /// until the entitlement watcher has finished, so a verified purchase is
    /// still propagated to the shared LicenseService while the sheet is hidden.
    /// </summary>
    private void HideForPurchaseFlow()
    {
        StopPurchaseCompletionWatch();
        _purchaseFlowActive = true;
        IsVisible = false;
    }

    /// <summary>
    /// Satın alma akışının gerçek entitlement sonucunu takip etmek için lisansa
    /// abone olur. Play Activity'si açıldığında sheet gizlenir; callback veya
    /// sınırlı fallback yenilemesi Premium'u doğruladığında ortak lisans state'i
    /// reklam ve diğer Premium tüketicilerine yayılır. Tekrar abone olmayı önler.
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

    private void StartPurchaseCompletionWatch()
    {
        StopPurchaseCompletionWatch();

        var cts = new CancellationTokenSource();
        _purchaseCompletionCts = cts;
        _ = WatchForPurchaseCompletionAsync(cts);
    }

    /// <summary>
    /// Play Billing returns success when its purchase UI opens, not when the
    /// entitlement has been verified. The normal SubscriptionChanged event is
    /// still the primary path, but this short UI-safe watchdog closes a host if
    /// that event races with the billing Activity resume callback.
    /// </summary>
    private async Task WatchForPurchaseCompletionAsync(CancellationTokenSource cts)
    {
        try
        {
            foreach (var delay in PurchaseRefreshDelays)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                }

                if (cts.IsCancellationRequested)
                {
                    break;
                }

                // Do not only inspect the cached IsPremium value. A Play
                // callback can race with Activity resume and leave the cache
                // stale; this refresh queries Play and the backend again.
                await RefreshLicenseStatusAsync().ConfigureAwait(false);

                if (_licenseService?.IsPremium == true)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // The user closed the sheet or a newer purchase replaced this
            // watcher; no status message is needed.
        }
        finally
        {
            if (ReferenceEquals(_purchaseCompletionCts, cts))
            {
                _purchaseCompletionCts = null;
                _purchaseFlowActive = false;
            }

            cts.Dispose();
        }
    }

    private void StopPurchaseCompletionWatch()
    {
        var cts = _purchaseCompletionCts;
        _purchaseCompletionCts = null;
        cts?.Cancel();
    }

    private async Task RefreshLicenseStatusAsync()
    {
        var licenseService = _licenseService;
        if (licenseService is null)
        {
            return;
        }

        try
        {
            await licenseService.RefreshSubscriptionStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Upsell] Store entitlement refresh failed: {ex.Message}");
        }

        // RefreshSubscriptionStatusAsync may complete on a Billing/HTTP
        // thread. Keep all control-tree changes on Avalonia's UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_licenseService, licenseService))
            {
                return;
            }

            if (licenseService.IsPremium)
            {
                // Also cleans the hidden-flow subscription/watcher.
                TryClose();
            }
            else if (IsVisible)
            {
                UpdatePlanCardVisibility();
            }
        });
    }

    private void OnLicenseSubscriptionChanged()
    {
        if (!IsVisible || _licenseService is null || _isRestoring)
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
        UpdateBusyState();
    }

    private void SetRestoring(bool restoring)
    {
        _isRestoring = restoring;
        UpdateBusyState();
    }

    private void UpdateBusyState()
    {
        var busy = _isPurchasing || _isRestoring;
        MonthlyBuyButton.IsEnabled = !busy;
        LifetimeBuyButton.IsEnabled = !busy;
        RestorePurchasesButton.IsEnabled = !busy;
        BuySpinner.IsVisible = busy;
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
                RetryPricingButton.IsVisible = true;
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

    private async void RestorePurchases_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPurchasing || _isRestoring)
        {
            return;
        }

        EnsureLicenseSubscription();
        HideStatusMessages();
        SetRestoring(true);

        try
        {
            if (Application.Current is not App app)
            {
                ShowError(LocalizationSource.Instance["Upsell.Restore.Failed"]);
                return;
            }

            var store = app.EnsureServices()?.GetService<IStorePurchaseService>();
            if (store is not { IsSupported: true })
            {
                ShowInfo(LocalizationSource.Instance["Upsell.Restore.Unavailable"]);
                return;
            }

            // AndroidStorePurchaseService queries Play and verifies each
            // recognized purchase token through the secure backend.
            await store.RestorePurchasesAsync();

            // RestorePurchasesAsync raises EntitlementChanged asynchronously;
            // await the shared LicenseService refresh so the decision below is
            // based on the authoritative entitlement, not a stale cache.
            if (_licenseService is not null)
            {
                await _licenseService.RefreshSubscriptionStatusAsync();
            }

            if (!IsVisible)
            {
                return;
            }

            if (_licenseService?.IsPremium == true)
            {
                // Keep the success state visible in the sheet while the
                // existing platform notification/toast is dispatched.
                ShowInfo(LocalizationSource.Instance["Upsell.Restore.Success"]);
                var notificationService = app.EnsureServices()?.GetService<IDialogService>();
                if (notificationService is not null)
                {
                    try
                    {
                        await notificationService.ShowNotificationAsync(
                            LocalizationSource.Instance["Upsell.Restore.SuccessTitle"],
                            LocalizationSource.Instance["Upsell.Restore.Success"]);
                    }
                    catch (Exception notificationEx)
                    {
                        // The entitlement is already active; a notification
                        // failure must never turn a successful restore into an
                        // error state.
                        System.Diagnostics.Debug.WriteLine(
                            $"[Upsell] Restore success notification failed: {notificationEx.Message}");
                    }
                }

                TryClose();
                return;
            }

            if (_licenseService?.HasPendingStorePurchase == true)
            {
                ShowInfo(LocalizationSource.Instance["Settings.Store.PurchasePending"]);
                return;
            }

            ShowInfo(LocalizationSource.Instance["Upsell.Restore.NotFound"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upsell] Restore purchases failed: {ex}");
            if (IsVisible)
            {
                ShowError(LocalizationSource.Instance["Upsell.Restore.Failed"]);
            }
        }
        finally
        {
            SetRestoring(false);
        }
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
    /// Play penceresi başarıyla açıldığı anda sheet gizlenir. Play penceresi
    /// açılmazsa (ürün/aktivite/teknik hata) sheet açık kalır. Gizlendikten
    /// sonra entitlement callback'i ve bounded refresh watcher'ı ortak
    /// LicenseService'i günceller; Premium doğrulaması reklam ve UI katmanına
    /// aynı singleton state üzerinden yayılır.
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
                // LaunchBillingFlow başarıyla döndüyse Play satın alma
                // Activity'si açılmıştır; yalnızca bu noktada sheet'i gizle.
                HideForPurchaseFlow();
                StartPurchaseCompletionWatch();
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
                // A canceled test subscription remains owned until its paid
                // period expires. Refresh the authoritative entitlement before
                // telling the user that the plan is unavailable; if the
                // existing purchase is still active, the upsell is obsolete.
                if (_licenseService is not null)
                {
                    try
                    {
                        await _licenseService.RefreshSubscriptionStatusAsync();
                        if (_licenseService.IsPremium)
                        {
                            TryClose();
                            return;
                        }
                    }
                    catch (Exception refreshEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Upsell] Already-owned entitlement refresh failed: {refreshEx.Message}");
                    }
                }

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
