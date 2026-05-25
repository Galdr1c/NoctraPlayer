using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// License service implementation.
/// Free/Premium Store paketleri edition-driven çalışır; promosyon kodları Free sürümde süreli Premium açar.
/// </summary>
public class LicenseService : ObservableObject, ILicenseService
{
    static LicenseService()
    {
        LoadDotEnv();
    }

    private static void LoadDotEnv()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, ".env")) && !File.Exists(Path.Combine(root, "Noctra.sln")))
            {
                root = Path.GetDirectoryName(root);
            }

            var envPath = Path.Combine(root ?? string.Empty, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        {
                            Environment.SetEnvironmentVariable(key, value);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load .env file: {ex.Message}");
        }
    }

    private SubscriptionInfo _currentSubscription = new();
    private readonly IAppEditionService _appEditionService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly ILocalizationService? _localizationService;
    private bool _manualPremiumOverride;

#if DEBUG
    private const bool AllowsManualPremiumOverride = true;
#else
    private const bool AllowsManualPremiumOverride = false;
#endif

    /// <summary>
    /// Developer: Uzak JSON adresini burada sabitleyebilir, settings.json içindeki
    /// promoCodeConfigUrl alanı veya NOCTRA_PROMO_CODES_URL environment değişkeni ile verebilirsin.
    /// Beklenen JSON:
    /// { "codes": [ { "code": "PROMO-EXAMPLE-7D", "durationDays": 7, "isActive": true } ] }
    /// </summary>
    private const string DefaultRemotePromoCodesUrl = "";


    private static readonly JsonSerializerOptions PromoJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // ==========================================
    // FEATURE NAMES
    // ==========================================
    public static class Features
    {
        public const string AdFree = "ad_free";
        public const string EpgAutoRefresh = "epg_auto_refresh";
        public const string ResumePlayback = "resume_playback";
        public const string SleepTimer = "sleep_timer";
    }

    // ==========================================
    // LIMIT NAMES
    // ==========================================
    public static class Limits
    {
        public const string Profiles = "profiles";
        public const string CustomEpgUrls = "custom_epg_urls";
    }

    public LicenseService(IAppEditionService appEditionService)
        : this(appEditionService, new EphemeralSettingsService(), new HttpClient())
    {
    }

    public LicenseService(
        IAppEditionService appEditionService,
        ISettingsService settingsService,
        HttpClient httpClient,
        ILocalizationService? localizationService = null)
    {
        _appEditionService = appEditionService;
        _settingsService = settingsService;
        _httpClient = httpClient;
        _localizationService = localizationService;
        _settingsService.SettingsChanged += OnSettingsChanged;
        SyncSubscriptionFromSettings(notify: false);
    }

    // ==========================================
    // LEGACY PROPERTIES (backward compat)
    // ==========================================
    public bool IsPremium
    {
        get
        {
            SyncSubscriptionFromSettings(notify: false);
            return _currentSubscription.IsPremiumOrHigher;
        }
    }

    public bool CanUpgradeToPremium => _appEditionService.IsFreeEdition;
    public bool IsEditionLockedPremium => _appEditionService.IsPremiumEdition;
    public DateTime? PromoPremiumExpiresAtUtc => _settingsService.Settings.PromoPremiumExpiresAtUtc;
    public string? ActivePromoCode => _settingsService.Settings.ActivePromoCode;

    public void ActivatePremium()
    {
        if (_appEditionService.IsPremiumEdition || !AllowsManualPremiumOverride)
        {
            return;
        }

        _manualPremiumOverride = true;
        _settingsService.Settings.PromoPremiumExpiresAtUtc = null;
        _settingsService.Settings.ActivePromoCode = null;
        _currentSubscription.Tier = SubscriptionTier.Premium;
        _currentSubscription.ExpiresAt = null;
        _currentSubscription.IsTrialPeriod = false;
        RaiseSubscriptionChanged();
    }

    public void DeactivatePremium()
    {
        if (_appEditionService.IsPremiumEdition || !AllowsManualPremiumOverride || _currentSubscription.Tier == SubscriptionTier.Free)
        {
            return;
        }

        _manualPremiumOverride = false;
        _settingsService.Settings.PromoPremiumExpiresAtUtc = null;
        _settingsService.Settings.ActivePromoCode = null;
        _currentSubscription.Tier = SubscriptionTier.Free;
        _currentSubscription.ExpiresAt = null;
        _currentSubscription.IsTrialPeriod = false;
        RaiseSubscriptionChanged();
    }

    public string GetPriceText()
    {
        return "499.95 TL (Tek Sefer)";
    }

    // ==========================================
    // PROMO CODE SYSTEM
    // ==========================================
    public async Task<PromoCodeRedemptionResult> ApplyPromoCodeAsync(string promoCode)
    {
        if (_appEditionService.IsPremiumEdition)
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.PremiumEdition", "Bu paket zaten kalıcı Premium sürüm."));
        }

        var normalizedCode = NormalizePromoCode(promoCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.EmptyCode", "Lütfen promosyon kodunu girin."));
        }

        var promoCodesResult = await LoadPromoCodesAsync();
        if (!promoCodesResult.Success)
        {
            return PromoCodeRedemptionResult.Fail(promoCodesResult.ErrorMessage);
        }

        var matchedCode = promoCodesResult.Codes.FirstOrDefault(code =>
            NormalizePromoCode(code.Code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (matchedCode == null)
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.InvalidCode", "Promosyon kodu bulunamadı veya geçersiz."));
        }

        if (!matchedCode.IsActive)
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.Inactive", "Bu promosyon kodu aktif değil."));
        }

        if (matchedCode.DurationDays <= 0)
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.InvalidDuration", "Bu promosyon kodu için geçerli süre tanımlanmamış."));
        }

        if (matchedCode.ValidUntilUtc.HasValue && matchedCode.ValidUntilUtc.Value <= DateTime.UtcNow)
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.Expired", "Bu promosyon kodunun kullanım süresi dolmuş."));
        }

        var settings = _settingsService.Settings;
        settings.RedeemedPromoCodes ??= new List<string>();
        if (!matchedCode.AllowReuse && settings.RedeemedPromoCodes.Any(code =>
                NormalizePromoCode(code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
        {
            return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.AlreadyRedeemed", "Bu promosyon kodu daha önce bu cihazda kullanılmış."));
        }

        _manualPremiumOverride = false;

        var startDate = settings.PromoPremiumExpiresAtUtc.HasValue && settings.PromoPremiumExpiresAtUtc.Value > DateTime.UtcNow
            ? settings.PromoPremiumExpiresAtUtc.Value
            : DateTime.UtcNow;
        var expiresAt = startDate.AddDays(matchedCode.DurationDays);

        settings.ActivePromoCode = normalizedCode;
        settings.PromoPremiumExpiresAtUtc = expiresAt;
        if (!settings.RedeemedPromoCodes.Any(code => NormalizePromoCode(code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
        {
            settings.RedeemedPromoCodes.Add(normalizedCode);
        }

        await _settingsService.SaveAsync();
        SyncSubscriptionFromSettings(notify: true);

        return PromoCodeRedemptionResult.Ok(
            Localize("GlobalSettings.Promo.SuccessFormat", "Promosyon kodu uygulandı. Premium {0} tarihine kadar aktif.", FormatLocalDate(expiresAt)),
            expiresAt,
            matchedCode.DurationDays);
    }

    private async Task<PromoCodeLoadResult> LoadPromoCodesAsync()
    {
        var remoteUrl = GetRemotePromoCodesUrl();
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return PromoCodeLoadResult.Fail(Localize("GlobalSettings.Promo.Error.ConfigMissing", "Promosyon kodu yapılandırması bulunamadı. Lütfen uygulama yöneticisinin promosyon kodu URL'sini yapılandırdığından emin olun."));
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await _httpClient.GetAsync(remoteUrl, cts.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            return PromoCodeLoadResult.Ok(ParsePromoCodeJson(json));
        }
        catch
        {
            return PromoCodeLoadResult.Fail(Localize("GlobalSettings.Promo.Error.ConfigLoadFailed", "Promosyon kodu yapılandırması yüklenemedi. Lütfen internet bağlantınızı kontrol edip tekrar deneyin."));
        }
    }

    private static List<PromoCodeDefinition> ParsePromoCodeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<PromoCodeDefinition>();
        }

        var trimmed = json.TrimStart();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return JsonSerializer.Deserialize<List<PromoCodeDefinition>>(json, PromoJsonOptions) ?? new List<PromoCodeDefinition>();
        }

        var config = JsonSerializer.Deserialize<PromoCodeConfiguration>(json, PromoJsonOptions);
        return config?.Codes ?? new List<PromoCodeDefinition>();
    }

    private string GetRemotePromoCodesUrl()
    {
        var envUrl = Environment.GetEnvironmentVariable("NOCTRA_PROMO_CODES_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl.Trim();
        }

        var settingsUrl = _settingsService.Settings.PromoCodeConfigUrl;
        if (!string.IsNullOrWhiteSpace(settingsUrl))
        {
            return settingsUrl.Trim();
        }

        return DefaultRemotePromoCodesUrl;
    }

    private sealed record PromoCodeLoadResult(
        bool Success,
        IReadOnlyList<PromoCodeDefinition> Codes,
        string ErrorMessage)
    {
        public static PromoCodeLoadResult Ok(IReadOnlyList<PromoCodeDefinition> codes) =>
            new(true, codes, string.Empty);

        public static PromoCodeLoadResult Fail(string errorMessage) =>
            new(false, Array.Empty<PromoCodeDefinition>(), errorMessage);
    }

    private void OnSettingsChanged()
    {
        SyncSubscriptionFromSettings(notify: true);
    }

    private void SyncSubscriptionFromSettings(bool notify)
    {
        var oldTier = _currentSubscription.Tier;
        var oldExpiresAt = _currentSubscription.ExpiresAt;
        var oldIsTrial = _currentSubscription.IsTrialPeriod;

        if (_appEditionService.IsPremiumEdition)
        {
            _currentSubscription.Tier = SubscriptionTier.Premium;
            _currentSubscription.ExpiresAt = null;
            _currentSubscription.IsTrialPeriod = false;
        }
        else if (_manualPremiumOverride)
        {
            _currentSubscription.Tier = SubscriptionTier.Premium;
            _currentSubscription.ExpiresAt = null;
            _currentSubscription.IsTrialPeriod = false;
        }
        else
        {
            var promoExpiresAt = _settingsService.Settings.PromoPremiumExpiresAtUtc;
            if (promoExpiresAt.HasValue && promoExpiresAt.Value > DateTime.UtcNow)
            {
                _currentSubscription.Tier = SubscriptionTier.Premium;
                _currentSubscription.ExpiresAt = promoExpiresAt;
                _currentSubscription.IsTrialPeriod = true;
            }
            else
            {
                _currentSubscription.Tier = SubscriptionTier.Free;
                _currentSubscription.ExpiresAt = null;
                _currentSubscription.IsTrialPeriod = false;
            }
        }

        if (notify && (oldTier != _currentSubscription.Tier || oldExpiresAt != _currentSubscription.ExpiresAt || oldIsTrial != _currentSubscription.IsTrialPeriod))
        {
            RaiseSubscriptionChanged();
        }
    }

    private void RaiseSubscriptionChanged()
    {
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
        OnPropertyChanged(nameof(PromoPremiumExpiresAtUtc));
        OnPropertyChanged(nameof(ActivePromoCode));
        SubscriptionChanged?.Invoke();
    }

    private static string NormalizePromoCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().Replace(" ", string.Empty).ToUpperInvariant();
    }

    private static string FormatLocalDate(DateTime utcDate)
    {
        return utcDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    private string Localize(string key, string fallback, params object[] args)
    {
        var template = _localizationService?.GetString(key);
        if (string.IsNullOrWhiteSpace(template) || string.Equals(template, key, StringComparison.Ordinal))
        {
            template = fallback;
        }

        return args.Length == 0 ? template : string.Format(template, args);
    }

    // ==========================================
    // NEW TIER SYSTEM
    // ==========================================

    public SubscriptionInfo GetCurrentSubscription()
    {
        SyncSubscriptionFromSettings(notify: false);
        return _currentSubscription;
    }

    public SubscriptionTier CurrentTier
    {
        get
        {
            SyncSubscriptionFromSettings(notify: false);
            return _currentSubscription.Tier;
        }
    }

    public bool IsFeatureAvailable(string featureName)
    {
        var tier = CurrentTier;

        return featureName switch
        {
            Features.AdFree => tier == SubscriptionTier.Premium,
            Features.EpgAutoRefresh => tier == SubscriptionTier.Premium,
            Features.ResumePlayback => tier == SubscriptionTier.Premium,
            Features.SleepTimer => tier == SubscriptionTier.Premium,
            _ => false
        };
    }

    public bool IsWithinLimit(string limitName, int currentCount)
    {
        var tier = CurrentTier;

        int maxAllowed = limitName switch
        {
            Limits.Profiles => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxProfiles
                : TierLimits.Free.MaxProfiles,
            Limits.CustomEpgUrls => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxCustomEpgUrls
                : TierLimits.Free.MaxCustomEpgUrls,
            _ => int.MaxValue
        };

        return currentCount < maxAllowed;
    }

    public int GetLimit(string limitName)
    {
        var tier = CurrentTier;

        return limitName switch
        {
            Limits.Profiles => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxProfiles
                : TierLimits.Free.MaxProfiles,
            Limits.CustomEpgUrls => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxCustomEpgUrls
                : TierLimits.Free.MaxCustomEpgUrls,
            _ => int.MaxValue
        };
    }

    public event Action? SubscriptionChanged;

    public async Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier)
    {
        if (targetTier != SubscriptionTier.Premium || _appEditionService.IsPremiumEdition)
        {
            return false;
        }

        var candidateUris = new[]
        {
            _appEditionService.PremiumStoreLaunchUri,
            _appEditionService.PremiumStoreWebUri
        };

        foreach (var candidate in candidateUris.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = true
                });
                return await Task.FromResult(true);
            }
            catch
            {
                // Try the next URI.
            }
        }

        return false;
    }

    public async Task RefreshSubscriptionStatusAsync()
    {
        SyncSubscriptionFromSettings(notify: true);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Debug/test için tier'ı manuel ayarla (Event fırlatmaz)
    /// </summary>
    public void SetTierForTesting(SubscriptionTier tier)
    {
        if (!AllowsManualPremiumOverride || (_appEditionService.IsPremiumEdition && tier != SubscriptionTier.Premium))
        {
            return;
        }

        _manualPremiumOverride = tier == SubscriptionTier.Premium;
        _currentSubscription.Tier = tier;
        _currentSubscription.ExpiresAt = null;
        _currentSubscription.IsTrialPeriod = false;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
    }

    private sealed class EphemeralSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public event Action? SettingsChanged;
        public Task LoadAsync() => Task.CompletedTask;
        public Task LoadProfileSettingsAsync(int profileId) => Task.CompletedTask;
        public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) => Task.FromResult<AppSettings?>(Settings);
        public Task SaveAsync()
        {
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
        public void ResetToDefaults() => SettingsChanged?.Invoke();
        public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
    }
}
