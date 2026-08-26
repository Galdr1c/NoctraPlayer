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
    private readonly SemaphoreSlim _billingGate = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _billingPeriodCache = new();

    private BillingClient? _billingClient;
    private bool _disposed;

    public AndroidStorePurchaseService(
        Context context,
        AndroidActivityProvider activityProvider,
        IBillingVerificationClient? billingVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _billingVerifier = billingVerifier;
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

        // Ürün türleri ve katalog erişimi birbirinden bağımsızdır. Örneğin
        // lifetime ürünü henüz Play Console'da yoksa aylık aboneliğin fiyatı
        // kaybolmamalıdır; iki sorguyu ayrı yürütüp yalnızca başarısız ürünü
        // yoksayıyoruz. İki istek eşzamanlı başlatılır, böylece toplam ağ
        // gecikmesi iki ardışık istek kadar büyümez.
        var monthlyTask = QueryAvailableProductDetailsAsync(
            client,
            StoreProducts.MonthlySubscription,
            BillingClient.ProductType.Subs,
            cancellationToken);
        var lifetimeTask = QueryAvailableProductDetailsAsync(
            client,
            StoreProducts.LifetimePurchase,
            BillingClient.ProductType.Inapp,
            cancellationToken);
        var detailBatches = await Task.WhenAll(monthlyTask, lifetimeTask).ConfigureAwait(false);

        var products = new List<StoreProduct>();
        foreach (var details in detailBatches.SelectMany(batch => batch))
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
            // Trial/intro içermeyen base plan offer'ı öncelikli seçilir;
            // istenmeyen offer token'ı (trial, introductory) satın alma
            // akışına gönderilmez.
            var offer = SelectSubscriptionOffer(GetSubscriptionOfferDetails(details));
            if (string.IsNullOrWhiteSpace(offer.OfferToken))
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

        // Onaylama (acknowledge) sorgu sonucundan ve doğrulama istemcisinden
        // BAĞIMSIZDIR: liste geldiyse abonelik dahil her PURCHASED kayıt
        // (kalıcı paket dahil) tek tek acknowledge edilir. Google Play,
        // onaylanmayan non-consumable satın alımları 3 gün içinde otomatik
        // iade eder; callback kaçırıldığında (yarım kalan satın alma, uygulama
        // kapanması, başka cihazda yapılan satın alma, restore) bu döngü
        // acknowledge'ı telafi eder. Sorgu başarısızsa liste null gelir ve
        // döngü zaten çalışmaz.
        if (inappPurchases is not null)
        {
            foreach (var purchase in inappPurchases)
            {
                AcknowledgeIfNeeded(purchase);
            }
        }

        if (subscriptionPurchases is not null)
        {
            foreach (var purchase in subscriptionPurchases)
            {
                AcknowledgeIfNeeded(purchase);
            }
        }

        // Play sorgusu başarısız olduysa veya doğrulama istemcisi yoksa hak
        // doğrulanamadı (IsVerified=false) — LicenseService son bilinen
        // doğrulanmış önbelleği kullanır (fail-safe; hak asla erken düşmez).
        if (inappPurchases is null || subscriptionPurchases is null || _billingVerifier is null)
        {
            return new StoreEntitlement { IsVerified = false };
        }

        var packageName = _context.PackageName ?? string.Empty;

        var hasLifetime = false;
        DateTime? subscriptionEnd = null;
        // Aktif aboneliğin gerçek trial offer'da olup olmadığı backend'den
        // gelir (client tahmin etmez). Kazanan (en geç bitişli) doğrulamanın
        // bayrağı taşınır; ücretli aylık kullanıcı asla trial görünmez.
        var isTrialPeriod = false;
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
            var verified = await VerifyTokenAsync(purchase, packageName, cancellationToken);
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
            var verified = await VerifyTokenAsync(purchase, packageName, cancellationToken);
            if (verified is null)
            {
                anyVerificationFailed = true;
                continue;
            }

            if (verified.IsActive && verified.ExpiresAtUtc.HasValue &&
                (subscriptionEnd is null || verified.ExpiresAtUtc.Value > subscriptionEnd.Value))
            {
                subscriptionEnd = verified.ExpiresAtUtc;
                isTrialPeriod = verified.IsTrialPeriod;
            }
        }

        // Play'de onay bekleyen (PENDING) satın alma — örn. operatör faturalaması
        // veya banka onayı. Pending satın alma yukarıdaki döngülerde PURCHASED
        // filtresine takıldığı için hak VERMEZ; bu bayrak yalnızca UI'ın
        // "ödeme bekleniyor" bildirimi göstermesi içindir. Sorgu başarılı
        // olduğu için sonuç otoritatiftir; ödeme çözülünce bir sonraki sorgu
        // (resume/EntitlementChanged) bayrağı temizler.
        var hasPendingPurchase =
            inappPurchases.Any(p => p.PurchaseState == PurchaseState.Pending) ||
            subscriptionPurchases.Any(p => p.PurchaseState == PurchaseState.Pending);

        return new StoreEntitlement
        {
            HasLifetimePremium = hasLifetime,
            SubscriptionExpiresAtUtc = subscriptionEnd,
            IsTrialPeriod = isTrialPeriod,
            HasPendingPurchase = hasPendingPurchase,
            IsVerified = !anyVerificationFailed
        };
    }

    private async Task<BillingVerifiedEntitlement?> VerifyTokenAsync(
        Purchase purchase,
        string packageName,
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
            PurchaseToken = purchase.PurchaseToken,
            ProductId = productId,
            PackageName = packageName
        }, cancellationToken).ConfigureAwait(false);
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

        var details = await QueryAvailableProductDetailsAsync(
            client,
            productId,
            productType,
            cancellationToken).ConfigureAwait(false);

        return details.FirstOrDefault(p =>
            string.Equals(p.ProductId, productId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tek bir ürünün ayrıntılarını sorgular. Ürün bulunamadığında veya Play
    /// geçici/kalıcı bir hata döndürdüğünde diğer ürün sorgusunu etkilememesi
    /// için boş liste döner. Log yalnızca katalog tanısı içerir; satın alma
    /// token'ı, hesap veya kişisel veri yazılmaz.
    /// </summary>
    private async Task<IReadOnlyList<ProductDetails>> QueryAvailableProductDetailsAsync(
        BillingClient client,
        string productId,
        string productType,
        CancellationToken cancellationToken)
    {
        var @params = QueryProductDetailsParams.NewBuilder()
            .SetProductList(new List<QueryProductDetailsParams.Product>
            {
                BuildProductListEntry(productId, productType)
            })
            .Build();

        try
        {
            var result = await RunOnUiThreadTaskAsync(
                () => client.QueryProductDetailsAsync(@params),
                cancellationToken).ConfigureAwait(false);
            var billingResult = result.Result;
            var details = billingResult.ResponseCode == BillingResponseCode.Ok
                ? result.ProductDetailsList?.ToArray() ?? Array.Empty<ProductDetails>()
                : Array.Empty<ProductDetails>();

            global::Android.Util.Log.Info(
                "NoctraBilling",
                $"product query id={productId} type={productType} " +
                $"response={billingResult.ResponseCode} message={billingResult.DebugMessage} " +
                $"fetched={details.Length}");

            return details;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "NoctraBilling",
                $"product query failed id={productId} type={productType} error={ex.GetType().Name}");
            return Array.Empty<ProductDetails>();
        }
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
            // Fiyat/gösterim: RECURRING fazdan okunan base plan offer'ı
            // (trial/introductory fiyat "aylık fiyat" gibi gösterilmez).
            var offer = SelectSubscriptionOffer(GetSubscriptionOfferDetails(details));
            if (string.IsNullOrWhiteSpace(offer.OfferToken))
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
        string? OfferId,
        IReadOnlyList<string> OfferTags,
        string? OfferToken,
        string? FormattedPrice,
        string? BillingPeriod,
        bool HasTrialOrIntro);

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
    /// JNI ile okunur: offerId + offer tag'leri + offerToken + pricing
    /// phase'leri. Görüntülenecek fiyat her zaman RECURRING (tekrarlayan)
    /// fazdan alınır — trial/introductory fiyat "aylık fiyat" gibi
    /// gösterilmez. Herhangi bir faz trial/intro ise HasTrialOrIntro true olur
    /// (bu offer'ın saf base plan değil, kampanya offer'ı olduğunu belirtir).
    /// </summary>
    private static SubscriptionOfferInfo ReadOffer(Java.Lang.Object offer)
    {
        var offerId = CallJavaString(offer, "getOfferId", "()Ljava/lang/String;");
        var token = CallJavaString(offer, "getOfferToken", "()Ljava/lang/String;");
        var offerTags = ReadStringList(offer, "getOfferTags");
        var (recurringPrice, recurringPeriod, hasTrialOrIntro) = ReadPricingPhases(offer);
        return new SubscriptionOfferInfo(offerId, offerTags, token, recurringPrice, recurringPeriod, hasTrialOrIntro);
    }

    /// <summary>
    /// Offer'ın pricing phase'lerini okur: RECURRING fazın fiyatı/dönemi +
    /// trial/intro varlığı. RECURRING faz bulunamazsa (ör. yalnızca trial)
    /// ilk faz yedek olarak kullanılır — satın alma token'ı yine aynı offer'dan
    /// gittiği için fiyat/token tutarsızlığı oluşmaz.
    /// </summary>
    private static (string? RecurringPrice, string? RecurringPeriod, bool HasTrialOrIntro) ReadPricingPhases(Java.Lang.Object offer)
    {
        // PricingPhase.RECURRING = 1 (Play Billing v7+). Diğer değerler
        // (NON_RECURRING=2, FINITE_RECURRING=3) trial/intro anlamına gelir.
        const int recurrenceModeRecurring = 1;

        var phasesHandle = CallJavaObject(offer, "getPricingPhases", "()Lcom/android/billingclient/api/PricingPhases;");
        if (phasesHandle == IntPtr.Zero)
        {
            return (null, null, false);
        }

        string? recurringPrice = null;
        string? recurringPeriod = null;
        string? fallbackPrice = null;
        string? fallbackPeriod = null;
        var hasTrialOrIntro = false;

        try
        {
            // Dikkat: phasesHandle sahipliği ALINMAZ (DoNotTransfer) — yalnızca
            // JNI üzerinden okunur ve aşağıdaki finally ile yerel referans silinir.
            var phases = Java.Lang.Object.GetObject<Java.Lang.Object>(
                phasesHandle, JniHandleOwnership.DoNotTransfer);
            if (phases is null)
            {
                return (null, null, false);
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
                        var count = JNIEnv.CallIntMethod(phaseListHandle, sizeMethod);
                        for (var i = 0; i < count; i++)
                        {
                            var phaseHandle = JNIEnv.CallObjectMethod(phaseListHandle, getMethod, new JValue(i));
                            if (phaseHandle == IntPtr.Zero)
                            {
                                continue;
                            }

                            using var phase = Java.Lang.Object.GetObject<Java.Lang.Object>(
                                phaseHandle, JniHandleOwnership.TransferLocalRef);
                            if (phase is null)
                            {
                                continue;
                            }

                            var price = CallJavaString(phase, "getFormattedPrice", "()Ljava/lang/String;");
                            var period = CallJavaString(phase, "getBillingPeriod", "()Ljava/lang/String;");
                            var recurrenceMode = CallJavaInt(phase, "getRecurrenceMode");

                            if (fallbackPrice is null && price is not null)
                            {
                                fallbackPrice = price;
                                fallbackPeriod = period;
                            }

                            if (recurrenceMode == recurrenceModeRecurring)
                            {
                                if (recurringPrice is null && price is not null)
                                {
                                    recurringPrice = price;
                                    recurringPeriod = period;
                                }
                            }
                            else
                            {
                                hasTrialOrIntro = true;
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

        return (recurringPrice ?? fallbackPrice, recurringPeriod ?? fallbackPeriod, hasTrialOrIntro);
    }

    /// <summary>
    /// Java List&lt;String&gt; döndüren metodları (örn. getOfferTags) JNI ile okur.
    /// </summary>
    private static IReadOnlyList<string> ReadStringList(Java.Lang.Object target, string methodName)
    {
        var result = new List<string>();
        var listHandle = CallJavaObject(target, methodName, "()Ljava/util/List;");
        if (listHandle == IntPtr.Zero)
        {
            return result;
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

                    var value = JNIEnv.GetString(itemHandle, JniHandleOwnership.TransferLocalRef);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
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

        return result;
    }

    /// <summary>
    /// Play Console'da aynı üründe birden fazla offer bulunabilir (trial,
    /// introductory price, birden fazla base plan, farklı offer tag'leri).
    /// Rastgele ilk offer'ı seçmek yanlış fiyatı gösterebilir veya istenmeyen
    /// offer token'ını satın alma akışına gönderebilir. Seçim sırası:
    ///   1) Trial/intro içermeyen, tag'siz, offerId'siz saf base plan offer'ı.
    ///   2) Trial/intro içermeyen herhangi bir offer.
    ///   3) Yedek: ilk offer (fiyat yine recurring fazdan okunur).
    /// </summary>
    private static SubscriptionOfferInfo SelectSubscriptionOffer(IReadOnlyList<SubscriptionOfferInfo> offers)
    {
        var basePlan = offers.FirstOrDefault(o =>
            !o.HasTrialOrIntro && o.OfferTags.Count == 0 && string.IsNullOrEmpty(o.OfferId));
        if (basePlan is not null)
        {
            return basePlan;
        }

        var noTrial = offers.FirstOrDefault(o => !o.HasTrialOrIntro);
        if (noTrial is not null)
        {
            return noTrial;
        }

        return offers.FirstOrDefault()
               ?? new SubscriptionOfferInfo(null, Array.Empty<string>(), null, null, null, false);
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

    /// <summary>Java int döndüren metodları (örn. getRecurrenceMode) JNI ile okur.</summary>
    private static int CallJavaInt(Java.Lang.Object target, string methodName)
    {
        var classRef = JNIEnv.GetObjectClass(target.Handle);
        try
        {
            var methodId = JNIEnv.GetMethodID(classRef, methodName, "()I");
            return JNIEnv.CallIntMethod(target.Handle, methodId);
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

    /// <summary>
    /// Satın almayı Play tarafından acknowledge eder (non-consumable kayıtlar
    /// 3 gün içinde onaylanmazsa otomatik iade edilir). Fire-and-forget:
    /// beklemek GetEntitlementAsync'i geciktirir; başarısız onaylama
    /// IsAcknowledged hâlâ false olduğu için bir sonraki sorguda otomatik
    /// yeniden denenir.
    ///
    /// Not (hak akışı): hak, acknowledge'a DEĞİL backend doğrulamasına
    /// bağlıdır — acknowledge başarısız olsa bile doğrulanmış satın alma
    /// entitlement verir. Acknowledge yalnızca Play'in 3 günlük otomatik
    /// iadesini önlemek içindir; iade gerçekleşirse backend doğrulaması
    /// inaktif döner ve hak zaten düşer.
    /// </summary>
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
                // Transient hatada kısa bir retry: kullanıcı uygulamayı uzun süre
                // açmazsa tek deneme 3 günlük otomatik iade riskini taşırdı.
                const int attemptCount = 2;
                for (var attempt = 1; attempt <= attemptCount; attempt++)
                {
                    var result = await AcknowledgeOnceAsync(token).ConfigureAwait(false);
                    if (result is null or BillingResponseCode.Ok)
                    {
                        break;
                    }

                    var transient = result is
                        BillingResponseCode.ServiceUnavailable or
                        BillingResponseCode.NetworkError or
                        BillingResponseCode.BillingUnavailable or
                        BillingResponseCode.Error;

                    if (attempt < attemptCount && transient)
                    {
                        // 1-2 saniyelik kısa bekleme — geçici ağ/servis hatası için.
                        await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
                        continue;
                    }

                    // Geçici hatalar ile diğer hatalar ayrı loglanır ki başarısız
                    // acknowledge görünmez kalmasın; her iki durumda da sonraki
                    // sorgu (IsAcknowledged false) yeniden dener. Kalıcı sayılabilecek
                    // hatalar (DEVELOPER_ERROR, ITEM_NOT_OWNED) yeniden denense bile
                    // zararsızdır — başarısız onaylama 3 günlük otomatik iade riskini
                    // taşır, vazgeçmek daha tehlikelidir.
                    System.Diagnostics.Debug.WriteLine(transient
                        ? $"[StorePurchase] Acknowledge geçici hata — sonraki sorguda yeniden denenecek: {result} {attempt}"
                        : $"[StorePurchase] Acknowledge diğer hata — sonraki sorguda yeniden denenecek: {result} {attempt}");
                }
            }
            catch (Exception ex)
            {
                // Exception ile sonuçlanan onaylama (örn. billing client bağlantısı
                // yok) — bir sonraki sorguda tekrar deneneceği için log yeterli.
                System.Diagnostics.Debug.WriteLine($"[StorePurchase] Acknowledge failed: {ex.Message}");
            }
        });
    }

    /// <summary>Tek acknowledge denemesi; hata durumunda BillingResponseCode döner, exception'da null.</summary>
    private async Task<BillingResponseCode?> AcknowledgeOnceAsync(string token)
    {
        try
        {
            var client = await EnsureBillingClientAsync(CancellationToken.None).ConfigureAwait(false);
            var @params = AcknowledgePurchaseParams.NewBuilder()
                .SetPurchaseToken(token)
                .Build();
            var result = await RunOnUiThreadTaskAsync(
                () => client.AcknowledgePurchaseAsync(@params),
                CancellationToken.None).ConfigureAwait(false);
            return result.ResponseCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorePurchase] Acknowledge attempt failed: {ex.Message}");
            return null;
        }
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
