using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using Noctra.Core.Services;

namespace Noctra.ViewModels;

/// <summary>
/// Ayarlar view model
/// </summary>
public partial class SettingsViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlaylistService _playlistService;
    private readonly ISettingsService _settingsService;
    private readonly IEpgService _epgService;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IWatchHistoryService _watchHistoryService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDiagnosticReportService _diagnosticService;
    private readonly ILicenseService _licenseService;
    private readonly IAppVersionService _appVersionService;
    private readonly ILocalizationService _localizationService;
    private readonly ISecurityService _securityService;
    private readonly IAppPathService _appPaths;
    private readonly ICacheService _cacheService;
    private readonly IProfileService? _profileService;
    private readonly IDispatcherService? _dispatcherService;
    private readonly SettingsAutoSaveCoordinator _autoSaveCoordinator;
    private readonly SettingsChangeOriginGate _settingsChangeOriginGate = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _statisticsScanGate = new(1, 1);
    private Task _initialStatisticsTask = Task.CompletedTask;
    private readonly object _autoSaveStatusSync = new();
    private readonly HashSet<SettingsStatusArea> _pendingAutoSaveAreas = [];
    private CancellationTokenSource? _epgRefreshWatchCts;
    
    private int _isRefreshOperationRunning;
    private string? _activeRefreshScope;
    private bool _wasEpgRefreshActive;
    private bool _isLoadingSettings;
    private bool _autoSaveEnabled;

    private sealed record ChannelListStatistics(
        DateTime? LastUpdated,
        int TotalChannels);

    private sealed record EpgStatistics(
        int TotalPrograms,
        int TotalChannels,
        DateTime? LastUpdated,
        string? LastError);

    // Auto-clear: status mesajları birkaç saniye sonra otomatik temizlenir.
    private readonly Dictionary<SettingsStatusArea, CancellationTokenSource> _statusAutoClearTokens = new();
    private static readonly TimeSpan StatusAutoClearDelay = TimeSpan.FromSeconds(7);

    private static readonly HashSet<string> AutoSavePropertyNames = new(StringComparer.Ordinal)
    {
        nameof(UserAgent),
        nameof(AutoPlayNext),
        nameof(IsBufferSmall),
        nameof(IsBufferNormal),
        nameof(IsBufferLarge),
        nameof(SelectedDataUsage),
        nameof(SubtitleEnabled),
        nameof(SubtitleLanguage),
        nameof(SubtitleFontSize),
        nameof(PreferredAudioLanguage),
        nameof(SelectedDownloadQuality),
        nameof(DownloadWifiOnly),
        nameof(DownloadPath),
        nameof(ShowDownloadNotification),
        nameof(IsDarkTheme),
        nameof(AppLanguage),
        nameof(ChannelListRefreshFrequencyHours),
        nameof(EpgRefreshFrequencyHours),
        nameof(CustomEpgUrl),
        nameof(EpgEnabled),
        nameof(EpgTimeOffsetHours),
        nameof(SaveWatchHistory),
        nameof(ClearHistoryOnExit),
        nameof(WatchHistoryRetentionIndex)
    };

    private static readonly HashSet<string> DebouncedAutoSavePropertyNames = new(StringComparer.Ordinal)
    {
        nameof(UserAgent),
        nameof(DownloadPath),
        nameof(CustomEpgUrl)
    };

    // ============ Oynatma Ayarları ============
    [ObservableProperty]
    private string _userAgent = string.Empty;

    [ObservableProperty]
    private bool _autoPlayNext;

    [ObservableProperty]
    private bool _isBufferSmall;

    [ObservableProperty]
    private bool _isBufferNormal;

    [ObservableProperty]
    private bool _isBufferLarge;
    
    [ObservableProperty]
    private int _selectedDataUsage;

    [ObservableProperty]
    private bool _subtitleEnabled;

    [ObservableProperty]
    private string _subtitleLanguage = "en";

    [ObservableProperty]
    private int _subtitleFontSize = 40;

    [ObservableProperty]
    private string _preferredAudioLanguage = "en";
    
    // ============ İndirme Ayarları ============
    
    [ObservableProperty]
    private int _selectedDownloadQuality;
    
    [ObservableProperty]
    private bool _downloadWifiOnly;
    
    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _showDownloadNotification;

    // ============ Görünüm ============
    
    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _appLanguage = "en";

    [ObservableProperty]
    private int _channelListRefreshFrequencyHours;

    [ObservableProperty]
    private int _epgRefreshFrequencyHours;

    [ObservableProperty]
    private int _channelListRefreshFrequencyIndex;

    [ObservableProperty]
    private int _epgRefreshFrequencyIndex;

    [ObservableProperty]
    private int _epgTimeOffsetHours;

    [ObservableProperty]
    private int _epgTimeOffsetIndex;

    [ObservableProperty]
    private bool _epgEnabled;

    partial void OnChannelListRefreshFrequencyIndexChanged(int value)
    {
        ChannelListRefreshFrequencyHours = value switch
        {
            1 => 1,
            2 => 3,
            3 => 12,
            4 => 24,
            5 => 48,  // 2 gün
            6 => 72,  // 3 gün
            7 => 168, // 7 gün
            _ => 0
        };
    }

    partial void OnEpgRefreshFrequencyIndexChanged(int value)
    {
        EpgRefreshFrequencyHours = value switch
        {
            1 => 1,
            2 => 3,
            3 => 12,
            4 => 24,
            5 => 48,  // 2 gün
            6 => 72,  // 3 gün
            7 => 168, // 7 gün
            _ => 0
        };
    }

    partial void OnEpgTimeOffsetIndexChanged(int value)
    {
        EpgTimeOffsetHours = value - 12; // Index 12 is '0 (Otomatik)', so value 12 - 12 = 0
    }

    [ObservableProperty]
    private string _customEpgUrl = string.Empty;

    [ObservableProperty]
    private ObservableCollection<EpgUrlItem> _customEpgUrls = new();

    // ============ Privacy settings ============

    [ObservableProperty]
    private bool _saveWatchHistory;

    [ObservableProperty]
    private int _watchHistoryRetentionIndex;

    [ObservableProperty]
    private bool _clearHistoryOnExit;


    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply immediately on desktop and mobile. On Android this updates
        // Avalonia's ThemeVariant layer without recreating the Activity.
        _themeService.SetTheme(value);

        if (!_isLoadingSettings && _settingsService.Settings.IsDarkTheme != value)
        {
            _settingsService.Settings.IsDarkTheme = value;
            if (!_autoSaveEnabled)
            {
                _ = _settingsChangeOriginGate.RunOwnedSaveAsync(_settingsService.SaveAsyncBestEffort);
            }
        }
    }

    partial void OnAppLanguageChanged(string value)
    {
        if (_isLoadingSettings || !_autoSaveEnabled)
        {
            return;
        }

        _localizationService.SetLanguage(string.IsNullOrWhiteSpace(value) ? "en" : value);
    }
    
    // ============ EPG & Playlist ============
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _appearanceStatusMessage = string.Empty;

    [ObservableProperty]
    private string _playbackStatusMessage = string.Empty;

    [ObservableProperty]
    private string _audioStatusMessage = string.Empty;

    [ObservableProperty]
    private string _downloadStatusMessage = string.Empty;

    [ObservableProperty]
    private string _channelStatusMessage = string.Empty;

    [ObservableProperty]
    private string _epgStatusMessage = string.Empty;

    [ObservableProperty]
    private string _customEpgValidationMessage = string.Empty;

    [ObservableProperty]
    private string _privacyStatusMessage = string.Empty;

    [ObservableProperty]
    private string _cacheStatusMessage = string.Empty;

    [ObservableProperty]
    private string _resetStatusMessage = string.Empty;

    [ObservableProperty]
    private int _refreshProgressPercent;
    

    public bool IsGlobalLoading => _mainViewModel.IsGlobalLoading;
    public string GlobalLoadingMessage => _mainViewModel.GlobalLoadingMessage;
    public bool CanRefreshEpgNow =>
        !IsGlobalLoading &&
        Volatile.Read(ref _isRefreshOperationRunning) == 0 &&
        _mainViewModel.EpgProgress is null;

    [ObservableProperty]
    private string _currentProfileName = string.Empty;
    
    [ObservableProperty]
    private string _currentProfileAvatar = string.Empty;

    [ObservableProperty]
    private string _providerName = string.Empty;
    
    [ObservableProperty]
    private string _providerUrl = string.Empty;

    [ObservableProperty]
    private string _providerUrlLabel = string.Empty;
    
    [ObservableProperty]
    private string _providerUsername = string.Empty;

    [ObservableProperty]
    private string _providerIdentityLabel = string.Empty;
    
    [ObservableProperty]
    private string _providerPassword = string.Empty;

    [ObservableProperty]
    private bool _showProviderIdentity;

    [ObservableProperty]
    private bool _showProviderPassword;

    [ObservableProperty]
    private bool _showProviderExpiration;
    
    [ObservableProperty]
    private DateTime? _expirationDate;
    
    [ObservableProperty]
    private string _expirationStatus = string.Empty;
    
    [ObservableProperty]
    private DateTime _profileCreatedAt;

    public SettingsViewModel(
        ISettingsService settingsService,
        IEpgService epgService, 
        IThemeService themeService,
        IDialogService dialogService,
        IWatchHistoryService watchHistoryService,
        MainViewModel mainViewModel,
        IPlaylistService playlistService,
        IDbContextFactory<AppDbContext> contextFactory,
        IDiagnosticReportService diagnosticService,
        ILicenseService licenseService,
        IAppVersionService appVersionService,
        ILocalizationService localizationService,
        ISecurityService securityService,
        IAppPathService? appPaths = null,
        ICacheService? cacheService = null,
        IProfileService? profileService = null,
        IDispatcherService? dispatcherService = null)
    {
        _settingsService = settingsService;
        _epgService = epgService;
        _themeService = themeService;
        _dialogService = dialogService;
        _watchHistoryService = watchHistoryService;
        _mainViewModel = mainViewModel;
        _playlistService = playlistService;
        _contextFactory = contextFactory;
        _diagnosticService = diagnosticService;
        _licenseService = licenseService;
        _appVersionService = appVersionService;
        _localizationService = localizationService;
        _securityService = securityService;
        _dispatcherService = dispatcherService;
        _appPaths = appPaths ?? new DesktopAppPathService();
        _cacheService = cacheService ?? new CacheService(_appPaths);
        _profileService = profileService;
        _autoSaveCoordinator = new SettingsAutoSaveCoordinator(PersistAutoSaveAsync);
        
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        _licenseService.SubscriptionChanged += OnLicenseSubscriptionChanged;
        
        ChannelListLastError = _mainViewModel.ChannelListLastError;
        
        LoadSettings();
        LoadProfileInfo();
        SyncEpgProgressFromMain();
        _initialStatisticsTask = LoadInitialStatisticsAsync();
        _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
        _ = UpdateCacheSizeAsync();
    }

    private async Task LoadInitialStatisticsAsync()
    {
        await ScanChannelListStatsCoreAsync(updateStatusMessage: false);

        if (_mainViewModel.EpgProgress is not null)
        {
            SyncEpgProgressFromMain();
            return;
        }

        await ScanEpgStatsCoreAsync(updateStatusMessage: false);
    }

    private bool EpgSelectionStillMatches(int? profileId)
        => !_lifetimeCts.IsCancellationRequested &&
           _mainViewModel.CurrentProfile?.Id == profileId;

    private bool ChannelSelectionStillMatches(int? profileId, int? playlistId)
        => !_lifetimeCts.IsCancellationRequested &&
           _mainViewModel.CurrentProfile?.Id == profileId &&
           _mainViewModel.SelectedPlaylist?.Id == playlistId;

    public string CurrentVersion => _appVersionService.DisplayVersion;
    public bool IsPremium => _licenseService.IsPremium;

    // ============ Cache ============

    [ObservableProperty]
    private string _cacheSizeString = "0 B";

    // ============ Promo Code ============

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPromoCodeCommand))]
    private string _promoCodeInput = string.Empty;

    /// <summary>
    /// Kod girişi yazıldıkça kanonik biçime dönüştürülür: büyük harf + her
    /// 4 karakterde bir '-' (bkz. PromoCodeFormatter); paste sonrası boşluk/
    /// ayraç karakterler otomatik temizlenir. Kullanıcı yeni kod yazmaya
    /// başladığında eski sonuç mesajı temizlenir (madde 15).
    /// </summary>
    partial void OnPromoCodeInputChanged(string value)
    {
        if (!IsApplyingPromoCode)
        {
            PromoCodeStatus = string.Empty;
            IsPromoCodeStatusSuccess = false;
        }

        var formatted = PromoCodeFormatter.Normalize(value);
        if (!string.Equals(formatted, value, StringComparison.Ordinal))
        {
            PromoCodeInput = formatted;
        }
    }

    [ObservableProperty]
    private string _promoCodeStatus = string.Empty;

    [ObservableProperty]
    private bool _isPromoCodeStatusSuccess;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPromoCodeCommand))]
    private bool _isApplyingPromoCode;

    public string PremiumStatusText
    {
        get
        {
            if (!_licenseService.IsPremium)
            {
                return string.Empty;
            }

            var expiresAt = _licenseService.PremiumExpiresAtUtc;
            if (expiresAt.HasValue)
            {
                // Yerel kültüre göre tarih (madde 21) + kalan süre bilgisi
                var expiryText = expiresAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                var remainingDays = Math.Max(0, (int)Math.Ceiling((expiresAt.Value - DateTime.UtcNow).TotalDays));
                var status = string.Format(_localizationService.GetString("GlobalSettings.Promo.Status.PremiumFormat"), expiryText);
                var remaining = string.Format(_localizationService.GetString("GlobalSettings.Promo.Status.RemainingDaysFormat"), remainingDays);
                return status + " · " + remaining;
            }

            return _localizationService.GetString("GlobalSettings.Promo.Status.Premium");
        }
    }

    /// <summary>
    /// Kalıcı Premium'da (edition-locked veya Play lifetime paket) promo kartı
    /// gizlenir — kod zaten anlamsız (lifetime her zaman kazanır, eklenen süre
    /// hiç kullanılmaz); süreli promo Premium aktifse kart kalır çünkü sistem
    /// süre eklemeyi destekler.
    /// </summary>
    public bool CanUsePromoCodes =>
        !_licenseService.IsEditionLockedPremium &&
        !_licenseService.HasLifetimePremium;

    /// <summary>
    /// Süreli (promo) Premium aktifken buton "Süre Ekle" der; aksi halde "Kodu Kullan".
    /// </summary>
    public string PromoApplyButtonText =>
        _licenseService.IsPremium && _licenseService.PromoPremiumExpiresAtUtc.HasValue
            ? _localizationService.GetString("GlobalSettings.Promo.ApplyExtend")
            : _localizationService.GetString("GlobalSettings.Promo.Apply");

    private bool CanApplyPromoCode => !IsApplyingPromoCode && PromoCodeFormatter.IsValid(PromoCodeInput);

    [RelayCommand(CanExecute = nameof(CanApplyPromoCode))]
    private async Task ApplyPromoCodeAsync()
    {
        if (IsApplyingPromoCode)
        {
            return;
        }

        IsApplyingPromoCode = true;
        try
        {
            var result = await _licenseService.ApplyPromoCodeAsync(PromoCodeInput);
            IsPromoCodeStatusSuccess = result.Success;
            PromoCodeStatus = result.Success ? FormatPromoCodeSuccess(result) : result.Message;
            if (result.Success)
            {
                PromoCodeInput = string.Empty;
                OnPropertyChanged(nameof(IsPremium));
                OnPropertyChanged(nameof(PremiumStatusText));
            }
        }
        catch (Exception ex)
        {
            // Kullanıcıya teknik/İngilizce runtime mesajı gösterilmez; gerçek
            // exception loglanır, yerelleştirilmiş güvenli mesaj gösterilir.
            System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] ApplyPromoCode failed: {ex}");
            PromoCodeStatus = _localizationService.GetString("GlobalSettings.Promo.Error.Generic");
            IsPromoCodeStatusSuccess = false;
        }
        finally
        {
            IsApplyingPromoCode = false;
        }
    }

    /// <summary>
    /// Başarılı redemption için tek sonuç kartı metni:
    /// "✓ N gün Premium eklendi" + "Yeni bitiş tarihi: T". Bitiş tarihi
    /// Premium kartında (PremiumStatusText) tekrar edilmez.
    /// </summary>
    private string FormatPromoCodeSuccess(PromoCodeRedemptionResult result)
    {
        var expiryText = result.PremiumExpiresAtUtc.HasValue
            ? result.PremiumExpiresAtUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : string.Empty;
        var addedTemplate = _localizationService.GetString("GlobalSettings.Promo.Success.DaysAddedFormat");
        var expiryTemplate = _localizationService.GetString("GlobalSettings.Promo.Success.NewExpiryFormat");
        return string.Format(addedTemplate, result.DurationDays) + Environment.NewLine + string.Format(expiryTemplate, expiryText);
    }

    /// <summary>
    /// Play'de onay bekleyen (PENDING) bir satın alma var mı? Pending satın
    /// alma Premium vermez; ayarlar ekranı bu bayrağı görüp "ödeme bekleniyor"
    /// bildirimi gösterir.
    /// </summary>
    public bool HasPendingStorePurchase => _licenseService.HasPendingStorePurchase;

    /// <summary>
    /// Kayıtlı Premium hakkı çözülemiyorsa true — ayarlar ekranı uyarı gösterir.
    /// </summary>
    public bool HasCorruptedPromoGrant => _licenseService.IsPromoGrantCorrupted;

    public string PromoGrantCorruptedMessage =>
        _localizationService.GetString("GlobalSettings.Promo.Error.GrantCorrupted");

    [RelayCommand]
    private async Task ShowUpsell()
    {
        await _dialogService.ShowUpsellAsync();
    }

    [RelayCommand]
    private void ReportBug()
    {
        _diagnosticService.OpenBugReport();
    }

    [RelayCommand]
    private async Task ShowPrivacyPolicyAsync()
    {
        await _dialogService.ShowLegalDocumentAsync(
            _localizationService.GetString("GlobalSettings.Privacy.Title"),
            _localizationService.GetString("GlobalSettings.Privacy.Message.Current"));
    }

    [RelayCommand]
    private async Task ShowTermsAsync()
    {
        await _dialogService.ShowLegalDocumentAsync(
            _localizationService.GetString("GlobalSettings.Terms.Title"),
            _localizationService.GetString("GlobalSettings.Terms.Message.Current"));
    }

    private void OnSettingsService_Changed()
    {
        if (_settingsChangeOriginGate.ShouldReload)
        {
            LoadSettings();
        }

        OnPropertyChanged(nameof(HasCorruptedPromoGrant));
        OnPropertyChanged(nameof(CanUsePromoCodes));
        OnPropertyChanged(nameof(PromoApplyButtonText));
    }

    public void EnableAutoSave()
    {
        if (_autoSaveEnabled)
        {
            return;
        }

        _autoSaveEnabled = true;
        AttachCustomEpgHandlers(CustomEpgUrls);

        // The shared downloader cannot transcode arbitrary provider streams. Mobile
        // therefore always downloads the provider's original stream instead of
        // exposing the desktop-era "Standard" option as if it changed quality.
        if (SelectedDownloadQuality != (int)DownloadQuality.High)
        {
            SelectedDownloadQuality = (int)DownloadQuality.High;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        QueueAutoSave(e.PropertyName);
    }

    private void QueueAutoSave(string? propertyName)
    {
        if (!_autoSaveEnabled ||
            _isLoadingSettings ||
            string.IsNullOrWhiteSpace(propertyName) ||
            !AutoSavePropertyNames.Contains(propertyName))
        {
            return;
        }

        lock (_autoSaveStatusSync)
        {
            _pendingAutoSaveAreas.Add(GetAutoSaveArea(propertyName));
        }

        var snapshot = CaptureSettingsSnapshot();
        var activeProfileId = _mainViewModel.CurrentProfile?.Id;
        if (activeProfileId.HasValue &&
            activeProfileId.Value > 0 &&
            _settingsService.Settings.ProfileId == activeProfileId.Value)
        {
            snapshot.ApplyTo(_settingsService.Settings);
        }

        var delay = DebouncedAutoSavePropertyNames.Contains(propertyName)
            ? TimeSpan.FromMilliseconds(450)
            : TimeSpan.FromMilliseconds(75);
        _autoSaveCoordinator.RequestSave(delay);
    }

    private async Task PersistAutoSaveAsync()
    {
        SettingsStatusArea[] affectedAreas;
        lock (_autoSaveStatusSync)
        {
            affectedAreas = _pendingAutoSaveAreas.ToArray();
            _pendingAutoSaveAreas.Clear();
        }

        try
        {
            var snapshot = CaptureSettingsSnapshot();
            var saved = false;
            await _settingsChangeOriginGate.RunOwnedSaveAsync(async () =>
            {
                saved = await SaveForActiveProfileAsync(
                    _settingsService,
                    _mainViewModel.CurrentProfile?.Id,
                    snapshot.ApplyTo);
            });

            if (saved)
            {
                CustomEpgSourcePolicy.MarkPersisted(CustomEpgUrls);
            }
        }
        catch (Exception ex)
        {
            // Re-queue pending areas so the next save attempt retries them.
            lock (_autoSaveStatusSync)
            {
                foreach (var area in affectedAreas)
                {
                    _pendingAutoSaveAreas.Add(area);
                }
            }

            var message = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("Common.ErrorFormat"),
                ex.Message);
            StatusMessage = message;
            foreach (var area in affectedAreas)
            {
                SetPanelStatus(area, message);
            }

            throw;
        }
    }

    private static SettingsStatusArea GetAutoSaveArea(string propertyName) => propertyName switch
    {
        nameof(IsDarkTheme) or nameof(AppLanguage) => SettingsStatusArea.Appearance,
        nameof(SubtitleEnabled) or nameof(SubtitleLanguage) or nameof(SubtitleFontSize) or
            nameof(PreferredAudioLanguage) => SettingsStatusArea.Audio,
        nameof(SelectedDownloadQuality) or nameof(DownloadWifiOnly) or nameof(DownloadPath) or
            nameof(ShowDownloadNotification) => SettingsStatusArea.Download,
        nameof(ChannelListRefreshFrequencyHours) => SettingsStatusArea.Channel,
        nameof(EpgRefreshFrequencyHours) or nameof(CustomEpgUrl) or nameof(EpgEnabled) or
            nameof(EpgTimeOffsetHours) => SettingsStatusArea.Epg,
        nameof(WatchHistoryRetentionIndex) or nameof(ClearHistoryOnExit) => SettingsStatusArea.Privacy,
        _ => SettingsStatusArea.Playback
    };

    private void SetPanelStatus(SettingsStatusArea area, string message)
    {
        switch (area)
        {
            case SettingsStatusArea.Appearance:
                AppearanceStatusMessage = message;
                break;
            case SettingsStatusArea.Playback:
                PlaybackStatusMessage = message;
                break;
            case SettingsStatusArea.Audio:
                AudioStatusMessage = message;
                break;
            case SettingsStatusArea.Download:
                DownloadStatusMessage = message;
                break;
            case SettingsStatusArea.Channel:
                ChannelStatusMessage = message;
                break;
            case SettingsStatusArea.Epg:
                EpgStatusMessage = message;
                break;
            case SettingsStatusArea.Privacy:
                PrivacyStatusMessage = message;
                break;
            case SettingsStatusArea.Cache:
                CacheStatusMessage = message;
                break;
            case SettingsStatusArea.Reset:
                ResetStatusMessage = message;
                break;
        }

        ScheduleStatusAutoClear(area);
    }

    /// <summary>
    /// Belirtilen durum alanı için otomatik temizleme zamanlayıcı.
    /// Yenileme çalışırken askıya alınır, bittikten sonra tekrar aktif olur.
    /// </summary>
    private void ScheduleStatusAutoClear(SettingsStatusArea area)
    {
        // Önceki zamanlayıcıyı iptal et
        if (_statusAutoClearTokens.TryGetValue(area, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _statusAutoClearTokens[area] = cts;

        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StatusAutoClearDelay, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // Yenileme çalışıyorsa temizleme yapma
            if (Volatile.Read(ref _isRefreshOperationRunning) == 1)
            {
                return;
            }

            // UI thread'e geçerek mesajı temizle
            if (_dispatcherService is not null)
            {
                _dispatcherService.BeginInvoke(() => ClearPanelStatus(area));
            }
            else
            {
                ClearPanelStatus(area);
            }
        }, token);
    }

    private void ClearPanelStatus(SettingsStatusArea area)
    {
        switch (area)
        {
            case SettingsStatusArea.Appearance:
                AppearanceStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Playback:
                PlaybackStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Audio:
                AudioStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Download:
                DownloadStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Channel:
                ChannelStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Epg:
                EpgStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Privacy:
                PrivacyStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Cache:
                CacheStatusMessage = string.Empty;
                break;
            case SettingsStatusArea.Reset:
                ResetStatusMessage = string.Empty;
                break;
        }
    }

    private void CancelAllStatusAutoClears()
    {
        foreach (var kvp in _statusAutoClearTokens)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _statusAutoClearTokens.Clear();
    }

    private void SetSharedAndPanelStatus(SettingsStatusArea area, string message)
    {
        StatusMessage = message;
        SetPanelStatus(area, message);
    }

    private void OnLicenseSubscriptionChanged()
    {
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(PremiumStatusText));
        OnPropertyChanged(nameof(HasPendingStorePurchase));
        OnPropertyChanged(nameof(HasCorruptedPromoGrant));
        OnPropertyChanged(nameof(CanUsePromoCodes));
        OnPropertyChanged(nameof(PromoApplyButtonText));
        AddCustomEpgCommand.NotifyCanExecuteChanged();
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // EpgProgress ve ChannelLoadingProgress gibi durumlar arka plan
        // iş parçacığından bildirilebilir; PropertyChanged zinciri Button gibi
        // UI nesnelerine (Örn: RefreshEpgNowCommand.NotifyCanExecuteChanged)
        // dokunduğundan işleme UI iş parçacığına taşınmalıdır.
        if (_dispatcherService is not null)
        {
            _dispatcherService.Invoke(() => MainViewModel_PropertyChangedCore(e));
            return;
        }

        MainViewModel_PropertyChangedCore(e);
    }

    private void MainViewModel_PropertyChangedCore(System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentProfile))
        {
            LoadProfileInfo();
            _ = ScanChannelListStatsCoreAsync(updateStatusMessage: false);
            _ = ScanEpgStatsCoreAsync(updateStatusMessage: false);
            _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedPlaylist))
        {
            _ = ScanChannelListStatsCoreAsync(updateStatusMessage: false);
        }
        else if (e.PropertyName == nameof(MainViewModel.ChannelListLastError))
        {
            ChannelListLastError = _mainViewModel.ChannelListLastError;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsGlobalLoading))
        {
            OnPropertyChanged(nameof(IsGlobalLoading));
            NotifyEpgRefreshAvailability();
            AddCustomEpgCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(MainViewModel.GlobalLoadingMessage))
        {
            OnPropertyChanged(nameof(GlobalLoadingMessage));
        }
        else if (e.PropertyName == nameof(MainViewModel.ChannelLoadingProgress) ||
                 e.PropertyName == nameof(MainViewModel.ChannelLoadingStats))
        {
            SyncChannelProgressFromMain();
        }
        else if (e.PropertyName == nameof(MainViewModel.EpgProgress))
        {
            SyncEpgProgressFromMain();
        }
    }

    private void LoadProfileInfo()
    {
        ResetProviderAccountInfo();

        if (_mainViewModel.CurrentProfile != null)
        {
            CurrentProfileName = _mainViewModel.CurrentProfile.Name;
            CurrentProfileAvatar = _mainViewModel.CurrentProfile.Avatar;
            ProfileCreatedAt = _mainViewModel.CurrentProfile.CreatedAt;
            
            // If CreatedAt is default (min value), set it to Now for display or handle it
            if (ProfileCreatedAt == DateTime.MinValue) ProfileCreatedAt = DateTime.UtcNow;

            if (_mainViewModel.CurrentProfile.ProviderAccount != null)
            {
                var account = _mainViewModel.CurrentProfile.ProviderAccount;
                var none = _localizationService.GetString("Common.None");
                ProviderName = account.Name;
                ProviderUrl = GetProviderDisplayUrl(account);
                ProviderUrlLabel = GetProviderUrlLabel(account.Type);
                ProviderIdentityLabel = GetProviderIdentityLabel(account.Type);
                ProviderUsername = string.IsNullOrWhiteSpace(account.Username) ? none : account.Username;

                ShowProviderIdentity = account.Type != ProfileType.M3U;
                ShowProviderPassword = account.Type == ProfileType.XtreamCodes;
                ShowProviderExpiration = account.Type is ProfileType.XtreamCodes or ProfileType.StalkerPortal;

                var decryptedPassword = _securityService.Decrypt(account.Password);
                ProviderPassword = string.IsNullOrWhiteSpace(decryptedPassword) ? none : decryptedPassword;
                
                ExpirationDate = account.ExpirationDate;
                
                if (ExpirationDate.HasValue)
                {
                    var daysLeft = (ExpirationDate.Value - DateTime.UtcNow).TotalDays;
                    if (daysLeft < 0) ExpirationStatus = _localizationService.GetString("Settings.Expiration.Expired");
                    else if (daysLeft < 7) ExpirationStatus = string.Format(_localizationService.GetString("Settings.Expiration.SoonFormat"), Math.Ceiling(daysLeft));
                    else ExpirationStatus = string.Format(_localizationService.GetString("Settings.Expiration.RemainingFormat"), Math.Ceiling(daysLeft));
                }
                else
                {
                    ExpirationStatus = _localizationService.GetString("Common.Unknown");
                }
            }
        }
    }

    private void ResetProviderAccountInfo()
    {
        var none = _localizationService.GetString("Common.None");
        ProviderName = string.Empty;
        ProviderUrl = none;
        ProviderUrlLabel = FormatAccountLabel(_localizationService.GetString("Settings.Account.Url"));
        ProviderUsername = none;
        ProviderIdentityLabel = FormatAccountLabel(_localizationService.GetString("Settings.Account.Username"));
        ProviderPassword = none;
        ShowProviderIdentity = false;
        ShowProviderPassword = false;
        ShowProviderExpiration = false;
        ExpirationDate = null;
        ExpirationStatus = _localizationService.GetString("Common.Unknown");
    }

    private string GetProviderUrlLabel(ProfileType type)
        => FormatAccountLabel(type switch
        {
            ProfileType.M3U => _localizationService.GetString("Profiles.Account.M3uLink"),
            _ => _localizationService.GetString("Profiles.Account.ServerUrl")
        });

    private string GetProviderIdentityLabel(ProfileType type)
        => FormatAccountLabel(type == ProfileType.StalkerPortal
            ? _localizationService.GetString("Profiles.Account.MacAddress")
            : _localizationService.GetString("Profiles.Account.Username"));

    private static string GetProviderDisplayUrl(ProviderAccount account)
        => account.Type == ProfileType.M3U
            ? account.Url
            : GetProviderBaseUrl(account.Url);

    private static string FormatAccountLabel(string value)
        => string.IsNullOrWhiteSpace(value) || value.TrimEnd().EndsWith(':')
            ? value
            : $"{value}:";

    private static string GetProviderBaseUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        var normalized = rawUrl.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }

        return rawUrl;
    }

    [ObservableProperty]
    private ObservableCollection<string> _hiddenLiveGroups = new();

    [ObservableProperty]
    private ObservableCollection<string> _hiddenMovieGroups = new();

    [ObservableProperty]
    private ObservableCollection<string> _hiddenSeriesGroups = new();

    public int TotalHiddenGroupsCount => HiddenLiveGroups.Count + HiddenMovieGroups.Count + HiddenSeriesGroups.Count;

    [RelayCommand]
    private async Task UnhideGroupAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        var s = _settingsService.Settings;
        bool removed = false;
        
        if (s.HiddenLiveGroups.Remove(groupName))
        {
            HiddenLiveGroups.Remove(groupName);
            removed = true;
        }
        if (s.HiddenMovieGroups.Remove(groupName))
        {
            HiddenMovieGroups.Remove(groupName);
            removed = true;
        }
        if (s.HiddenSeriesGroups.Remove(groupName))
        {
            HiddenSeriesGroups.Remove(groupName);
            removed = true;
        }

        if (removed)
        {
            await _settingsChangeOriginGate.RunOwnedSaveAsync(_settingsService.SaveAsyncBestEffort);
            _mainViewModel.ScheduleImmediateFilter();
        }
    }

    private void LoadSettings()
    {
        _isLoadingSettings = true;
        try
        {
            var s = _settingsService.Settings;
        
            // Playback
        UserAgent = s.UserAgent ?? string.Empty;
        AutoPlayNext = s.AutoPlayNext;
        
        IsBufferSmall = s.VideoBufferSize == BufferSize.Small;
        IsBufferNormal = s.VideoBufferSize == BufferSize.Normal;
        IsBufferLarge = s.VideoBufferSize == BufferSize.Large;

        SelectedDataUsage = (int)s.DataUsage;
        SubtitleEnabled = s.SubtitleEnabled;
        SubtitleLanguage = string.IsNullOrWhiteSpace(s.SubtitleLanguage) ? "en" : s.SubtitleLanguage;
        SubtitleFontSize = SubtitleAppearanceDefaults.ResolveDesktopFontSize(s.SubtitleTextSize);
        PreferredAudioLanguage = string.IsNullOrWhiteSpace(s.PreferredAudioLanguage) ? "en" : s.PreferredAudioLanguage;
        
        // Downloads
        SelectedDownloadQuality = (int)s.DownloadQuality;
        DownloadWifiOnly = s.DownloadWifiOnly;
        DownloadPath = NormalizeDownloadPath(s.DownloadPath);
        ShowDownloadNotification = s.ShowDownloadNotification;

        // Privacy
        SaveWatchHistory = s.SaveWatchHistory;
        ClearHistoryOnExit = s.ClearHistoryOnExit;
        WatchHistoryRetentionIndex = s.WatchHistoryRetentionDays switch
        {
            3 => 1,
            7 => 2,
            14 => 3,
            30 => 4,
            _ => 0
        };
        
        // Appearance
        IsDarkTheme = s.IsDarkTheme;
        AppLanguage = string.IsNullOrWhiteSpace(s.Language) ? "en" : s.Language;
        ChannelListRefreshFrequencyHours = s.ChannelListRefreshFrequencyHours;
        EpgRefreshFrequencyHours = s.EpgRefreshFrequencyHours;
        EpgEnabled = s.EpgEnabled;

        ChannelListRefreshFrequencyIndex = ChannelListRefreshFrequencyHours switch
        {
            1 => 1,
            3 => 2,
            12 => 3,
            24 => 4,
            48 => 5,
            72 => 6,
            168 => 7,
            _ => 0
        };

        EpgRefreshFrequencyIndex = EpgRefreshFrequencyHours switch
        {
            1 => 1,
            3 => 2,
            12 => 3,
            24 => 4,
            48 => 5,
            72 => 6,
            168 => 7,
            _ => 0
        };

        EpgTimeOffsetHours = s.EpgTimeOffsetHours;
        EpgTimeOffsetIndex = EpgTimeOffsetHours + 12;

        CustomEpgUrl = s.CustomEpgUrl ?? string.Empty;

        // Custom EPG URLs List (with Migration)
        var urls = s.CustomEpgUrls ?? new List<string>();
        if (urls.Count == 0 && !string.IsNullOrWhiteSpace(s.CustomEpgUrl))
        {
            urls = new List<string> { s.CustomEpgUrl };
        }
        DetachCustomEpgHandlers(CustomEpgUrls);
        CustomEpgUrls = new ObservableCollection<EpgUrlItem>(urls.Select(u => new EpgUrlItem
        {
            Url = u,
            PersistedUrl = u
        }));
        if (_autoSaveEnabled)
        {
            AttachCustomEpgHandlers(CustomEpgUrls);
        }
        UpdateCustomEpgEditorState();

            // Hidden Groups
            HiddenLiveGroups = new ObservableCollection<string>(s.HiddenLiveGroups);
            HiddenMovieGroups = new ObservableCollection<string>(s.HiddenMovieGroups);
            HiddenSeriesGroups = new ObservableCollection<string>(s.HiddenSeriesGroups);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var snapshot = CaptureSettingsSnapshot();

        var saved = false;
        await _settingsChangeOriginGate.RunOwnedSaveAsync(async () =>
        {
            saved = await SaveForActiveProfileAsync(
                _settingsService,
                _mainViewModel.CurrentProfile?.Id,
                snapshot.ApplyTo);
        });

        if (!saved)
        {
            return;
        }

        CustomEpgSourcePolicy.MarkPersisted(CustomEpgUrls);
        _themeService.SetTheme(snapshot.IsDarkTheme);
        if (!_autoSaveEnabled)
        {
            StatusMessage = _localizationService.GetString("Settings.Status.Saved");
        }
    }

    private SettingsFormSnapshot CaptureSettingsSnapshot()
    {
        var normalizedDownloadPath = NormalizeDownloadPath(DownloadPath);
        return new SettingsFormSnapshot(
            UserAgent?.Trim() ?? string.Empty,
            AutoPlayNext,
            IsBufferSmall && IsPremium
                ? BufferSize.Small
                : IsBufferLarge && IsPremium
                    ? BufferSize.Large
                    : BufferSize.Normal,
            (DataUsageLevel)SelectedDataUsage,
            SubtitleEnabled,
            SubtitleLanguage,
            SubtitleFontSize,
            PreferredAudioLanguage,
            (DownloadQuality)SelectedDownloadQuality,
            DownloadWifiOnly,
            normalizedDownloadPath,
            ShowDownloadNotification,
            IsDarkTheme,
            string.IsNullOrWhiteSpace(AppLanguage) ? "en" : AppLanguage,
            Math.Max(0, ChannelListRefreshFrequencyHours),
            Math.Max(0, EpgRefreshFrequencyHours),
            string.IsNullOrWhiteSpace(CustomEpgUrl) ? null : CustomEpgUrl.Trim(),
            CustomEpgSourcePolicy.BuildPersistedSources(CustomEpgUrls),
            EpgEnabled,
            EpgTimeOffsetHours,
            SaveWatchHistory,
            ClearHistoryOnExit,
            WatchHistoryRetentionIndex switch
            {
                1 => 3,
                2 => 7,
                3 => 14,
                4 => 30,
                _ => 0
            });
    }

    internal static async Task<bool> SaveForActiveProfileAsync(
        ISettingsService settingsService,
        int? activeProfileId,
        Action<AppSettings> applyChanges)
    {
        if (!activeProfileId.HasValue || activeProfileId.Value <= 0)
        {
            return false;
        }

        if (settingsService.Settings.ProfileId != activeProfileId.Value)
        {
            await settingsService.LoadProfileSettingsAsync(activeProfileId.Value);
        }

        applyChanges(settingsService.Settings);
        try
        {
            await settingsService.SaveAsync();
        }
        catch (SettingsPersistenceException)
        {
            return false;
        }
        return true;
    }

    private sealed record SettingsFormSnapshot(
        string UserAgent,
        bool AutoPlayNext,
        BufferSize VideoBufferSize,
        DataUsageLevel DataUsage,
        bool SubtitleEnabled,
        string SubtitleLanguage,
        int SubtitleFontSize,
        string PreferredAudioLanguage,
        DownloadQuality DownloadQuality,
        bool DownloadWifiOnly,
        string DownloadPath,
        bool ShowDownloadNotification,
        bool IsDarkTheme,
        string Language,
        int ChannelListRefreshFrequencyHours,
        int EpgRefreshFrequencyHours,
        string? CustomEpgUrl,
        List<string> CustomEpgUrls,
        bool EpgEnabled,
        int EpgTimeOffsetHours,
        bool SaveWatchHistory,
        bool ClearHistoryOnExit,
        int WatchHistoryRetentionDays)
    {
        public void ApplyTo(AppSettings settings)
        {
            settings.UserAgent = UserAgent;
            settings.AutoPlayNext = AutoPlayNext;
            settings.VideoBufferSize = VideoBufferSize;
            settings.DataUsage = DataUsage;
            settings.SubtitleEnabled = SubtitleEnabled;
            settings.SubtitleLanguage = SubtitleLanguage;
            settings.SubtitleTextSize = SubtitleAppearanceDefaults.ResolveTextSize(SubtitleFontSize);
            settings.PreferredAudioLanguage = PreferredAudioLanguage;
            settings.DownloadQuality = DownloadQuality;
            settings.DownloadWifiOnly = DownloadWifiOnly;
            settings.DownloadPath = DownloadPath;
            settings.ShowDownloadNotification = ShowDownloadNotification;
            settings.IsDarkTheme = IsDarkTheme;
            settings.Language = Language;
            settings.ChannelListRefreshFrequencyHours = ChannelListRefreshFrequencyHours;
            settings.EpgRefreshFrequencyHours = EpgRefreshFrequencyHours;
            settings.CustomEpgUrl = CustomEpgUrl;
            settings.CustomEpgUrls = CustomEpgUrls;
            settings.EpgEnabled = EpgEnabled;
            settings.EpgTimeOffsetHours = EpgTimeOffsetHours;
            settings.SaveWatchHistory = SaveWatchHistory;
            settings.ClearHistoryOnExit = ClearHistoryOnExit;
            settings.WatchHistoryRetentionDays = WatchHistoryRetentionDays;
        }
    }

    [ObservableProperty]
    private int _totalChannels;

    [ObservableProperty]
    private int _totalEpgPrograms;

    [ObservableProperty]
    private int _totalEpgChannels;

    [ObservableProperty]
    private DateTime? _channelListLastUpdated;

    [ObservableProperty]
    private string? _channelListLastError;

    [ObservableProperty]
    private DateTime? _lastEpgUpdate;

    [ObservableProperty]
    private string? _epgLastError;

    [RelayCommand(CanExecute = nameof(CanAddCustomEpg))]
    private void AddCustomEpg()
    {
        if (CustomEpgUrls.Count >= AppSettings.EPG_URL_LIMIT)
        {
            CustomEpgValidationMessage = string.Format(_localizationService.GetString("Settings.Error.EpgLimitFormat"), AppSettings.EPG_URL_LIMIT);
            return;
        }

        if (!IsPremium && CustomEpgUrls.Count >= AppSettings.EPG_URL_FREE_LIMIT)
        {
            CustomEpgValidationMessage = string.Format(_localizationService.GetString("Settings.Error.EpgFreeLimitFormat"), AppSettings.EPG_URL_FREE_LIMIT);
            return;
        }

        if (CustomEpgUrls.Any(item => !CustomEpgSourcePolicy.IsValid(item.Url)))
        {
            CustomEpgValidationMessage = _localizationService.GetString("Settings.Error.EpgInvalidUrl");
            return;
        }

        var item = new EpgUrlItem();
        CustomEpgUrls.Add(item);
        item.PropertyChanged += CustomEpgItem_PropertyChanged;
        UpdateCustomEpgEditorState();
    }

    private bool CanAddCustomEpg() =>
        !IsGlobalLoading &&
        CustomEpgSourcePolicy.CanAdd(CustomEpgUrls.Select(item => item.Url), IsPremium);

    [RelayCommand]
    private void RemoveCustomEpg(EpgUrlItem item)
    {
        item.PropertyChanged -= CustomEpgItem_PropertyChanged;
        if (CustomEpgUrls.Remove(item))
        {
            UpdateCustomEpgEditorState();
            if (!string.IsNullOrWhiteSpace(item.PersistedUrl))
            {
                QueueAutoSave(nameof(CustomEpgUrl));
            }
        }
    }

    private void AttachCustomEpgHandlers(IEnumerable<EpgUrlItem> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= CustomEpgItem_PropertyChanged;
            item.PropertyChanged += CustomEpgItem_PropertyChanged;
        }
    }

    private void DetachCustomEpgHandlers(IEnumerable<EpgUrlItem> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= CustomEpgItem_PropertyChanged;
        }
    }

    private void CustomEpgItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EpgUrlItem.Url))
        {
            UpdateCustomEpgEditorState();
            if (CustomEpgUrls.All(item => CustomEpgSourcePolicy.IsValid(item.Url)))
            {
                QueueAutoSave(nameof(CustomEpgUrl));
            }
        }
    }

    private void UpdateCustomEpgEditorState()
    {
        var hasInvalidSource = CustomEpgUrls.Any(item => !CustomEpgSourcePolicy.IsValid(item.Url));
        CustomEpgValidationMessage = hasInvalidSource
            ? _localizationService.GetString("Settings.Error.EpgInvalidUrl")
            : string.Empty;
        AddCustomEpgCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ScanEpgStatsAsync()
        => await ScanEpgStatsCoreAsync(updateStatusMessage: true);

    private async Task ScanEpgStatsCoreAsync(bool updateStatusMessage)
    {
        var cancellationToken = _lifetimeCts.Token;
        var gateEntered = false;
        try
        {
            await _statisticsScanGate.WaitAsync(cancellationToken);
            gateEntered = true;

            if (updateStatusMessage)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Epg,
                    _localizationService.GetString("Settings.Status.EpgReading"));
            }

            var profileId = _mainViewModel.CurrentProfile?.Id;
            var statistics = await Task.Run(
                async () =>
                {
                    await using var db = await _contextFactory
                        .CreateDbContextAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var totalPrograms = 0;
                    var totalChannels = 0;
                    DateTime? lastUpdated = null;

                    if (profileId.HasValue)
                    {
                        var activeChannels = await db.Channels
                            .AsNoTracking()
                            .Where(c =>
                                c.Playlist != null &&
                                c.Playlist.ProfileId == profileId.Value &&
                                c.Playlist.IsActive)
                            .Select(c => new { c.Id, c.TvgId })
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var searchIds = activeChannels
                            .Select(c => c.TvgId)
                            .Where(id => !string.IsNullOrEmpty(id))
                            .Concat(activeChannels.Select(c => c.Id.ToString()))
                            .Distinct()
                            .ToList();

                        totalPrograms = await db.EpgPrograms
                            .CountAsync(
                                p => searchIds.Contains(p.ChannelId),
                                cancellationToken)
                            .ConfigureAwait(false);

                        totalChannels = await db.EpgPrograms
                            .Where(p => searchIds.Contains(p.ChannelId))
                            .Select(p => p.ChannelId)
                            .Distinct()
                            .CountAsync(cancellationToken)
                            .ConfigureAwait(false);

                        lastUpdated = await db.Playlists
                            .AsNoTracking()
                            .Where(p =>
                                p.ProfileId == profileId.Value &&
                                p.IsActive &&
                                p.EpgLastUpdated != null)
                            .OrderByDescending(p => p.EpgLastUpdated)
                            .Select(p => p.EpgLastUpdated)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var errorQuery = db.Playlists
                        .AsNoTracking()
                        .Where(p =>
                            p.IsActive &&
                            !string.IsNullOrWhiteSpace(p.EpgLastError));
                    if (profileId.HasValue)
                    {
                        errorQuery = errorQuery.Where(
                            p => p.ProfileId == profileId.Value);
                    }

                    var lastError = await errorQuery
                        .OrderByDescending(
                            p => p.EpgLastUpdated ?? p.LastUpdated ?? p.CreatedAt)
                        .Select(p => p.EpgLastError)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return new EpgStatistics(
                        totalPrograms,
                        totalChannels,
                        lastUpdated,
                        lastError);
                },
                cancellationToken);

            if (!EpgSelectionStillMatches(profileId))
            {
                return;
            }

            TotalEpgPrograms = statistics.TotalPrograms;
            TotalEpgChannels = statistics.TotalChannels;
            LastEpgUpdate = statistics.LastUpdated;
            EpgLastError = statistics.LastError;

            if (string.IsNullOrWhiteSpace(EpgLastError))
            {
                EpgLastError = _epgService.LastError;
            }

            if (!string.IsNullOrWhiteSpace(EpgLastError))
            {
                EpgLastError = UserFriendlyErrorMessage.FromText(EpgLastError);
            }
            
            if (updateStatusMessage && !string.IsNullOrEmpty(EpgLastError))
            {
                var isWarning = EpgLastError.Contains("eşleşen yayın bilgisi bulunamadı") || EpgLastError.Contains("0 program");
                if (isWarning)
                {
                    SetSharedAndPanelStatus(
                        SettingsStatusArea.Epg,
                        _localizationService.GetString("Settings.Status.EpgUpdated"));
                }
                else
                {
                    SetSharedAndPanelStatus(
                        SettingsStatusArea.Epg,
                        _localizationService.GetString("Settings.Status.EpgError"));
                }
            }
            else if (updateStatusMessage)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Epg,
                    _localizationService.GetString("Settings.Status.EpgUpdated"));
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // The Settings scope was closed while its initial statistics were loading.
        }
        catch (Exception ex)
        {
            if (updateStatusMessage)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Epg,
                    string.Format(_localizationService.GetString("Settings.Status.Stats.ErrorFormat"), UserFriendlyErrorMessage.FromException(ex)));
            }
        }
        finally
        {
            if (gateEntered)
            {
                _statisticsScanGate.Release();
            }
        }
    }

    [RelayCommand]
    private async Task ScanChannelListStatsAsync()
        => await ScanChannelListStatsCoreAsync(updateStatusMessage: true);

    private async Task ScanChannelListStatsCoreAsync(bool updateStatusMessage)
    {
        var cancellationToken = _lifetimeCts.Token;
        var gateEntered = false;
        try
        {
            await _statisticsScanGate.WaitAsync(cancellationToken);
            gateEntered = true;

            if (updateStatusMessage)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Channel,
                    _localizationService.GetString("Settings.Status.ChannelsReading"));
            }

            var playlistId = _mainViewModel.SelectedPlaylist?.Id;
            var profileId = _mainViewModel.CurrentProfile?.Id;
            var statistics = await Task.Run(
                async () =>
                {
                    await using var db = await _contextFactory
                        .CreateDbContextAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (playlistId.HasValue)
                    {
                        var lastUpdated = await db.Playlists
                            .AsNoTracking()
                            .Where(p => p.Id == playlistId.Value)
                            .Select(p => p.LastUpdated)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var totalChannels = await db.Channels
                            .CountAsync(
                                c => c.PlaylistId == playlistId.Value,
                                cancellationToken)
                            .ConfigureAwait(false);

                        return new ChannelListStatistics(
                            lastUpdated,
                            totalChannels);
                    }

                    if (profileId.HasValue)
                    {
                        var lastUpdated = await db.Playlists
                            .AsNoTracking()
                            .Where(p =>
                                p.IsActive &&
                                p.ProfileId == profileId.Value)
                            .OrderByDescending(p => p.LastUpdated)
                            .Select(p => p.LastUpdated)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var totalChannels = await db.Channels
                            .CountAsync(
                                c =>
                                    c.Playlist != null &&
                                    c.Playlist.ProfileId == profileId.Value &&
                                    c.Playlist.IsActive,
                                cancellationToken)
                            .ConfigureAwait(false);

                        return new ChannelListStatistics(
                            lastUpdated,
                            totalChannels);
                    }

                    return new ChannelListStatistics(null, 0);
                },
                cancellationToken);

            if (!ChannelSelectionStillMatches(profileId, playlistId))
            {
                return;
            }

            ChannelListLastUpdated = statistics.LastUpdated;
            TotalChannels = statistics.TotalChannels;

            if (updateStatusMessage)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Channel,
                    _localizationService.GetString("Settings.Status.ChannelsUpdated"));
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // The Settings scope was closed while its initial statistics were loading.
        }
        catch
        {
            ChannelListLastUpdated = null;
            if (updateStatusMessage)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Channel,
                    _localizationService.GetString("Settings.Status.Channel.Error"));
            }
        }
        finally
        {
            if (gateEntered)
            {
                _statisticsScanGate.Release();
            }
        }
    }

    [RelayCommand]
    private async Task RefreshChannelListNowAsync()
    {
        if (!TryBeginRefreshOperation(_localizationService.GetString("Settings.Refresh.Channel.Started"), "Channel"))
        {
            return;
        }

        try
        {
            _mainViewModel.IsGlobalLoading = true;
            _mainViewModel.GlobalLoadingMessage = _localizationService.GetString("Settings.Refresh.Channel.Started");

            SetProgressStatus("Channel", 0, _localizationService.GetString("Settings.Refresh.Channel.Started"));
            await _mainViewModel.RefreshSelectedPlaylistAsync(force: true);
            SyncChannelProgressFromMain(minimumPercent: 88, fallbackMessage: _localizationService.GetString("Settings.Refresh.Channel.UpdatingData"));

            SetProgressStatus("Channel", Math.Max(RefreshProgressPercent, 90), _localizationService.GetString("Settings.Refresh.Channel.UpdatingData"));
            _mainViewModel.GlobalLoadingMessage = _localizationService.GetString("Settings.Refresh.Channel.UpdatingData");
            await ScanChannelListStatsCoreAsync(updateStatusMessage: false);

            // Kanal listesi yenilenirken bitiş süresini de güncelle
            SetProgressStatus("Channel", Math.Max(RefreshProgressPercent, 95), _localizationService.GetString("Settings.Refresh.Channel.CheckingAccount"));
            _mainViewModel.GlobalLoadingMessage = _localizationService.GetString("Settings.Refresh.Channel.CheckingAccount");
            await _mainViewModel.RefreshCurrentProfileExpirationAsync();
            LoadProfileInfo();

            if (!string.IsNullOrWhiteSpace(ChannelListLastError))
            {
                SetProgressStatus("Channel", 100, ChannelListLastError);
            }
            else
            {
                // Ekstra kontrol: Eğer error yok ama kanal sayısı hala 0 ise uyar
                var afterCount = await _playlistService.GetChannelCountAsync(_mainViewModel.SelectedPlaylist?.Id ?? 0);
                if (afterCount == 0)
                {
                    SetProgressStatus("Channel", 100, _localizationService.GetString("Settings.Refresh.Channel.Warning.NoContent"));
                }
                else
                {
                    SetProgressStatus("Channel", 100, _localizationService.GetString("Settings.Refresh.Channel.Completed"));
                }
            }
        }
        catch (Exception ex)
        {
            ChannelListLastError = UserFriendlyErrorMessage.FromException(ex);
            SetProgressStatus("Channel", RefreshProgressPercent, UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Settings.Refresh.Channel.Error"), ex));
        }
        finally
        {
            _mainViewModel.IsGlobalLoading = false;
            _mainViewModel.GlobalLoadingMessage = string.Empty;
            EndRefreshOperation();
        }
    }

    [RelayCommand]
    private void CancelRefreshOperation()
    {
        if (Volatile.Read(ref _isRefreshOperationRunning) != 1)
        {
            return;
        }

        _epgRefreshWatchCts?.Cancel();
        if (string.Equals(_activeRefreshScope, "Channel", StringComparison.OrdinalIgnoreCase))
        {
            _mainViewModel.CancelProfileBackgroundLoading();
            _mainViewModel.IsChannelLoading = false;
            _mainViewModel.ChannelLoadingStats = string.Empty;
        }

        var message = _localizationService.GetString("Dialog.Cancel");
        var cancelledArea = string.Equals(_activeRefreshScope, "EPG", StringComparison.OrdinalIgnoreCase)
            ? SettingsStatusArea.Epg
            : SettingsStatusArea.Channel;
        SetSharedAndPanelStatus(cancelledArea, message);
        _mainViewModel.StatusMessage = message;
        _mainViewModel.IsGlobalLoading = false;
        _mainViewModel.GlobalLoadingMessage = string.Empty;
        EndRefreshOperation();
    }

    private bool CanStartEpgRefresh() => CanRefreshEpgNow;

    [RelayCommand(CanExecute = nameof(CanStartEpgRefresh))]
    private async Task RefreshEpgNowAsync()
    {
        // Önce ayarları kaydet ki arka plan görevi yeni URL'yi görebilsin
        await SaveSettingsAsync();

        if (!TryBeginRefreshOperation(_localizationService.GetString("Settings.Refresh.Epg.Started"), "EPG"))
        {
            return;
        }

        SetProgressStatus("EPG", 8, _localizationService.GetString("Settings.Refresh.Epg.Started"));
        var started = _mainViewModel.ForceRefreshEpgInBackground();
        if (!started)
        {
            SetProgressStatus("EPG", 8, _localizationService.GetString("Settings.Refresh.Epg.InProgress"));
            EndRefreshOperation();
            return;
        }

        EpgLastError = null;
        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var pId = _mainViewModel.CurrentProfile?.Id;
            var activePlaylists = await db.Playlists.Where(p => p.IsActive && p.ProfileId == pId).ToListAsync();
            foreach (var p in activePlaylists)
            {
                p.EpgLastError = null;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] Failed to clear EPG errors before refresh: {ex.Message}");
        }

        _epgRefreshWatchCts?.Cancel();
        _epgRefreshWatchCts?.Dispose();
        _epgRefreshWatchCts = new CancellationTokenSource();

        _ = WatchEpgRefreshOutcomeAsync(_epgRefreshWatchCts.Token);
    }

    private async Task WatchEpgRefreshOutcomeAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow.AddSeconds(-2);
        var timeoutAt = DateTime.UtcNow.AddMinutes(10); // Match EpgService timeout

        try
        {
            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < timeoutAt)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                    
                    // MainViewModel'daki progres verisini yakala
                    var currentProgress = _mainViewModel.EpgProgress;
                    if (currentProgress != null)
                    {
                        var percent = (int)currentProgress.ProgressPercent;
                        SetProgressStatus("EPG", percent, currentProgress.Message);
                    }
                    else 
                    {
                        // Progress nesnesi yoksa istatistik taramaya devam et (fall-back)
                        await ScanEpgStatsCoreAsync(updateStatusMessage: false);
                        
                        if (LastEpgUpdate.HasValue && LastEpgUpdate.Value >= startedAt)
                        {
                            SetProgressStatus("EPG", 100, _localizationService.GetString("Settings.Refresh.Epg.Completed"));
                            return;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(EpgLastError))
                    {
                        var isWarning = EpgLastError.Contains("eşleşen yayın bilgisi bulunamadı") || EpgLastError.Contains("0 program");
                        
                        if (isWarning)
                        {
                            SetProgressStatus("EPG", 100, _localizationService.GetString("Settings.Refresh.Epg.Completed"));
                        }
                        else
                        {
                            SetProgressStatus(
                                "EPG",
                                100,
                                string.Format(_localizationService.GetString("Settings.Refresh.Epg.ErrorFormat"), EpgLastError));
                        }
                        return;
                    }
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    SetProgressStatus("EPG", RefreshProgressPercent, UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Settings.Refresh.Epg.WatchingError"), ex));
                    return;
                }
            }

            if (DateTime.UtcNow >= timeoutAt)
            {
                SetProgressStatus("EPG", RefreshProgressPercent, _localizationService.GetString("Settings.Refresh.Epg.Timeout"));
            }
        }
        finally
        {
            EndRefreshOperation();
        }
    }

    private bool TryBeginRefreshOperation(string operationLabel, string scope)
    {
        if (Interlocked.Exchange(ref _isRefreshOperationRunning, 1) == 1)
        {
            var busyArea = scope.Equals("EPG", StringComparison.OrdinalIgnoreCase)
                ? SettingsStatusArea.Epg
                : SettingsStatusArea.Channel;
            SetSharedAndPanelStatus(
                busyArea,
                _localizationService.GetString("Settings.Refresh.OperationInProgress"));
            return false;
        }

        _activeRefreshScope = scope;
        NotifyEpgRefreshAvailability();
        RefreshProgressPercent = 0;
        ChannelListLastError = null;
        var area = scope.Equals("EPG", StringComparison.OrdinalIgnoreCase)
            ? SettingsStatusArea.Epg
            : SettingsStatusArea.Channel;
        SetSharedAndPanelStatus(
            area,
            string.Format(_localizationService.GetString("Settings.Status.Refresh.Label"), operationLabel));
        return true;
    }

    private void EndRefreshOperation()
    {
        _activeRefreshScope = null;
        Interlocked.Exchange(ref _isRefreshOperationRunning, 0);
        NotifyEpgRefreshAvailability();

        // Refresh sırasında süresi dolan auto-clear'lar atlandı. Şimdi yeniden
        // planlayarak mesajların ekranda yapışmasını önle.
        RescheduleExpiredStatusMessages();
    }

    private void RescheduleExpiredStatusMessages()
    {
        // Mevcut (süresi dolmuş/iptal edilmiş) token'ları temizle
        foreach (var kvp in _statusAutoClearTokens)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _statusAutoClearTokens.Clear();

        // Dolu olan HER panel durumu için yeni bir auto-clear zamanla.
        // Yalnızca Channel/EPG değil, Appearance, Playback, Audio vs. de dahil.
        foreach (var area in Enum.GetValues<SettingsStatusArea>())
        {
            if (!string.IsNullOrEmpty(GetPanelStatus(area)))
            {
                ScheduleStatusAutoClear(area);
            }
        }
    }

    private string GetPanelStatus(SettingsStatusArea area) => area switch
    {
        SettingsStatusArea.Appearance => AppearanceStatusMessage,
        SettingsStatusArea.Playback => PlaybackStatusMessage,
        SettingsStatusArea.Audio => AudioStatusMessage,
        SettingsStatusArea.Download => DownloadStatusMessage,
        SettingsStatusArea.Channel => ChannelStatusMessage,
        SettingsStatusArea.Epg => EpgStatusMessage,
        SettingsStatusArea.Privacy => PrivacyStatusMessage,
        SettingsStatusArea.Cache => CacheStatusMessage,
        SettingsStatusArea.Reset => ResetStatusMessage,
        _ => string.Empty
    };

    private void SyncChannelProgressFromMain(int minimumPercent = 0, string? fallbackMessage = null)
    {
        if (Volatile.Read(ref _isRefreshOperationRunning) != 1 ||
            !string.Equals(_activeRefreshScope, "Channel", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var percent = Math.Max(minimumPercent, (int)Math.Round(_mainViewModel.ChannelLoadingProgress));
        var message = !string.IsNullOrWhiteSpace(_mainViewModel.ChannelLoadingStats)
            ? _mainViewModel.ChannelLoadingStats
            : !string.IsNullOrWhiteSpace(_mainViewModel.StatusMessage)
                ? _mainViewModel.StatusMessage
                : fallbackMessage ?? _localizationService.GetString("Settings.Refresh.Channel.Started");

        SetProgressStatus("Channel", percent, message, updateMainStatus: false);
    }

    private void SyncEpgProgressFromMain()
    {
        var progress = _mainViewModel.EpgProgress;
        if (progress is not null)
        {
            _wasEpgRefreshActive = true;
            _activeRefreshScope = "EPG";
            Interlocked.Exchange(ref _isRefreshOperationRunning, 1);
            NotifyEpgRefreshAvailability();

            SetProgressStatus(
                "EPG",
                (int)Math.Round(progress.ProgressPercent),
                progress.Message,
                updateMainStatus: false);
            return;
        }

        if (!_wasEpgRefreshActive)
        {
            return;
        }

        _wasEpgRefreshActive = false;
        EndRefreshOperation();
        _ = ScanEpgStatsCoreAsync(updateStatusMessage: false);
    }

    private void NotifyEpgRefreshAvailability()
    {
        OnPropertyChanged(nameof(CanRefreshEpgNow));
        RefreshEpgNowCommand.NotifyCanExecuteChanged();
    }

    private void SetProgressStatus(string scope, int percent, string message, bool updateMainStatus = true)
    {
        var normalized = Math.Clamp(percent, 0, 100);
        var localizedScope = scope.Equals("Channel", StringComparison.OrdinalIgnoreCase)
            ? _localizationService.GetString("Settings.Refresh.Scope.Channel")
            : scope.Equals("EPG", StringComparison.OrdinalIgnoreCase)
                ? _localizationService.GetString("Settings.Refresh.Scope.Epg")
                : scope;

        RefreshProgressPercent = normalized;
        StatusMessage = string.Format(
            CultureInfo.CurrentCulture,
            _localizationService.GetString("Settings.Status.ProgressFormat"),
            localizedScope,
            message,
            normalized);
        SetPanelStatus(
            scope.Equals("EPG", StringComparison.OrdinalIgnoreCase)
                ? SettingsStatusArea.Epg
                : SettingsStatusArea.Channel,
            StatusMessage);
        
        // Settings penceresi kapatılsa bile ana pencerenin sol altındaki bar güncellenmeye devam etsin
        if (updateMainStatus &&
            (scope.Equals("EPG", StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("Channel", StringComparison.OrdinalIgnoreCase)))
        {
            _mainViewModel.StatusMessage = StatusMessage;
        }
    }

    private string NormalizeDownloadPath(string? rawPath)
    {
        return _appPaths.NormalizeDownloadDirectory(rawPath);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        _ = SaveSettingsAsync();
    }
    
    [RelayCommand]
    private void ResetToDefaults()
    {
        _settingsService.ResetToDefaults();
        LoadSettings();
        SetSharedAndPanelStatus(
            SettingsStatusArea.Reset,
            _localizationService.GetString("Settings.Status.Reset"));
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var profileId = _mainViewModel.CurrentProfile?.Id;
        if (!profileId.HasValue) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("Settings.Privacy.Clear.Title"),
            _localizationService.GetString("Settings.Privacy.Clear.Confirm"));

        if (confirmed)
        {
            try
            {
                await _watchHistoryService.DeleteProfileHistoryAsync(profileId.Value);
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Privacy,
                    _localizationService.GetString("Settings.Privacy.Clear.Success"));
                _mainViewModel.ResetWatchHistoryUI();
            }
            catch (Exception ex)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Privacy,
                    string.Format(_localizationService.GetString("Common.ErrorFormat"), ex.Message));
            }
        }
    }

    private async Task UpdateCacheSizeAsync()
    {
        CacheSizeString = await _cacheService.GetCacheSizeStringAsync();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("GlobalSettings.Cache.Clear.ConfirmTitle"),
            _localizationService.GetString("GlobalSettings.Cache.Clear.ConfirmMessage"));

        if (confirmed)
        {
            try
            {
                await _settingsChangeOriginGate.RunOwnedSaveAsync(_settingsService.SaveAsync);
                await _cacheService.ClearCacheAsync();

                if (_profileService != null)
                {
                    var profiles = await _profileService.GetProfilesAsync();
                    var activeIds = new HashSet<int>(profiles.Select(p => p.Id));
                    activeIds.Add(0);
                    activeIds.Add(_settingsService.Settings.ProfileId);
                    await _settingsService.CleanOrphanedSettingsAsync(activeIds);
                }

                await _epgService.ClearEpgAsync();
                await _epgService.VacuumAsync();

                await UpdateCacheSizeAsync();

                SetSharedAndPanelStatus(
                    SettingsStatusArea.Cache,
                    _localizationService.GetString("GlobalSettings.Cache.Clear.SuccessMessage"));
            }
            catch (Exception ex)
            {
                SetSharedAndPanelStatus(
                    SettingsStatusArea.Cache,
                    string.Format(_localizationService.GetString("Common.ErrorFormat"), ex.Message));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();
        _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        _settingsService.SettingsChanged -= OnSettingsService_Changed;
        _licenseService.SubscriptionChanged -= OnLicenseSubscriptionChanged;
        DetachCustomEpgHandlers(CustomEpgUrls);

        await _initialStatisticsTask.ConfigureAwait(false);
        await _statisticsScanGate.WaitAsync().ConfigureAwait(false);
        _statisticsScanGate.Release();

        _autoSaveEnabled = false;
        await _autoSaveCoordinator.DisposeAsync().ConfigureAwait(false);

        _epgRefreshWatchCts?.Cancel();
        _epgRefreshWatchCts?.Dispose();
        _epgRefreshWatchCts = null;

        CancelAllStatusAutoClears();
    }

    /// <summary>
    /// Flushes any pending auto-save before the DI scope is disposed.
    /// Must be called while all dependencies are still alive.
    /// </summary>
    public async Task FlushPendingAutoSaveAsync()
    {
        await _autoSaveCoordinator.DisposeAsync().ConfigureAwait(false);
    }

}

