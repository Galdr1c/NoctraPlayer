using System.Security.Cryptography;
using System.Text;

namespace Noctra.Billing.Api;

/// <summary>İstemci girdisi doğrulaması başarısız olduğunda 400 döndürür.</summary>
public sealed class BillingRequestException : Exception
{
    public BillingRequestException(string message) : base(message)
    {
    }
}

/// <summary>
/// Doğrulama akışını yönetir. İstemcinin gönderdiği packageName/productId'ye
/// asla güvenilmez — yalnızca sunucu yapılandırmasındaki sabitler kabul edilir;
/// gerçek durum her zaman Google'dan okunur.
/// </summary>
public sealed class EntitlementService
{
    private readonly BillingConfig _config;
    private readonly IPlayBillingApi _billingApi;
    private readonly EntitlementStore _store;

    public EntitlementService(BillingConfig config, IPlayBillingApi billingApi, EntitlementStore store)
    {
        _config = config;
        _billingApi = billingApi;
        _store = store;
    }

    public async Task<VerifiedEntitlementResponse> VerifyAndStoreAsync(
        BillingVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1) Sunucu tarafı doğrulama — istemci girdisi güvenilmez.
        if (string.IsNullOrWhiteSpace(request.PurchaseToken))
        {
            throw new BillingRequestException("purchaseToken boş.");
        }

        if (string.IsNullOrWhiteSpace(request.InstallationId))
        {
            throw new BillingRequestException("installationId boş.");
        }

        if (!string.Equals(request.PackageName, _config.PackageName, StringComparison.Ordinal))
        {
            throw new BillingRequestException("packageName sunucu yapılandırmasıyla uyuşmuyor.");
        }

        if (!_config.IsKnownProduct(request.ProductId))
        {
            throw new BillingRequestException($"productId sunucu yapılandırmasında yok: {request.ProductId}");
        }

        // 2) Google'dan gerçek durumu oku.
        var verification = await _billingApi.VerifyAsync(
                request.ProductId,
                request.PurchaseToken,
                _config.PackageName,
                cancellationToken)
            .ConfigureAwait(false);

        // 3) Kaydet (ham token yerine özeti).
        var verifiedAt = DateTime.UtcNow;
        await _store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = request.InstallationId,
            ProductId = request.ProductId,
            PurchaseTokenHash = HashToken(request.PurchaseToken),
            EntitlementType = verification.EntitlementType,
            IsActive = verification.IsActive,
            ExpiresAtUtc = verification.ExpiresAtUtc,
            AutoRenewEnabled = verification.AutoRenewEnabled,
            State = verification.State,
            IsTrialPeriod = verification.IsTrialPeriod,
            LastVerifiedAtUtc = verifiedAt
        }, cancellationToken).ConfigureAwait(false);

        // 4) Client'ın uygulayacağı normalize sonuç.
        return new VerifiedEntitlementResponse
        {
            IsActive = verification.IsActive,
            EntitlementType = verification.EntitlementType,
            ExpiresAtUtc = verification.ExpiresAtUtc,
            AutoRenewEnabled = verification.AutoRenewEnabled,
            State = verification.State,
            IsTrialPeriod = verification.IsTrialPeriod,
            VerifiedAtUtc = verifiedAt
        };
    }

    /// <summary>
    /// RTDN bildirimindeki token için kayıtlı entitlement'ı yeniden doğrular.
    /// Google'ın RTDN SubscriptionNotification.subscriptionId ve
    /// OneTimeProductNotification.sku alanları deprecated olduğu için ürün
    /// kimliği çağırandan alınmaz — store'daki token kayıtlarından türetilir.
    /// </summary>
    public async Task<bool> ReverifyByTokenAsync(
        string purchaseToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken))
        {
            return false;
        }

        var tokenHash = HashToken(purchaseToken);
        var existing = await _store.GetAllByPurchaseTokenHashAsync(tokenHash, cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count == 0)
        {
            // Token daha önce bu backend'de hiç görülmedi — hangi kuruluma ait
            // olduğu bilinemediği için güncellenemez (RTDN best-effort).
            return false;
        }

        // Ürün kimliği store'daki satırlardan türetilir (bir satın alma token'ı
        // tek ürüne bağlıdır; yine de güvenli tarafta kalınır). Yalnızca sunucu
        // yapılandırmasında bilinen ürünler doğrulanır — config'den çıkarılmış
        // eski bir ürünün satırı kalırsa bilinmeyen productId ile Play'e istek
        // atılmaz (kalıcı 503 → sonsuz Pub/Sub retry riski). Aynı satın alma
        // birden fazla kurulumda (telefon + tablet) restore edilmiş olabilir —
        // sonuç token'ı paylaşan BÜTÜN satırlara yazılır; yalnızca birini
        // güncellemek diğer cihazı eski state'te bırakırdı.
        var productIds = existing
            .Select(x => x.ProductId)
            .Where(_config.IsKnownProduct)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (productIds.Length == 0)
        {
            // Store'da yalnızca artık bilinmeyen (retired) ürün satırları var —
            // doğrulanacak bir şey yok; retry anlamsız (kalıcı durum).
            return false;
        }

        var handled = false;
        foreach (var productId in productIds)
        {
            var verification = await _billingApi.VerifyAsync(
                    productId,
                    purchaseToken,
                    _config.PackageName,
                    cancellationToken)
                .ConfigureAwait(false);

            var lastVerifiedAtUtc = DateTime.UtcNow;
            foreach (var row in existing.Where(r =>
                         string.Equals(r.ProductId, productId, StringComparison.Ordinal)))
            {
                await _store.UpsertAsync(new StoredEntitlementRow
                {
                    InstallationId = row.InstallationId,
                    ProductId = productId,
                    PurchaseTokenHash = tokenHash,
                    EntitlementType = verification.EntitlementType,
                    IsActive = verification.IsActive,
                    ExpiresAtUtc = verification.ExpiresAtUtc,
                    AutoRenewEnabled = verification.AutoRenewEnabled,
                    State = verification.State,
                    IsTrialPeriod = verification.IsTrialPeriod,
                    LastVerifiedAtUtc = lastVerifiedAtUtc
                }, cancellationToken).ConfigureAwait(false);
            }

            handled = true;
        }

        return handled;
    }

    public static string HashToken(string purchaseToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken)));
}
