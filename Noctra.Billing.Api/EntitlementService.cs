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
            VerifiedAtUtc = verifiedAt
        };
    }

    /// <summary>RTDN bildirimindeki token için kayıtlı entitlement'ı yeniden doğrular.</summary>
    public async Task<bool> ReverifyByTokenAsync(
        string purchaseToken,
        string productId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken) || !_config.IsKnownProduct(productId))
        {
            return false;
        }

        var tokenHash = HashToken(purchaseToken);
        var existing = await _store.GetByPurchaseTokenHashAsync(tokenHash, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            // Token daha önce bu backend'de hiç görülmedi — hangi kuruluma ait
            // olduğu bilinemediği için güncellenemez (RTDN best-effort).
            return false;
        }

        var verification = await _billingApi.VerifyAsync(
                productId,
                purchaseToken,
                _config.PackageName,
                cancellationToken)
            .ConfigureAwait(false);

        await _store.UpsertAsync(new StoredEntitlementRow
        {
            InstallationId = existing.InstallationId,
            ProductId = productId,
            PurchaseTokenHash = tokenHash,
            EntitlementType = verification.EntitlementType,
            IsActive = verification.IsActive,
            ExpiresAtUtc = verification.ExpiresAtUtc,
            AutoRenewEnabled = verification.AutoRenewEnabled,
            State = verification.State,
            LastVerifiedAtUtc = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        return true;
    }

    public static string HashToken(string purchaseToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken)));
}
