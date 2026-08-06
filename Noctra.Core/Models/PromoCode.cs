namespace Noctra.Models;

/// <summary>
/// Developer veya uzak JSON dosyası tarafından tanımlanan promosyon kodu.
/// </summary>
public sealed class PromoCodeDefinition
{
    public string Code { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowReuse { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Uzak promosyon kodu yapılandırması için JSON kök modeli.
/// "schemaVersion" alanı opsiyoneldir; yoksa sürüm 1 kabul edilir.
/// </summary>
public sealed class PromoCodeConfiguration
{
    public int SchemaVersion { get; set; }
    public List<PromoCodeDefinition> Codes { get; set; } = new();
}

/// <summary>
/// Promosyon kodu uygulama sonucunun hata kategorisi.
/// Kullanıcıya yalnızca yerelleştirilmiş, güvenli mesaj gösterilir;
/// bu kategoriler tanılama ve loglama için ayırt edicidir.
/// </summary>
public enum PromoCodeResultKind
{
    Success,
    Offline,
    Timeout,
    ServiceUnavailable,
    ConfigurationInvalid,
    CodeInvalid,
    CodeInactive,
    CodeExpired,
    AlreadyRedeemed,
    PersistenceFailed,
    Unknown
}

/// <summary>
/// Promosyon kodu uygulama sonucu.
/// </summary>
public sealed class PromoCodeRedemptionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? PremiumExpiresAtUtc { get; init; }
    public int DurationDays { get; init; }
    public PromoCodeResultKind Kind { get; init; } = PromoCodeResultKind.Success;

    public static PromoCodeRedemptionResult Ok(string message, DateTime premiumExpiresAtUtc, int durationDays) => new()
    {
        Success = true,
        Message = message,
        PremiumExpiresAtUtc = premiumExpiresAtUtc,
        DurationDays = durationDays,
        Kind = PromoCodeResultKind.Success
    };

    public static PromoCodeRedemptionResult Fail(string message, PromoCodeResultKind kind = PromoCodeResultKind.Unknown) => new()
    {
        Success = false,
        Message = message,
        Kind = kind
    };
}
