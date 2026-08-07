using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.BillingClient.Api;
using Android.Content;
using Android.Runtime;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Google Play Billing üzerinden Premium satışı ve hak doğrulama.
/// Ürünler: aylık abonelik (otomatik yenilenen) + tek seferlik kalıcı paket.
///
/// BillingClient yalnızca ana iş parçacığında oluşturulabilir ve kullanılabilir;
/// bu yüzden tüm çağrılar mevcut Activity üzerinden RunOnUiThread ile yapılır.
/// Haklar her açılışta ve satın alma güncellenince doğrulama BACKEND'inde
/// (Noctra.Billing.Api) yeniden doğrulanır — gerçek bitiş Play Developer
/// API'den (subscriptionsv2.get → lineItems.expiryTime) gelir; burada süre
/// hesaplanmaz. Doğrulama hizmetine ulaşılamazsa LicenseService son bilinen
/// doğrulanmış önbelleği kullanır.
/// </summary>
public sealed class AndroidStorePurchaseService : IStorePurchaseService, IDisposable
{
    private static readonly TimeSpan BillingTimeout = TimeSpan.FromSeconds(10);

    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly IBillingVerificationClient? _billingVerifier;
    private readonly ISettingsService? _settingsService;
    private readonly SemaphoreSlim _billingGate = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _billingPeriodCache = new();

    private BillingClient? _billingClient;
    private bool _disposed;

    public AndroidStorePurchaseService(
        Context context,
        AndroidActivityProvider activityProvider,
        IBillingVerificationClient? billingVerifier = null,
        ISettingsService? settingsService = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _billingVerifier = billingVerifier;
        _settingsService = settingsService;
        _context = context.ApplicationContext ?? context;
    }

    public bool IsSupported => true;

    public event EventHandler? EntitlementChanged;

    // ==========================================
    // IStorePurchaseService
    // ==========================================

    public async Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var client = await EnsureBillingClientAsync(cancellationToken).ConfigureAwait(false);

        var entries = new List<QueryProductDetailsParams.Product>
        {
            BuildProductListEntry(StoreProducts.MonthlySubscription, BillingClient.ProductType.Subs),
            BuildProductListEntry(StoreProducts.LifetimePurchase, BillingClient.ProductType.Inapp)
        };

        var @params = QueryProductDetailsParams.NewBuilder()
            .SetProductList(entries)
            .Build();

        var result = await RunOnUiThreadTaskAsync(
            () => client.QueryProductDetailsAsync(@params),
            cancellationToken).ConfigureAwait(false);

        if (result.Result.ResponseCode != BillingResponseCode.Ok)
        {
            return Array.Empty<StoreProduct>();
        }

        var products = new List<StoreProduct>(result.ProductDetailsList.Count);
        foreach (var details in result.ProductDetailsList)
        {
            var product = MapProductDetails(details);
            if (product is not null)
            {
                products.Add(product);
            }
        }

