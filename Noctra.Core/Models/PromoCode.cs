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
/// </summary>
public sealed class PromoCodeConfiguration
{
    public List<PromoCodeDefinition> Codes { get; set; } = new();
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

    public static PromoCodeRedemptionResult Ok(string message, DateTime premiumExpiresAtUtc, int durationDays) => new()
    {
        Success = true,
        Message = message,
        PremiumExpiresAtUtc = premiumExpiresAtUtc,
        DurationDays = durationDays
    };

    public static PromoCodeRedemptionResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
