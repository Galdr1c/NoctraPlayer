namespace Noctra.Billing.Api;

/// <summary>İstemci girdisi doğrulaması başarısız olduğunda 400 döndürür.</summary>
public sealed class BillingRequestException : Exception
{
    public BillingRequestException(string message) : base(message)
    {
    }
}

/// <summary>
/// Doğrulama akışını yönetir — tamamen stateless. İstemcinin gönderdiği
/// packageName/productId'ye asla güvenilmez; yalnızca sunucu yapılandırmasındaki
/// sabitler kabul edilir; gerçek durum her zaman Google'dan okunur. Kalıcı
/// entitlement verisi tutulmaz: Noctra'nın merkezi kullanıcı hesabı yoktur,
/// hak kaynağı Google'dır ve client her açılışta token'ı yeniden doğrular.
/// </summary>
public sealed class EntitlementService
{
    private readonly BillingConfig _config;
    private readonly IPlayBillingApi _billingApi;

    public EntitlementService(BillingConfig config, IPlayBillingApi billingApi)
    {
        _config = config;
        _billingApi = billingApi;
    }

    public async Task<VerifiedEntitlementResponse> VerifyAsync(
        BillingVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1) Sunucu tarafı doğrulama — istemci girdisi güvenilmez.
        if (string.IsNullOrWhiteSpace(request.PurchaseToken))
        {
            throw new BillingRequestException("purchaseToken boş.");
        }

        // Abuse/cost koruması: Play purchase token'ları ~100-400 karakterdir;
        // aşırı uzun değerler (bot taraması, dev payload) doğrudan reddedilir.
        if (request.PurchaseToken.Length is < 10 or > 4096)
        {
            throw new BillingRequestException("purchaseToken geçersiz uzunlukta.");
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

        // 3) Client'ın uygulayacağı normalize sonuç.
        return new VerifiedEntitlementResponse
        {
            IsActive = verification.IsActive,
            EntitlementType = verification.EntitlementType,
            ExpiresAtUtc = verification.ExpiresAtUtc,
            AutoRenewEnabled = verification.AutoRenewEnabled,
            State = verification.State,
            IsTrialPeriod = verification.IsTrialPeriod,
            VerifiedAtUtc = DateTime.UtcNow
        };
    }
}
