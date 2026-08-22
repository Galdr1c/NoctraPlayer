using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// License service implementation.
/// Free/Premium Store paketleri edition-driven çalışır; promosyon kodları Free sürümde süreli Premium açar.
/// </summary>
public class LicenseService : ObservableObject, ILicenseService, IDisposable
{
    static LicenseService()
    {
        EnvFileLoader.Load();
    }

    private SubscriptionInfo _currentSubscription = new();
    private readonly IAppEditionService _appEditionService;
    private readonly ISettingsService _settingsService;
    private readonly ISecurityService _securityService;
    private readonly HttpClient _httpClient;
    private readonly ILocalizationService? _localizationService;
    private readonly IPlatformActionService? _platformActionService;
    private readonly IStorePurchaseService? _storePurchaseService;
    private readonly IDispatcherService? _dispatcherService;

    /// <summary>
    /// Süreli Premium'un (abonelik/promosyon) bitiş anında UI'ı güncelleyen
    /// timer. Uygulama açık kalırken SubscriptionChanged'in yalnızca settings
    /// değişikliği/sync'te gelmesi Premium rozetinin süre dolduktan sonra
    /// ekranda kalmasına yol açardı; bu timer bitiş anında sync tetikler.
    /// </summary>
    private Timer? _expiryTimer;
    private readonly object _expiryTimerGate = new();
    private bool _disposed;

    /// <summary>
    /// Timer'ın hedeflediği bitiş anı. Aynı değer için tekrar planlama yapılmaz
    /// (getter'lar her çağrıda SyncSubscriptionFromSettings çalıştırdığı için
    /// gereksiz Timer üretimini/çevrimini önler). Timer tetiklenince sıfırlanır.
    /// </summary>
    private DateTime? _scheduledExpiryUtc;

    /// <summary>
    /// Mağazadan (Google Play) doğrulanan Premium hakları. Yalnızca mağaza
    /// desteği olan platformlarda güncellenir; haklar değişince veya platform
    /// yüzeyi hazır olduktan sonra çağıran tarafından yenilenir. Constructor,
    /// son doğrulanmış hakkı diskteki önbellekten senkron yükler (soğuk başlangıçta
    /// Free→Premium sıçraması olmasın); async refresh tamamlanınca güncel durumla
    /// ezilir. Android cold-start'ta Billing refresh Activity hazır olana kadar
    /// özellikle ertelenir.
    /// </summary>
    private StoreEntitlement _storeEntitlement = StoreEntitlement.None;
    private bool _manualPremiumOverride;

    /// <summary>
    /// Şifrelenmiş PromoGrant çözülemiyor/çözümlenemiyorsa true olur.
    /// Fail-closed davranış (Free'e düşme) doğrudur; ancak kullanıcıya sessiz
    /// kalınmamalıdır — ayarlar ekranı bu bayrağı görüp uyarı gösterir.
    /// </summary>
    private bool _promoGrantCorrupted;

    /// <summary>
    /// Promo redemption'ın read–modify–save döngüsünü serileştirir. Aynı anda
    /// iki ApplyPromoCodeAsync çağrısı olursa ikisi de eski grant'i okuyup kendi
    /// sonucunu yazabilir ("son yazan kazanır") ve bir kodun süresi ya da
    /// redeemed geçmişi kaybolabilir. UI'daki IsApplyingPromoCode bayrağı
    /// yalnızca tek ViewModel'deki çift tıklamayı engeller; servis iki ayrı
    /// ViewModel, pencere veya doğrudan çağrı karşısında da güvenli olmalıdır.
    /// </summary>
    private readonly SemaphoreSlim _redemptionLock = new(1, 1);

    /// <summary>
    /// Mağaza haklarını yeniden doğrulayan akışları serileştirir. Aynı anda
    /// kaynaklar tetiklenebilir — satın alma değişince EntitlementChanged ve
    /// OnResume/Activated'de RefreshSubscriptionStatusAsync (Android'de Activity
    /// yüzeyi hazırlandıktan sonra). Eşzamanlı GetEntitlementAsync çağrıları
    /// hem gereksiz Play/backend yükü hem de önbelleğe yazma yarışı üretir;
    /// bu kilit tek seferde yalnızca bir refresh çalıştırır (sonuncusu bekler).
    /// </summary>
    private readonly SemaphoreSlim _storeRefreshLock = new(1, 1);

#if DEBUG
    private const bool AllowsManualPremiumOverride = true;
#else
    private const bool AllowsManualPremiumOverride = false;
#endif