        return products;
    }

    public async Task<StorePurchaseResult> LaunchPurchaseAsync(StoreProduct product, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (product is null)
        {
            return StorePurchaseResult.Fail("Product is null.");
        }

        var client = await EnsureBillingClientAsync(cancellationToken).ConfigureAwait(false);
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return StorePurchaseResult.Fail("No activity available to start a purchase.");
        }

        var details = await FindProductDetailsAsync(client, product.ProductId, product.Kind, cancellationToken)
            .ConfigureAwait(false);
        if (details is null)
        {
            return StorePurchaseResult.Fail($"Product not found: {product.ProductId}");
        }

        var productParamsBuilder = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(details);
        if (product.Kind == StoreProductKind.Subscription)
        {
            var offer = GetSubscriptionOfferDetails(details).FirstOrDefault();
            if (offer is null || string.IsNullOrWhiteSpace(offer.OfferToken))
            {
                return StorePurchaseResult.Fail("Subscription offer is not available.");
            }

            productParamsBuilder.SetOfferToken(offer.OfferToken);
        }

        var flowParams = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList(new List<BillingFlowParams.ProductDetailsParams> { productParamsBuilder.Build() })
            .Build();

        var billingResult = await RunOnUiThreadAsync(
            () => client.LaunchBillingFlow(activity, flowParams),
            cancellationToken).ConfigureAwait(false);

        if (billingResult.ResponseCode == BillingResponseCode.Ok)
        {
            return StorePurchaseResult.Ok();
        }

        if (billingResult.ResponseCode == BillingResponseCode.UserCancelled)
        {
            return StorePurchaseResult.Cancelled();
        }

        if (billingResult.ResponseCode == BillingResponseCode.ItemAlreadyOwned)
        {
            return StorePurchaseResult.Owned();
        }

        System.Diagnostics.Debug.WriteLine($"[StorePurchase] Launch failed: {billingResult.DebugMessage}");
        return StorePurchaseResult.Fail(billingResult.DebugMessage);
    }

    public async Task<StoreEntitlement> GetEntitlementAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var client = await EnsureBillingClientAsync(cancellationToken).ConfigureAwait(false);

        var inappPurchases = await QueryPurchasesAsync(client, BillingClient.ProductType.Inapp, cancellationToken)
            .ConfigureAwait(false);
        var subscriptionPurchases = await QueryPurchasesAsync(client, BillingClient.ProductType.Subs, cancellationToken)
            .ConfigureAwait(false);

        // Play sorgusu başarısız olduysa veya doğrulama istemcisi yoksa hak
        // doğrulanamadı (IsVerified=false) — LicenseService son bilinen
        // doğrulanmış önbelleği kullanır (fail-safe; hak asla erken düşmez).
        if (inappPurchases is null || subscriptionPurchases is null || _billingVerifier is null)
        {
            return new StoreEntitlement { IsVerified = false };
        }

        foreach (var purchase in inappPurchases.Concat(subscriptionPurchases))
        {
            AcknowledgeIfNeeded(purchase);
        }

        var installationId = await EnsureInstallationIdAsync();
        var packageName = _context.PackageName ?? string.Empty;

        var hasLifetime = false;
        DateTime? subscriptionEnd = null;
        // Sorgu başarılı olduğuna göre sonuç otoritatiftir: satın alım yoksa hak
        // yoktur (IsVerified=true, boş hak). Yalnızca mevcut bir token backend'de
        // doğrulanamazsa (ağ/sunucu hatası) IsVerified=false döner — LicenseService
        // o zaman son bilinen doğrulanmış önbelleği korur (hak asla erken düşmez).
        // Not (takas): iki döngüde tek bir token bile doğrulanamazsa sonucun TAMAMI
        // doğrulanmamış sayılır ve önbelleğe dönülür; yeni cihazda boş önbellekle
        // restore ederken başarıyla doğrulanan bir hak bu turda uygulanmayabilir —
        // muhafazakâr yön, "hak erken düşmez" ilkesiyle uyumludur.
        var anyVerificationFailed = false;

        // Her satın alma token'ı backend'de doğrulanır. Süre burada ASLA
        // hesaplanmaz — gerçek bitiş Play'den (subscriptionsv2.get →
        // lineItems.expiryTime) backend üzerinden gelir.
        foreach (var purchase in inappPurchases.Where(p =>
                     p.PurchaseState == PurchaseState.Purchased &&
                     p.Products.Contains(StoreProducts.LifetimePurchase, StringComparer.OrdinalIgnoreCase)))
        {
            var verified = await VerifyTokenAsync(purchase, packageName, installationId, cancellationToken);
            if (verified is null)
            {
                anyVerificationFailed = true;
                continue;
            }

            if (verified.IsActive && string.Equals(verified.EntitlementType, "Lifetime", StringComparison.OrdinalIgnoreCase))
            {
                hasLifetime = true;
            }
        }

        foreach (var purchase in subscriptionPurchases.Where(p =>
                     p.PurchaseState == PurchaseState.Purchased &&
                     p.Products.Contains(StoreProducts.MonthlySubscription, StringComparer.OrdinalIgnoreCase)))
        {
            var verified = await VerifyTokenAsync(purchase, packageName, installationId, cancellationToken);
            if (verified is null)
            {
                anyVerificationFailed = true;
                continue;
            }

            if (verified.IsActive && verified.ExpiresAtUtc.HasValue &&
                (subscriptionEnd is null || verified.ExpiresAtUtc.Value > subscriptionEnd.Value))
            {
                subscriptionEnd = verified.ExpiresAtUtc;
            }
        }

        return new StoreEntitlement
        {
            HasLifetimePremium = hasLifetime,
            SubscriptionExpiresAtUtc = subscriptionEnd,
            IsVerified = !anyVerificationFailed
        };
    }

    private async Task<BillingVerifiedEntitlement?> VerifyTokenAsync(
        Purchase purchase,
        string packageName,
        string installationId,
        CancellationToken cancellationToken)
    {
        var productId = purchase.Products.FirstOrDefault() ?? string.Empty;
        if (_billingVerifier is null ||
            string.IsNullOrWhiteSpace(productId) ||
            string.IsNullOrWhiteSpace(purchase.PurchaseToken))
        {
            return null;
        }

        return await _billingVerifier.VerifyAsync(new BillingVerifyRequest
        {
            InstallationId = installationId,
            PurchaseToken = purchase.PurchaseToken,
            ProductId = productId,
            PackageName = packageName
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Anonim kurulum kimliği: ilk kullanımda üretilip global ayarlara yazılır.
    /// Purchase token'ları backend doğrulamasına bu kimlikle bağlanır.
    /// </summary>
    private async Task<string> EnsureInstallationIdAsync()
    {
        if (_settingsService is null)
        {
            return Guid.NewGuid().ToString("N");
        }

        var existing = _settingsService.Settings.InstallationId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var id = Guid.NewGuid().ToString("N");
        _settingsService.Settings.InstallationId = id;
        try
        {
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorePurchase] Failed to persist installation id: {ex.Message}");
        }

        return id;
    }

    public async Task RestorePurchasesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await GetEntitlementAsync(cancellationToken).ConfigureAwait(false);
        EntitlementChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==========================================
    // BillingClient lifecycle
    // ==========================================

    private async Task<BillingClient> EnsureBillingClientAsync(CancellationToken cancellationToken)
    {
        if (_billingClient is { IsReady: true })
        {
            return _billingClient;
        }

        await _billingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_billingClient is { IsReady: true })
            {
                return _billingClient;
            }

            var client = await RunOnUiThreadAsync(BuildBillingClient, cancellationToken).ConfigureAwait(false);
            var connectResult = await RunOnUiThreadTaskAsync(
                () => client.StartConnectionAsync(),
                cancellationToken).ConfigureAwait(false);

            if (connectResult.ResponseCode != BillingResponseCode.Ok)
            {
                System.Diagnostics.Debug.WriteLine($"[StorePurchase] Connection failed: {connectResult.DebugMessage}");
                // Bağlı olmayan client'ı hemen kapat; aksi halde sonraki denemede
                // eski client+listener kalıcı olarak sızardı.
                try
                {
                    client.EndConnection();
                }
                catch (Exception endEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[StorePurchase] Cleanup of failed connection: {endEx.Message}");
                }

                throw new InvalidOperationException(
                    $"Billing connection failed: {connectResult.ResponseCode} {connectResult.DebugMessage}");
            }

            _billingClient = client;
            return client;
        }
        finally
        {
            _billingGate.Release();
        }
    }

    private BillingClient BuildBillingClient()
    {
        var listener = new PurchasesUpdatedListener
        {
            Handler = OnPurchasesUpdated
        };

        return BillingClient.NewBuilder(_context)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
            .SetListener(listener)
            .Build();
    }

    private void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
    {
        if (billingResult.ResponseCode != BillingResponseCode.Ok)
        {
            return;
        }

        if (purchases is not null)
        {
            foreach (var purchase in purchases)
            {
                AcknowledgeIfNeeded(purchase);
            }
        }

        // Lisans servisi hak değişimini yeniden sorgulayıp aboneliği günceller.
        EntitlementChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==========================================
    // Product helpers
    // ==========================================

    private static QueryProductDetailsParams.Product BuildProductListEntry(string productId, string productType)
        => QueryProductDetailsParams.Product.NewBuilder()
            .SetProductId(productId)
            .SetProductType(productType)
            .Build();

    private async Task<ProductDetails?> FindProductDetailsAsync(
        BillingClient client,
        string productId,
        StoreProductKind kind,
        CancellationToken cancellationToken)
    {
        var productType = kind == StoreProductKind.Subscription
            ? BillingClient.ProductType.Subs
            : BillingClient.ProductType.Inapp;

        var @params = QueryProductDetailsParams.NewBuilder()
            .SetProductList(new List<QueryProductDetailsParams.Product>
            {
                BuildProductListEntry(productId, productType)
            })
            .Build();

        var result = await RunOnUiThreadTaskAsync(
            () => client.QueryProductDetailsAsync(@params),
            cancellationToken).ConfigureAwait(false);

        if (result.Result.ResponseCode != BillingResponseCode.Ok)
        {
            return null;
        }

        return result.ProductDetailsList.FirstOrDefault(p =>
            string.Equals(p.ProductId, productId, StringComparison.OrdinalIgnoreCase));
    }

    private StoreProduct? MapProductDetails(ProductDetails details)
    {
        if (string.Equals(details.ProductId, StoreProducts.LifetimePurchase, StringComparison.OrdinalIgnoreCase))
        {
            return new StoreProduct
            {
                ProductId = details.ProductId,
                Kind = StoreProductKind.Lifetime,
                Title = details.Title,
                Description = details.Description,
                Price = details.OneTimePurchaseOfferDetailsList?.FirstOrDefault()?.FormattedPrice ?? string.Empty
            };
        }

        if (string.Equals(details.ProductId, StoreProducts.MonthlySubscription, StringComparison.OrdinalIgnoreCase))
        {
            var offer = GetSubscriptionOfferDetails(details).FirstOrDefault();
            if (offer is null)
            {
                return null;
            }

            _billingPeriodCache[details.ProductId] = offer.BillingPeriod ?? "P1M";
            return new StoreProduct
            {
                ProductId = details.ProductId,
                Kind = StoreProductKind.Subscription,
                Title = details.Title,
                Description = details.Description,
                Price = offer.FormattedPrice ?? string.Empty,
                BillingPeriod = offer.BillingPeriod,
                OfferToken = offer.OfferToken
            };
        }

        return null;
    }

    private async Task<IList<Purchase>?> QueryPurchasesAsync(
        BillingClient client,
        string productType,
        CancellationToken cancellationToken)
    {
        var @params = QueryPurchasesParams.NewBuilder()
            .SetProductType(productType)
            .Build();

        var result = await RunOnUiThreadTaskAsync(
            () => client.QueryPurchasesAsync(@params),
            cancellationToken).ConfigureAwait(false);

        // Başarısız sorgu boş liste gibi davranmamalı — yoksa hak yanlışlıkla
        // "satın alma yok" sayılıp düşürülebilir. Null, çağıranın önbelleğe
        // dönmesini sağlar.
        return result.Result.ResponseCode == BillingResponseCode.Ok
            ? result.Purchases
            : null;
    }

    /// <summary>
    /// Binding'de InternalPurchasesUpdatedListener erişilemez olduğu için
    /// aynı arayüzü uygulayan küçük bir sarıcı.
    /// </summary>
    private sealed class PurchasesUpdatedListener : Java.Lang.Object, IPurchasesUpdatedListener
    {
        public Action<BillingResult, IList<Purchase>?>? Handler { get; set; }

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
            => Handler?.Invoke(billingResult, purchases);
    }

    private sealed record SubscriptionOfferInfo(
        string? OfferToken,
        string? FormattedPrice,
        string? BillingPeriod);

    /// <summary>
    /// Play Billing v9 binding'i ProductDetails üzerinde
    /// getSubscriptionOfferDetails erişimini atlamış; bu nedenle Java
    /// metoduna JNI ile erişilip liste çekilir. Yalnızca abonelik ürünleri
    /// için çağrılır; diğer ürünlerde boş liste döner.
    /// </summary>
    private static IReadOnlyList<SubscriptionOfferInfo> GetSubscriptionOfferDetails(ProductDetails details)
    {
        var offers = new List<SubscriptionOfferInfo>();
        try
        {
            var listHandle = CallJavaObject(details, "getSubscriptionOfferDetails", "()Ljava/util/List;");
            if (listHandle == IntPtr.Zero)
            {
                return offers;
            }

            try
            {
                var listClass = JNIEnv.GetObjectClass(listHandle);
                try
                {
                    var sizeMethod = JNIEnv.GetMethodID(listClass, "size", "()I");
                    var getMethod = JNIEnv.GetMethodID(listClass, "get", "(I)Ljava/lang/Object;");
                    var count = JNIEnv.CallIntMethod(listHandle, sizeMethod);
                for (var i = 0; i < count; i++)
                {
                    var itemHandle = JNIEnv.CallObjectMethod(listHandle, getMethod, new JValue(i));
                    if (itemHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    using var item = Java.Lang.Object.GetObject<Java.Lang.Object>(
                        itemHandle, JniHandleOwnership.TransferLocalRef);
                    if (item is not null)
                    {
                        offers.Add(ReadOffer(item));
                    }
                }
                }
                finally
                {
                    JNIEnv.DeleteLocalRef(listClass);
                }
            }
            finally
            {
                JNIEnv.DeleteLocalRef(listHandle);
            }
        }
        catch (Exception ex)
        {
            // Play her zaman subscription offer döndürmediği durumlarda
            // abonelik akışı offer bulunamadı hatasıyla tamamlanır.
            System.Diagnostics.Debug.WriteLine(
                $"[StorePurchase] SubscriptionOfferDetails read failed: {ex.Message}");
        }

        return offers;
    }

    /// <summary>
    /// SubscriptionOfferDetails (binding'de tür olarak açığa çıkmadığı için)
    /// JNI ile okunur: offerToken + ilk pricing phase'in fiyat/dönemi.
    /// </summary>
    private static SubscriptionOfferInfo ReadOffer(Java.Lang.Object offer)
    {
        var token = CallJavaString(offer, "getOfferToken", "()Ljava/lang/String;");
        if (string.IsNullOrWhiteSpace(token))
        {
            return new SubscriptionOfferInfo(token, null, null);
        }

        // getPricingPhases() -> PricingPhases -> getPricingPhaseList() -> List<PricingPhase>
        var phasesHandle = CallJavaObject(offer, "getPricingPhases", "()Lcom/android/billingclient/api/PricingPhases;");
        if (phasesHandle == IntPtr.Zero)
        {
            return new SubscriptionOfferInfo(token, null, null);
        }

        string? formattedPrice = null;
        string? billingPeriod = null;
        try
        {
            // Dikkat: phasesHandle sahipliği ALINMAZ (DoNotTransfer) — yalnızca
            // JNI üzerinden okunur ve aşağıdaki finally ile yerel referans silinir.
            var phases = Java.Lang.Object.GetObject<Java.Lang.Object>(
                phasesHandle, JniHandleOwnership.DoNotTransfer);
            if (phases is null)
            {
                return new SubscriptionOfferInfo(token, null, null);
            }

            var phaseListHandle = CallJavaObject(phases, "getPricingPhaseList", "()Ljava/util/List;");
            if (phaseListHandle != IntPtr.Zero)
            {
                try
                {
                    var listClass = JNIEnv.GetObjectClass(phaseListHandle);
                    try
                    {
                        var sizeMethod = JNIEnv.GetMethodID(listClass, "size", "()I");
                        var getMethod = JNIEnv.GetMethodID(listClass, "get", "(I)Ljava/lang/Object;");
                        if (JNIEnv.CallIntMethod(phaseListHandle, sizeMethod) > 0)
                        {
                            var firstHandle = JNIEnv.CallObjectMethod(phaseListHandle, getMethod, new JValue(0));
                            if (firstHandle != IntPtr.Zero)
                            {
                                using var firstPhase = Java.Lang.Object.GetObject<Java.Lang.Object>(
                                    firstHandle, JniHandleOwnership.TransferLocalRef);
                                if (firstPhase is not null)
                                {
                                    formattedPrice = CallJavaString(firstPhase, "getFormattedPrice", "()Ljava/lang/String;");
                                    billingPeriod = CallJavaString(firstPhase, "getBillingPeriod", "()Ljava/lang/String;");
                                }
                            }
                        }
                    }
                    finally
                    {
                        JNIEnv.DeleteLocalRef(listClass);
                    }
                }
                finally
                {
                    JNIEnv.DeleteLocalRef(phaseListHandle);
                }
            }
        }
        finally
        {
            JNIEnv.DeleteLocalRef(phasesHandle);
        }

        return new SubscriptionOfferInfo(token, formattedPrice, billingPeriod);
    }

    /// <summary>
    /// Hedef üzerinde Java metodunu çağırıp yerel referans döndürür.
    /// Sınıf referansı hemen temizlenir; dönüş değeri çağıran tarafından
    /// DeleteLocalRef ile serbest bırakılmalıdır.
    /// </summary>
    private static IntPtr CallJavaObject(Java.Lang.Object target, string methodName, string signature)
    {
        var classRef = JNIEnv.GetObjectClass(target.Handle);
        try
        {
            var methodId = JNIEnv.GetMethodID(classRef, methodName, signature);
            return JNIEnv.CallObjectMethod(target.Handle, methodId);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(classRef);
        }
    }

    private static string? CallJavaString(Java.Lang.Object target, string methodName, string signature)
    {
        var handle = CallJavaObject(target, methodName, signature);
        return handle == IntPtr.Zero ? null : JNIEnv.GetString(handle, JniHandleOwnership.TransferLocalRef);
    }

    // ==========================================
    // Entitlement computation
    // ==========================================

    private void AcknowledgeIfNeeded(Purchase purchase)
    {
        if (purchase.PurchaseState != PurchaseState.Purchased || purchase.IsAcknowledged)
        {
            return;
        }

        var token = purchase.PurchaseToken;
        _ = Task.Run(async () =>
        {
            try
            {
                var client = await EnsureBillingClientAsync(CancellationToken.None).ConfigureAwait(false);
                var @params = AcknowledgePurchaseParams.NewBuilder()
                    .SetPurchaseToken(token)
                    .Build();
                await RunOnUiThreadTaskAsync(
                    () => client.AcknowledgePurchaseAsync(@params),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Onaylanmamış satın alım 3 gün içinde otomatik iade edilir;
                // bir sonraki sorguda tekrar deneneceği için burada sadece log.
                System.Diagnostics.Debug.WriteLine($"[StorePurchase] Acknowledge failed: {ex.Message}");
            }
        });
    }

    // ==========================================
    // Threading helpers
    // ==========================================

    private Task<T> RunOnUiThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = _activityProvider.CurrentActivity;
        if (activity is not null)
        {
            activity.RunOnUiThread(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    tcs.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }
        else
        {
            tcs.TrySetException(new InvalidOperationException(
                "No Android Activity is available for Play Billing operations."));
        }

        return tcs.Task;
    }

    private Task<T> RunOnUiThreadTaskAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = _activityProvider.CurrentActivity;
        if (activity is not null)
        {
            activity.RunOnUiThread(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                _ = action().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        tcs.TrySetException(t.Exception?.InnerException ?? t.Exception!);
                    }
                    else if (t.IsCanceled)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        tcs.TrySetResult(t.Result);
                    }
                }, TaskScheduler.Default);
            });
        }
        else
        {
            tcs.TrySetException(new InvalidOperationException(
                "No Android Activity is available for Play Billing operations."));
        }

        return tcs.Task;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AndroidStorePurchaseService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // BillingClient ana iş parçacığına bağlıdır; Dispose bir arka plan
            // iş parçacığında da çağrılabilir, bu yüzden kapatma UI'da yapılır.
            var activity = _activityProvider.CurrentActivity;
            var clientToClose = _billingClient;
            _billingClient = null;
            if (clientToClose is not null && activity is not null)
            {
                activity.RunOnUiThread(() =>
                {
                    try
                    {
                        clientToClose.EndConnection();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StorePurchase] EndConnection failed: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorePurchase] Dispose failed: {ex.Message}");
        }

        _billingGate.Dispose();
    }
}