/// <summary>
/// Özel EPG URL öğesi
/// </summary>
internal enum SettingsStatusArea
{
    Appearance,
    Playback,
    Audio,
    Download,
    Channel,
    Epg,
    Privacy,
    Cache,
    Reset
}

internal sealed class SettingsChangeOriginGate
{
    private readonly AsyncLocal<int> _ownedSaveDepth = new();

    internal bool ShouldReload => _ownedSaveDepth.Value == 0;

    internal async Task RunOwnedSaveAsync(Func<Task> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        _ownedSaveDepth.Value++;
        try
        {
            await saveAsync();
        }
        finally
        {
            _ownedSaveDepth.Value--;
        }
    }
}

internal static class CustomEpgSourcePolicy
{
    internal static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool CanAdd(IEnumerable<string?> values, bool isPremium)
    {
        var sources = values.ToList();
        var limit = isPremium ? AppSettings.EPG_URL_LIMIT : AppSettings.EPG_URL_FREE_LIMIT;
        return sources.Count < limit && sources.All(IsValid);
    }

    internal static List<string> BuildPersistedSources(IEnumerable<EpgUrlItem> items)
    {
        var result = new List<string>();
        foreach (var item in items)
        {
            if (IsValid(item.Url))
            {
                result.Add(item.Url.Trim());
            }
            else if (IsValid(item.PersistedUrl))
            {
                result.Add(item.PersistedUrl!.Trim());
            }
        }

        return result;
    }

    internal static void MarkPersisted(IEnumerable<EpgUrlItem> items)
    {
        foreach (var item in items)
        {
            if (IsValid(item.Url))
            {
                item.PersistedUrl = item.Url.Trim();
            }
        }
    }
}

public partial class EpgUrlItem : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;

    internal string? PersistedUrl { get; set; }
}