    /// <summary>
    /// Release build'de kullanılan güvenilir promosyon kodu JSON adresi.
    /// URL kaynak kodda TUTULMAZ (GitHub'da görünmemesi için) — build sırasında
    /// NOCTRA_PROMO_CODES_URL ortam değişkeninden okunup assembly metadata
    /// olarak derlemeye gömülür (bkz. Noctra.Core.csproj). Kullanıcının
    /// değiştirebileceği settings.json alanından veya runtime ortam
    /// değişkeninden ASLA alınmaz.
    /// Beklenen JSON:
    /// { "codes": [ { "code": "PROMO-EXAMPLE-7D", "durationDays": 7, "isActive": true } ] }
    /// Üretimde bu akış server-side redemption ile değiştirilmelidir; client-side
    /// doğrulama yalnızca test/basit kampanyalar içindir.
    /// </summary>
    private static string TrustedPromoEndpoint
    {
        get
        {
#if DEBUG
            // DEBUG'da URL yalnızca runtime ortamından gelir (LoadDotEnv → .env
            // veya NOCTRA_PROMO_CODES_URL). Build-time metadata okunmaz; böylece
            // testler .env içeriğinden bağımsız, deterministik kalır.
            return string.Empty;
#else
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "Noctra.PromoEndpoint", StringComparison.Ordinal))
                ?.Value ?? string.Empty;
#endif
        }
    }


    private static readonly JsonSerializerOptions PromoJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Tek bir promosyon kodunun verebileceği maksimum Premium süresi (gün).
    /// DurationDays > 0 tek başına yeterli değildir: aşırı büyük değerler
    /// DateTime.AddDays sınırını aşıp ArgumentOutOfRangeException fırlatabilir
    /// ve anlamsız derecede uzun Premium süresi üretebilir.
    /// </summary>
    private const int MaximumPromoDurationDays = 365;

    /// <summary>
    /// Birikmiş toplam Premium süresi üst sınırı (gün). Yeni bir kod bu sınırı
    /// aşan bir toplam süre üretecekse reddedilir.
    /// </summary>
    private const int MaximumTotalPromoDurationDays = 730;

    /// <summary>
    /// Uzak promosyon yapılandırması için kabul edilen maksimum yanıt boyutu
    /// (bayt). Aşırı büyük bir yanıt yapılandırma hatası sayılır; böylece
    /// hatalı/beklenmedik bir uç nokta bellek tüketimini şişiremez.
    /// </summary>
    private const long MaxPromoConfigBytes = 256 * 1024;

    /// <summary>
    /// Desteklenen promosyon yapılandırma şema sürümü. JSON içinde
    /// "schemaVersion" belirtilmişse ve bu değerden büyükse yapılandırma
    /// tamamen reddedilir (yeni şema bilinmeden yanlış yorumlanmaz).
    /// </summary>
    private const int SupportedPromoConfigSchemaVersion = 1;

    // ==========================================
    // FEATURE NAMES
    // ==========================================
    public static class Features
    {
        public const string AdFree = "ad_free";
        public const string EpgAutoRefresh = "epg_auto_refresh";
        public const string ResumePlayback = "resume_playback";
        public const string SleepTimer = "sleep_timer";
        public const string PictureInPicture = "picture_in_picture";
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
        : this(appEditionService, new EphemeralSettingsService(), new HttpClient(), securityService: new SecurityService())
    {
    }

    public LicenseService(
        IAppEditionService appEditionService,
        ISettingsService settingsService,
        HttpClient httpClient,
        ILocalizationService? localizationService = null,
        ISecurityService? securityService = null,
        IPlatformActionService? platformActionService = null,
        IStorePurchaseService? storePurchaseService = null,
        IDispatcherService? dispatcherService = null,
        bool deferInitialStoreRefresh = false)
    {
        _appEditionService = appEditionService;
        _settingsService = settingsService;
        _securityService = securityService ?? new SecurityService();
        _httpClient = httpClient;
        _localizationService = localizationService;
        _platformActionService = platformActionService;
        _storePurchaseService = storePurchaseService;
        _dispatcherService = dispatcherService;

        if (_storePurchaseService is not null)
        {
            _storePurchaseService.EntitlementChanged += OnStoreEntitlementChanged;

            // Soğuk başlangıç: son doğrulanmış hak diskteki önbellekten SENKRON
            // yüklenir. Async refresh tamamlanana kadar kullanıcının Free
            // görünmesini (UI sıçraması) ve çevrimdışı açılışta hakkın refresh
            // timeout'u boyunca kaybolmasını önler. Önbellek yalnızca backend'den
            // doğrulanmış (IsVerified=true) sonucu içerir; abonelik bitişi gerçek
            // Play expiryTime'ı olduğundan SyncSubscriptionFromSettings'in
            // zamana duyarlı kontrolü önbellek üzerinde de doğru çalışır
            // (süresi dolan abonelik yine Free üretir).
            if (TryReadStoreEntitlementCache(out var cached))
            {
                _storeEntitlement = cached;
            }

            if (!deferInitialStoreRefresh)
            {
                _ = RefreshStoreEntitlementAsync();
            }
        }

        _settingsService.SettingsChanged += OnSettingsChanged;
        SyncSubscriptionFromSettings(notify: false);
    }

    private void OnStoreEntitlementChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = RefreshStoreEntitlementAsync();
    }

    /// <summary>
    /// Mağaza haklarını yeniden doğrular ve abonelik durumunu günceller.
    /// Sorgu başarısız olursa son bilinen hak korunur (fail-open değil,
    /// mevcut önbellek değeri geçerli kalır). Aynı anda yalnızca bir refresh
    /// çalışır; ikinci çağrı ilkinin bitmesini bekler.
    /// </summary>
    private async Task RefreshStoreEntitlementAsync(CancellationToken cancellationToken = default)
    {
        if (_storePurchaseService is null || _disposed)
        {
            return;
        }

        await _storeRefreshLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshStoreEntitlementCoreAsync(cancellationToken);
        }
        finally
        {
            _storeRefreshLock.Release();
        }
    }

    private async Task RefreshStoreEntitlementCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var storePurchaseService = _storePurchaseService;
            if (storePurchaseService is null)
            {
                return;
            }

            var entitlement = await storePurchaseService.GetEntitlementAsync(cancellationToken);
            if (entitlement.IsVerified)
            {
                // Backend doğrulaması geçti: hem kullan hem önbelleğe yaz.
                _storeEntitlement = entitlement;
                await PersistStoreEntitlementCacheAsync(entitlement);
            }
            else if (TryReadStoreEntitlementCache(out var cached))
            {
                // Doğrulama hizmetine ulaşılamadı (ağ/sunucu): fail-safe olarak
                // son bilinen doğrulanmış hak kullanılır — hak asla düşürülmez.
                _storeEntitlement = cached;
            }
            else
            {
                _storeEntitlement = StoreEntitlement.None;
            }

            SyncSubscriptionFromSettings(notify: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Store entitlement refresh failed: {ex}");
        }
    }

    /// <summary>
    /// Son doğrulanmış hakkı ayarlara önbellekler — doğrulama hizmeti
    /// çevrimdışıyken fail-safe değer olarak kullanılır. Best-effort: kayıt
    /// hatası hakkın bellekte kullanılmasını engellemez.
    /// </summary>
    private async Task PersistStoreEntitlementCacheAsync(StoreEntitlement entitlement)
    {
        try
        {
            var json = JsonSerializer.Serialize(entitlement);
            if (!string.Equals(
                    _settingsService.Settings.StoreVerifiedEntitlementJson,
                    json,
                    StringComparison.Ordinal))
            {
                _settingsService.Settings.StoreVerifiedEntitlementJson = json;
                await _settingsService.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LicenseService] Failed to persist store entitlement cache: {ex.Message}");
        }
    }

    private bool TryReadStoreEntitlementCache(out StoreEntitlement entitlement)
    {
        entitlement = StoreEntitlement.None;
        var json = _settingsService.Settings.StoreVerifiedEntitlementJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<StoreEntitlement>(json);
            if (parsed is null)
            {
                return false;
            }

            entitlement = parsed;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LicenseService] Failed to read store entitlement cache: {ex.Message}");
            return false;
        }
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

    /// <summary>
    /// Play'de onay bekleyen (PENDING) bir satın alma var mı? (örn. operatör
    /// faturalaması). Pending satın alma PREMIUM VERMEZ — hak yalnızca PURCHASED
    /// + backend doğrulamasıyla açılır. UI bu bayrağı görüp "ödeme bekleniyor"
    /// bildirimi gösterir; ödeme çözülünce bir sonraki mağaza sorgusu bayrağı
    /// temizler.
    /// </summary>
    public bool HasPendingStorePurchase => _storeEntitlement.HasPendingPurchase;

    /// <summary>
    /// Kalıcı (tek seferlik) Premium paketi sahibi mi? Upsell UX'i için —
    /// kalıcı paket sahibine aylık satın alma sunulmaz.
    /// </summary>
    public bool HasLifetimePremium => _storeEntitlement.HasLifetimePremium;

    /// <summary>
    /// Google Play'de AKTİF bir süreli abonelik var mı (kalıcı paketten
    /// bağımsız)? Kalıcı paket alındıktan sonra bile aylık abonelik Google
    /// Play'de yenilenmeye devam edebilir; upsell bu bayrağı görüp kullanıcıyı
    /// aboneliği iptal etmesi için abonelik yönetimine yönlendirir.
    /// Uygulama aboneliği ASLA otomatik "iptal edildi" varsaymaz.
    /// </summary>
    public bool HasActiveStoreSubscription =>
        _storeEntitlement.SubscriptionExpiresAtUtc.HasValue &&
        _storeEntitlement.SubscriptionExpiresAtUtc.Value > DateTime.UtcNow;

    /// <summary>
    /// Etkin Premium'un kaynağı (kalıcı paket / abonelik / promosyon / edisyon).
    /// </summary>
    public PremiumSource CurrentPremiumSource
    {
        get
        {
            SyncSubscriptionFromSettings(notify: false);
            return _currentSubscription.Source;
        }
    }

    public DateTime? PromoPremiumExpiresAtUtc => ReadPromoGrant()?.ExpiresAtUtc;

    /// <summary>
    /// Etkin Premium'un biteceği an (mağaza aboneliği veya promosyon;
    /// kalıcı edisyon/kalıcı paket için null). UI durum metinleri bunu kullanır.
    /// </summary>
    public DateTime? PremiumExpiresAtUtc
    {
        get
        {
            SyncSubscriptionFromSettings(notify: false);
            return _currentSubscription.Tier == SubscriptionTier.Premium
                ? _currentSubscription.ExpiresAt
                : null;
        }
    }

    public string? ActivePromoCode => ReadPromoGrant()?.ActivePromoCode;

    /// <summary>
    /// Kayıtlı PromoGrant decrypt/deserialize edilemiyorsa true.
    /// Getter, bayrağı güncellemek için grant'i yeniden okur.
    /// </summary>
    public bool IsPromoGrantCorrupted
    {
        get
        {
            _ = ReadPromoGrant();
            return _promoGrantCorrupted;
        }
    }

    public void ActivatePremium()
    {
        if (_appEditionService.IsPremiumEdition || !AllowsManualPremiumOverride)
        {
            return;
        }

        _manualPremiumOverride = true;
        ClearPromoState();
        _currentSubscription.Tier = SubscriptionTier.Premium;
        _currentSubscription.ExpiresAt = null;
        _currentSubscription.IsTrialPeriod = false;
        // Debug/test amaçlı manuel override kalıcı Premium gibi davranır.
        _currentSubscription.Source = PremiumSource.PremiumEdition;
        RaiseSubscriptionChanged();
    }

    public void DeactivatePremium()
    {
        if (_appEditionService.IsPremiumEdition || !AllowsManualPremiumOverride || _currentSubscription.Tier == SubscriptionTier.Free)
        {
            return;
        }

        _manualPremiumOverride = false;
        ClearPromoState();
        _currentSubscription.Tier = SubscriptionTier.Free;
        _currentSubscription.ExpiresAt = null;
        _currentSubscription.IsTrialPeriod = false;
        _currentSubscription.Source = PremiumSource.None;
        RaiseSubscriptionChanged();
    }

    // ==========================================
    // PROMO CODE SYSTEM
    // ==========================================
    public async Task<PromoCodeRedemptionResult> ApplyPromoCodeAsync(string promoCode)
    {
        if (_disposed)
        {
            return PromoCodeRedemptionResult.Fail(
                Localize("GlobalSettings.Promo.Error.Generic", "Promosyon kodu uygulanamadı. Lütfen daha sonra tekrar deneyin."),
                PromoCodeResultKind.Unknown);
        }

        await _redemptionLock.WaitAsync();
        try
        {
            return await ApplyPromoCodeCoreAsync(promoCode);
        }
        finally
        {
            _redemptionLock.Release();
        }
    }

    private async Task<PromoCodeRedemptionResult> ApplyPromoCodeCoreAsync(string promoCode)
    {
        try
        {
            if (_appEditionService.IsPremiumEdition)
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.PremiumEdition", "Bu paket zaten kalıcı Premium sürüm."), PromoCodeResultKind.Unknown);
            }

            if (_storeEntitlement.HasLifetimePremium)
            {
                // Lifetime her zaman kazanır: eklenen süre hiç kullanılmaz, bu
                // yüzden kod girip "Süre Ekle" görmek kullanıcıyı yanıltır.
                return PromoCodeRedemptionResult.Fail(
                    Localize("GlobalSettings.Promo.Error.LifetimePremium", "Kalıcı Premium paket sahibisiniz; promosyon kodları gerekmez."),
                    PromoCodeResultKind.Unknown);
            }

            var normalizedCode = NormalizePromoCode(promoCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.EmptyCode", "Lütfen promosyon kodunu girin."), PromoCodeResultKind.CodeInvalid);
            }

            var promoCodesResult = await LoadPromoCodesAsync();
            if (!promoCodesResult.Success)
            {
                return PromoCodeRedemptionResult.Fail(promoCodesResult.ErrorMessage, promoCodesResult.Kind);
            }

            var matchedCode = promoCodesResult.Codes.FirstOrDefault(code =>
                NormalizePromoCode(code.Code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));

            if (matchedCode == null)
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.InvalidCode", "Promosyon kodu bulunamadı veya geçersiz."), PromoCodeResultKind.CodeInvalid);
            }

            if (!matchedCode.IsActive)
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.Inactive", "Bu promosyon kodu aktif değil."), PromoCodeResultKind.CodeInactive);
            }

            if (matchedCode.DurationDays is <= 0 or > MaximumPromoDurationDays)
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.DurationLimitExceeded", "Bu promosyon kodunun süresi geçersiz."), PromoCodeResultKind.ConfigurationInvalid);
            }

            if (matchedCode.ValidUntilUtc.HasValue && matchedCode.ValidUntilUtc.Value <= DateTime.UtcNow)
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.Expired", "Bu promosyon kodunun kullanım süresi dolmuş."), PromoCodeResultKind.CodeExpired);
            }

            var promoGrant = ReadPromoGrant();
            var redeemedPromoCodes = (promoGrant?.RedeemedPromoCodes ?? new List<string>()).ToList();
            if (!matchedCode.AllowReuse && redeemedPromoCodes.Any(code =>
                    NormalizePromoCode(code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.AlreadyRedeemed", "Bu promosyon kodu daha önce bu cihazda kullanılmış."), PromoCodeResultKind.AlreadyRedeemed);
            }

            _manualPremiumOverride = false;

            var currentExpiresAt = promoGrant?.ExpiresAtUtc;
            var startDate = currentExpiresAt.HasValue && currentExpiresAt.Value > DateTime.UtcNow
                ? currentExpiresAt.Value
                : DateTime.UtcNow;
            var expiresAt = startDate.AddDays(matchedCode.DurationDays);

            if (expiresAt > DateTime.UtcNow.AddDays(MaximumTotalPromoDurationDays))
            {
                return PromoCodeRedemptionResult.Fail(Localize("GlobalSettings.Promo.Error.TotalDurationLimitReached", "Toplam Premium süresi üst sınırına ulaşıldığı için kod uygulanamadı."), PromoCodeResultKind.Unknown);
            }

            if (!redeemedPromoCodes.Any(code => NormalizePromoCode(code).Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
            {
                redeemedPromoCodes.Add(normalizedCode);
            }

            WritePromoGrant(new PromoGrant(
                normalizedCode,
                expiresAt,
                redeemedPromoCodes));

            try
            {
                await _settingsService.SaveAsync();
            }
            catch (Exception)
            {
                // Kalıcılık başarısız (SettingsPersistenceException veya sarılmamış
                // bir I/O hatası): kullanıcıya "başarılı" göstermeden önce bellek
                // durumunu eski grant'a geri al, böylece sonraki açılışla tutarsız
                // kalmasın. Kayıt başarısız olduğu için disk zaten eski haliyle kalır.
                if (promoGrant is null)
                {
                    _settingsService.Settings.PromoGrant = null;
                    _settingsService.Settings.ActivePromoCode = null;
                    _settingsService.Settings.PromoPremiumExpiresAtUtc = null;
                    _settingsService.Settings.RedeemedPromoCodes.Clear();
                }
                else
                {
                    WritePromoGrant(promoGrant);
                }

                return PromoCodeRedemptionResult.Fail(Localize(
                    "GlobalSettings.Promo.Error.SaveFailed",
                    "Promosyon kodu uygulanamadı: ayarlar kaydedilemedi. Lütfen tekrar deneyin."),
                    PromoCodeResultKind.PersistenceFailed);
            }

            SyncSubscriptionFromSettings(notify: true);

            return PromoCodeRedemptionResult.Ok(
                Localize("GlobalSettings.Promo.SuccessFormat", "Promosyon kodu uygulandı. Premium {0} tarihine kadar aktif.", FormatLocalDate(expiresAt)),
                expiresAt,
                matchedCode.DurationDays);
        }
        catch (Exception ex)
        {
            // Beklenmeyen servis hatası: kullanıcıya teknik ayrıntı gösterilmez;
            // yerelleştirilmiş güvenli mesaj döner, gerçek exception loglanır.
            System.Diagnostics.Debug.WriteLine($"[LicenseService] ApplyPromoCode failed: {ex}");
            return PromoCodeRedemptionResult.Fail(
                Localize("GlobalSettings.Promo.Error.Generic", "Promosyon kodu uygulanamadı. Lütfen daha sonra tekrar deneyin."),
                PromoCodeResultKind.Unknown);
        }
    }

    private async Task<PromoCodeLoadResult> LoadPromoCodesAsync()
    {
        var remoteUrl = GetRemotePromoCodesUrl();
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            // Store kullanıcısı URL yapılandıramaz; "yöneticiye başvurun" geliştirici
            // mesajıdır. Bunun yerine kullanıcıya hizmetin kullanılamadığı söylenir.
            return PromoCodeLoadResult.Fail(
                Localize("GlobalSettings.Promo.Error.ServiceUnavailable", "Promosyon hizmeti şu anda kullanılamıyor. Lütfen uygulamanın güncel olduğundan emin olup daha sonra tekrar deneyin."),
                PromoCodeResultKind.ServiceUnavailable);
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            // GitHub gist raw yanıtları max-age=300 (5 dk) cache'li olduğundan,
            // gist güncellendikten hemen sonra eski kod listesi görülebilirdi.
            // no-cache isteği CDN cache'ini bypass eder; her build'de (debug/
            // release/store) gist değişiklikleri anında yansır.
            //
            // Not: Bilinçli kapsam — liste üzerinde ETag/imza yok. no-cache politikası
            // CDN cache'ini devre dışı bıraktığı için ETag yeniden kullanımıyla çelişir;
            // imza ise build'e gömülü bir genel anahtar ve güvenilir imzalama altyapısı
            // gerektirir. Üretim akışı server-side redemption ile değiştirildiğinde
            // her ikisi de birlikte değerlendirilmelidir.
            using var request = new HttpRequestMessage(HttpMethod.Get, remoteUrl);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true
            };
            // ResponseHeadersRead: yanıt gövdesi tamamen belleğe alınmadan başlıklar
            // gelir gelmez dön; böylece aşağıdaki sınırlı akış okuması (256 KB sınırı)
            // indirme anında devreye girer ve aşırı büyük gövde belleği şişiremez.
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // 404 → yapılandırma adresi geçersiz; 5xx/diğer → hizmet çalışmıyor.
                // Hiçbirinde kullanıcının internetine işaret edilmez.
                var notFound = response.StatusCode == System.Net.HttpStatusCode.NotFound;
                return PromoCodeLoadResult.Fail(
                    Localize(
                        notFound
                            ? "GlobalSettings.Promo.Error.ConfigurationInvalid"
                            : "GlobalSettings.Promo.Error.ServiceUnavailable",
                        notFound
                            ? "Promosyon kodu listesi şu anda geçersiz. Lütfen daha sonra tekrar deneyin."
                            : "Promosyon hizmeti şu anda kullanılamıyor. Lütfen uygulamanın güncel olduğundan emin olup daha sonra tekrar deneyin."),
                    notFound
                        ? PromoCodeResultKind.ConfigurationInvalid
                        : PromoCodeResultKind.ServiceUnavailable);
            }

            // HTML yanıtı (örn. yanlış konumlandırılmış uç noktanın oturum açma
            // sayfası) asla kod listesi olarak yorumlanmamalı.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                return PromoCodeLoadResult.Fail(
                    Localize("GlobalSettings.Promo.Error.ConfigurationInvalid", "Promosyon kodu listesi şu anda geçersiz. Lütfen daha sonra tekrar deneyin."),
                    PromoCodeResultKind.ConfigurationInvalid);
            }

            // Boyut sınırı: önce Content-Length başlığı, sonra akış sınırlı okunur.
            if (response.Content.Headers.ContentLength.HasValue &&
                response.Content.Headers.ContentLength.Value > MaxPromoConfigBytes)
            {
                return PromoCodeLoadResult.Fail(
                    Localize("GlobalSettings.Promo.Error.ConfigurationInvalid", "Promosyon kodu listesi şu anda geçersiz. Lütfen daha sonra tekrar deneyin."),
                    PromoCodeResultKind.ConfigurationInvalid);
            }

            var json = await ReadContentWithLimitAsync(response.Content, cts.Token);
            return PromoCodeLoadResult.Ok(ParsePromoCodeJson(json));
        }
        catch (OperationCanceledException)
        {
            return PromoCodeLoadResult.Fail(
                Localize("GlobalSettings.Promo.Error.Timeout", "Promosyon hizmeti yanıt vermedi. Lütfen daha sonra tekrar deneyin."),
                PromoCodeResultKind.Timeout);
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Promo config network error: {ex.Message}");
            return PromoCodeLoadResult.Fail(
                Localize("GlobalSettings.Promo.Error.Offline", "İnternet bağlantısı kurulamadı. Lütfen bağlantınızı kontrol edip tekrar deneyin."),
                PromoCodeResultKind.Offline);
        }
        catch (PromoConfigException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Promo config invalid: {ex.Message}");
            return PromoCodeLoadResult.Fail(
                Localize("GlobalSettings.Promo.Error.ConfigurationInvalid", "Promosyon kodu listesi şu anda geçersiz. Lütfen daha sonra tekrar deneyin."),
                PromoCodeResultKind.ConfigurationInvalid);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Promo config JSON invalid: {ex.Message}");
            return PromoCodeLoadResult.Fail(
                Localize("GlobalSettings.Promo.Error.ConfigurationInvalid", "Promosyon kodu listesi şu anda geçersiz. Lütfen daha sonra tekrar deneyin."),
                PromoCodeResultKind.ConfigurationInvalid);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Promo config load failed: {ex}");
            return PromoCodeLoadResult.Fail(
                Localize("GlobalSettings.Promo.Error.ServiceUnavailable", "Promosyon hizmeti şu anda kullanılamıyor. Lütfen uygulamanın güncel olduğundan emin olup daha sonra tekrar deneyin."),
                PromoCodeResultKind.ServiceUnavailable);
        }
    }

    /// <summary>
    /// Yanıt gövdesini bayt cinsinden sınırlandırılmış biçimde okur.
    /// Limit aşılırsa <see cref="PromoConfigException"/> fırlatılır;
    /// böylece aşırı büyük yanıt belleğe alınmaz.
    /// </summary>
    private static async Task<string> ReadContentWithLimitAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxPromoConfigBytes)
            {
                throw new PromoConfigException("Promo config response exceeds the maximum size.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static List<PromoCodeDefinition> ParsePromoCodeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new PromoConfigException("Promo config is empty.");
        }

        var trimmed = json.TrimStart();
        List<PromoCodeDefinition>? codes;
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            codes = JsonSerializer.Deserialize<List<PromoCodeDefinition>>(json, PromoJsonOptions)
                ?? throw new PromoConfigException("Promo config array could not be deserialized.");
        }
        else
        {
            var config = JsonSerializer.Deserialize<PromoCodeConfiguration>(json, PromoJsonOptions)
                ?? throw new PromoConfigException("Promo config could not be deserialized.");

            if (config.SchemaVersion > SupportedPromoConfigSchemaVersion)
            {
                throw new PromoConfigException($"Unsupported promo config schema version: {config.SchemaVersion}.");
            }

            codes = config.Codes;
        }

        return ValidatePromoCodeList(codes);
    }

    /// <summary>
    /// Yapılandırmayı tamamen kabul/reddeder: boş liste, boş kod girişi ve
    /// normalizasyon sonrası yinelenen kodlar (örn. "AB CD" ve "ABCD") bozuk
    /// kabul edilir. Böylece FirstOrDefault eşleşmesi JSON sırasına bağlı kalmaz.
    /// </summary>
    private static List<PromoCodeDefinition> ValidatePromoCodeList(List<PromoCodeDefinition>? codes)
    {
        if (codes is null || codes.Count == 0)
        {
            throw new PromoConfigException("Promo config contains no codes.");
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PromoCodeDefinition>(codes.Count);
        foreach (var code in codes)
        {
            if (code is null)
            {
                throw new PromoConfigException("Promo config contains a null entry.");
            }

            var normalizedCode = NormalizePromoCode(code.Code);
            if (normalizedCode.Length == 0)
            {
                throw new PromoConfigException("Promo config contains an entry with an empty code.");
            }

            if (!normalized.Add(normalizedCode))
            {
                throw new PromoConfigException($"Duplicate promo code after normalization: '{normalizedCode}'.");
            }

            result.Add(code);
        }

        return result;
    }

    /// <summary>
    /// Promosyon yapılandırma şemasının reddedilmesi gerektiğini belirtir
    /// (boş/bozuk JSON, yinelenen kod, desteklenmeyen schema sürümü, boyut aşımı).
    /// </summary>
    private sealed class PromoConfigException : Exception
    {
        public PromoConfigException(string message)
            : base(message)
        {
        }
    }

    private string GetRemotePromoCodesUrl()
    {
        // Kullanıcının ayar dosyasını değiştirip kendi promosyon JSON'unu
        // işaret etmesi, uygulamanın kendi geçerli PromoGrant üretmesiyle
        // sonuçlanırdı. Bu yüzden yalnızca DEBUG'da env override'ı kabul
        // edilir (geliştirici test kampanyaları); Release'de adres yalnızca
        // derleme sabitinden gelir ve kullanıcı girdisinden etkilenemez.
#if DEBUG
        var overrideUrl = Environment.GetEnvironmentVariable("NOCTRA_PROMO_CODES_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl.Trim();
        }
#endif

        return TrustedPromoEndpoint;
    }

    private sealed record PromoCodeLoadResult(
        bool Success,
        IReadOnlyList<PromoCodeDefinition> Codes,
        string ErrorMessage,
        PromoCodeResultKind Kind)
    {
        public static PromoCodeLoadResult Ok(IReadOnlyList<PromoCodeDefinition> codes) =>
            new(true, codes, string.Empty, PromoCodeResultKind.Success);

        public static PromoCodeLoadResult Fail(string errorMessage, PromoCodeResultKind kind) =>
            new(false, Array.Empty<PromoCodeDefinition>(), errorMessage, kind);
    }

    private sealed record PromoGrant(
        string ActivePromoCode,
        DateTime ExpiresAtUtc,
        List<string> RedeemedPromoCodes);

    private void OnSettingsChanged()
    {
        if (_disposed)
        {
            return;
        }

        SyncSubscriptionFromSettings(notify: true);
    }

    private void SyncSubscriptionFromSettings(bool notify)
    {
        var oldTier = _currentSubscription.Tier;
        var oldExpiresAt = _currentSubscription.ExpiresAt;
        var oldIsTrial = _currentSubscription.IsTrialPeriod;
        var oldSource = _currentSubscription.Source;
        // Pending satın alma bayrağı tek başına tier'ı değiştirmez (Free kalır),
        // ancak "ödeme bekleniyor" banner'ının açılıp kapanması için durum
        // değişiminde bildirim gerekir.
        var oldPendingPurchase = _storeEntitlement.HasPendingPurchase;

        if (_appEditionService.IsPremiumEdition)
        {
            _currentSubscription.Tier = SubscriptionTier.Premium;
            _currentSubscription.ExpiresAt = null;
            _currentSubscription.IsTrialPeriod = false;
            _currentSubscription.Source = PremiumSource.PremiumEdition;
        }
        else if (_manualPremiumOverride)
        {
            _currentSubscription.Tier = SubscriptionTier.Premium;
            _currentSubscription.ExpiresAt = null;
            _currentSubscription.IsTrialPeriod = false;
            // Debug/test amaçlı manuel override kalıcı Premium gibi davranır.
            _currentSubscription.Source = PremiumSource.PremiumEdition;
        }
        else
        {
            // Öncelik sırası: kalıcı mağaza paketi → (abonelik veya promosyon
            // süresi, hangisi daha geç ise o) → Free. Mağaza hakları yalnızca
            // mağaza desteği olan platformda (Android) dolu olabilir.
            var store = _storeEntitlement;
            var promoExpiresAt = ReadPromoGrant()?.ExpiresAtUtc;

            if (store.HasLifetimePremium)
            {
                // Tek seferlik kalıcı paket: Premium edition gibi süresiz.
                _currentSubscription.Tier = SubscriptionTier.Premium;
                _currentSubscription.ExpiresAt = null;
                _currentSubscription.IsTrialPeriod = false;
                _currentSubscription.Source = PremiumSource.GooglePlayLifetime;
            }
            else
            {
                // Kaynak ayrımı: süreli Premium'un nereden geldiği kaydedilir
                // (abonelik mi, promosyon mu). Kaynak, bitişi belirleyen
                // (en geç olan) süredir. IsTrialPeriod burada ASLA true
                // yapılmaz — ücretli abonelik/promosyon trial değildir.
                DateTime? endsAt = null;
                var source = PremiumSource.None;
                if (store.HasActivePremium && store.SubscriptionExpiresAtUtc.HasValue)
                {
                    endsAt = store.SubscriptionExpiresAtUtc;
                    source = PremiumSource.GooglePlaySubscription;
                }
                if (promoExpiresAt.HasValue &&
                    promoExpiresAt.Value > DateTime.UtcNow &&
                    (!endsAt.HasValue || promoExpiresAt.Value > endsAt.Value))
                {
                    endsAt = promoExpiresAt;
                    source = PremiumSource.Promo;
                }

                if (endsAt.HasValue && endsAt.Value > DateTime.UtcNow)
                {
                    _currentSubscription.Tier = SubscriptionTier.Premium;
                    _currentSubscription.ExpiresAt = endsAt;
                    // IsTrialPeriod yalnızca GERÇEK trial offer aktifken true
                    // olur — backend (subscriptionsv2.get → offerId +
                    // monetization → recurrenceMode) doğrular; ücretli aylık
                    // kullanıcı asla trial gibi görünmez.
                    _currentSubscription.IsTrialPeriod =
                        source == PremiumSource.GooglePlaySubscription && store.IsTrialPeriod;
                    _currentSubscription.Source = source;
                }
                else
                {
                    _currentSubscription.Tier = SubscriptionTier.Free;
                    _currentSubscription.ExpiresAt = null;
                    _currentSubscription.IsTrialPeriod = false;
                    _currentSubscription.Source = PremiumSource.None;
                }
            }
        }

        if (notify && (oldTier != _currentSubscription.Tier ||
                       oldExpiresAt != _currentSubscription.ExpiresAt ||
                       oldIsTrial != _currentSubscription.IsTrialPeriod ||
                       oldSource != _currentSubscription.Source ||
                       oldPendingPurchase != _storeEntitlement.HasPendingPurchase))
        {
            // Bildirim UI thread'e taşınır; state değişikliği senkron kalır.
            RaiseSubscriptionChangedSafely();
        }

        ScheduleExpiryCheck();
    }

    /// <summary>
    /// Süreli Premium varsa bitiş anına tek seferlik timer kurar (en fazla
    /// 24 saat ileri; daha uzunsa periyodik yeniden kurulum ile çalışır, böylece
    /// sistem saati değişikliği / uzun uyku da yakalanır). Bitiş anında sync
    /// tetiklenir ve SubscriptionChanged ile tüm Premium UI güncellenir.
    /// Süresiz Premium (kalıcı paket/edisyon) veya Free'de timer kurulmaz.
    /// </summary>
    private void ScheduleExpiryCheck()
    {
        if (_disposed)
        {
            // Getter'lar (IsPremium, CurrentTier vb.) SyncSubscriptionFromSettings
            // üzerinden buraya ulaşabilir; Dispose sonrası yeni timer yaratılırsa
            // bitiş anında SubscriptionChanged tekrar ateşlenebilir.
            return;
        }

        lock (_expiryTimerGate)
        {
            var expiresAt = _currentSubscription.Tier == SubscriptionTier.Premium
                ? _currentSubscription.ExpiresAt
                : null;

            if (!expiresAt.HasValue || expiresAt.Value <= DateTime.UtcNow)
            {
                _expiryTimer?.Dispose();
                _expiryTimer = null;
                _scheduledExpiryUtc = null;
                return;
            }

            // Hedef aynıysa mevcut timer'ı koru — getter'ların her çağrıda
            // yeni Timer üretmesini (churn) önler.
            if (_scheduledExpiryUtc == expiresAt.Value && _expiryTimer is not null)
            {
                return;
            }

            var remaining = expiresAt.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var due = remaining > TimeSpan.FromHours(24)
                ? TimeSpan.FromHours(24)
                : remaining;

            if (_expiryTimer is null)
            {
                _expiryTimer = new System.Threading.Timer(
                    OnExpiryCheckElapsed,
                    null,
                    due,
                    System.Threading.Timeout.InfiniteTimeSpan);
            }
            else
            {
                _expiryTimer.Change(due, System.Threading.Timeout.InfiniteTimeSpan);
            }

            _scheduledExpiryUtc = expiresAt.Value;
        }
    }

    private void OnExpiryCheckElapsed(object? state)
    {
        // Timer thread pool üzerinde çalışır; state güncellemesi burada yapılır.
        // SubscriptionChanged bildirimi SyncSubscriptionFromSettings içinden
        // RaiseSubscriptionChangedSafely ile UI thread'e taşınır.
        // Önce hedefi temizle: ScheduleExpiryCheck 24 saatlik dilim için
        // yeniden planlayabilsin.
        lock (_expiryTimerGate)
        {
            _scheduledExpiryUtc = null;
        }

        try
        {
            SyncSubscriptionFromSettings(notify: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Expiry check failed: {ex.Message}");
        }
    }

    private void RaiseSubscriptionChanged()
    {
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
        OnPropertyChanged(nameof(HasPendingStorePurchase));
        OnPropertyChanged(nameof(PromoPremiumExpiresAtUtc));
        OnPropertyChanged(nameof(PremiumExpiresAtUtc));
        OnPropertyChanged(nameof(ActivePromoCode));
        OnPropertyChanged(nameof(HasLifetimePremium));
        OnPropertyChanged(nameof(HasActiveStoreSubscription));
        OnPropertyChanged(nameof(CurrentPremiumSource));
        SubscriptionChanged?.Invoke();
    }

    /// <summary>
    /// SubscriptionChanged bildirimini UI thread'e taşır. Mağaza sorgusu
    /// (GetEntitlementAsync) thread pool üzerinde tamamlanabilir ve
    /// SyncSubscriptionFromSettings o anda hangi thread'deyse orada çalışır;
    /// ViewModel'lerin PropertyChanged'i UI thread'de bekler. Dispatcher varsa
    /// bildirim UI thread'de yapılır (state güncellemesi yine çağıran thread'de
    /// senkron kalır — awaiter'lar güncel değeri görür), yoksa doğrudan
    /// (test ortamı).
    /// </summary>
    private void RaiseSubscriptionChangedSafely()
    {
        if (_dispatcherService is not null)
        {
            try
            {
                _dispatcherService.BeginInvoke(RaiseSubscriptionChanged);
                return;
            }
            catch (Exception ex)
            {
                // Dispatcher kapanış aşamasındaysa marshal başarısız olabilir;
                // bildirim o zaman burada, çağıran thread üzerinde yapılır.
                System.Diagnostics.Debug.WriteLine($"[LicenseService] Subscription change marshal failed: {ex.Message}");
            }
        }

        RaiseSubscriptionChanged();
    }

    private PromoGrant? ReadPromoGrant()
    {
        var encryptedGrant = _settingsService.Settings.PromoGrant;
        if (string.IsNullOrWhiteSpace(encryptedGrant))
        {
            _promoGrantCorrupted = false;
            return ImportLegacyPromoGrant();
        }

        try
        {
            var json = _securityService.Decrypt(encryptedGrant);
            if (string.IsNullOrWhiteSpace(json))
            {
                _promoGrantCorrupted = true;
                return null;
            }

            var grant = JsonSerializer.Deserialize<PromoGrant>(json, PromoJsonOptions);
            if (grant == null ||
                string.IsNullOrWhiteSpace(grant.ActivePromoCode) ||
                grant.ExpiresAtUtc <= DateTime.MinValue)
            {
                _promoGrantCorrupted = true;
                return null;
            }

            _promoGrantCorrupted = false;
            return grant with
            {
                ActivePromoCode = NormalizePromoCode(grant.ActivePromoCode),
                RedeemedPromoCodes = grant.RedeemedPromoCodes
                    .Where(code => !string.IsNullOrWhiteSpace(NormalizePromoCode(code)))
                    .Select(NormalizePromoCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            // Bozuk grant: Free'e düşmek doğru (fail-closed), fakat sessiz
            // kalmamak gerekir — ayarlar ekranı uyarı gösterebilir.
            System.Diagnostics.Debug.WriteLine($"[LicenseService] Promo grant corrupt: {ex.Message}");
            _promoGrantCorrupted = true;
            return null;
        }
    }

    private void WritePromoGrant(PromoGrant grant)
    {
        var json = JsonSerializer.Serialize(grant, PromoJsonOptions);
        _settingsService.Settings.PromoGrant = _securityService.Encrypt(json);
        _settingsService.Settings.ActivePromoCode = null;
        _settingsService.Settings.PromoPremiumExpiresAtUtc = null;
        _settingsService.Settings.RedeemedPromoCodes.Clear();
    }

    private PromoGrant? ImportLegacyPromoGrant()
    {
        var settings = _settingsService.Settings;
        if (string.IsNullOrWhiteSpace(settings.ActivePromoCode) ||
            !settings.PromoPremiumExpiresAtUtc.HasValue)
        {
            return null;
        }

        var grant = new PromoGrant(
            NormalizePromoCode(settings.ActivePromoCode),
            settings.PromoPremiumExpiresAtUtc.Value,
            (settings.RedeemedPromoCodes ?? new List<string>())
                .Where(code => !string.IsNullOrWhiteSpace(NormalizePromoCode(code)))
                .Select(NormalizePromoCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());

        if (string.IsNullOrWhiteSpace(grant.ActivePromoCode) || grant.ExpiresAtUtc <= DateTime.MinValue)
        {
            return null;
        }

        if (!grant.RedeemedPromoCodes.Any(code => string.Equals(code, grant.ActivePromoCode, StringComparison.OrdinalIgnoreCase)))
        {
            grant.RedeemedPromoCodes.Add(grant.ActivePromoCode);
        }

        WritePromoGrant(grant);
        return grant;
    }

    private void ClearPromoState()
    {
        _settingsService.Settings.PromoGrant = null;
        _settingsService.Settings.ActivePromoCode = null;
        _settingsService.Settings.PromoPremiumExpiresAtUtc = null;
        _settingsService.Settings.RedeemedPromoCodes.Clear();
    }

    private static string NormalizePromoCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
    }

    private static string FormatLocalDate(DateTime utcDate)
    {
        return utcDate.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
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
            Features.PictureInPicture => tier == SubscriptionTier.Premium,
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
        if (_disposed || targetTier != SubscriptionTier.Premium || _appEditionService.IsPremiumEdition)
        {
            return false;
        }

        // Mağaza desteği olan platformlarda (Android) satın alma Play Billing
        // üzerinden başlatılır; aylık abonelik önceliklidir. Masaüstünde store
        // servisi null olduğundan davranış değişmez (URI açma).
        if (_storePurchaseService is { IsSupported: true })
        {
            try
            {
                var products = await _storePurchaseService.GetProductsAsync().ConfigureAwait(false);
                var product = products.FirstOrDefault(p => p.Kind == StoreProductKind.Subscription)
                              ?? products.FirstOrDefault();
                if (product is null)
                {
                    return false;
                }

                var result = await _storePurchaseService.LaunchPurchaseAsync(product).ConfigureAwait(false);
                return result.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LicenseService] Store purchase flow failed: {ex.Message}");
                return false;
            }
        }

        var candidateUris = new[]
        {
            _appEditionService.PremiumStoreLaunchUri,
            _appEditionService.PremiumStoreWebUri
        };

        foreach (var candidate in candidateUris.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (_platformActionService is not null)
            {
                if (await _platformActionService.OpenUrlAsync(candidate).ConfigureAwait(false))
                {
                    return true;
                }

                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = true
                });
                return await Task.FromResult(true).ConfigureAwait(false);
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
        if (_disposed)
        {
            return;
        }

        // Resume/focus ve açık sync'lerde mağaza (Play) hakları yeniden sorgulanır:
        // başka cihazda yapılan satın alma, refund, iptal, account hold veya grace
        // period değişikliği uygulama arka plandayken olduysa burada yakalanır.
        // Store servisi yoksa (masaüstü) anında döner. Mağaza sorgusu
        // _storeRefreshLock ile serileştirilir; yalnızca doğrulanmış sonuç
        // önbelleği değiştirir — geçici hatada hak asla erken düşmez.
        await RefreshStoreEntitlementAsync();
        SyncSubscriptionFromSettings(notify: true);
    }

    /// <summary>
    /// Servis kaynaklarını temizler. Uygulama kapanışında DI container
    /// singleton'ı dispose ettiğinde çağrılır:
    ///  - Event abonelikleri kaldırılır — Dispose sonrası callback gelmesin ve
    ///    nesne event source tarafından tutulmasın (test sızıntısı).
    ///  - Expiry timer kapatılır.
    /// Semaphore'lar bilinçli olarak dispose EDİLMEZ: in-flight bir refresh
    /// (OnResume fire-and-forget) kilidi elinde tutarken Dispose çağrılırsa
    /// WaitAsync/Release ObjectDisposedException üretebilir; semaphore'lar
    /// yalnızca küçük bir wait handle tutar ve GC tarafından temizlenir.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_storePurchaseService is not null)
        {
            _storePurchaseService.EntitlementChanged -= OnStoreEntitlementChanged;
        }

        _settingsService.SettingsChanged -= OnSettingsChanged;

        lock (_expiryTimerGate)
        {
            _expiryTimer?.Dispose();
            _expiryTimer = null;
            _scheduledExpiryUtc = null;
        }
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
        _currentSubscription.Source = tier == SubscriptionTier.Premium
            ? PremiumSource.PremiumEdition
            : PremiumSource.None;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
        OnPropertyChanged(nameof(PremiumExpiresAtUtc));
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
        public void NotifySettingsChanged() => SettingsChanged?.Invoke();
        public void ResetToDefaults() => SettingsChanged?.Invoke();
        public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
    }
}
