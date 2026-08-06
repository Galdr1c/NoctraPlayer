using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;
using Noctra.Services;
using Noctra.Core.Services;
using Noctra.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.ViewModels;

public partial class GlobalSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;
    private readonly IAppVersionService _appVersionService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IDiagnosticReportService _diagnosticService;
    private readonly ILicenseService _licenseService;
    private readonly IProfileService _profileService;
    private readonly IEpgService _epgService;
    private readonly ILocalizationService _localizationService;
    private readonly IAppUpdateService _appUpdateService;

    [ObservableProperty]
    private string _cacheSizeString = "0 B";

    [ObservableProperty]
    private string _currentVersion = "1.0.0";

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(StartUpdateCommand), nameof(CompleteUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateCheckButton))]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(StartUpdateCommand), nameof(CompleteUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateCheckButton))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(StartUpdateCommand), nameof(CompleteUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateCheckButton))]
    private bool _isStartingUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(StartUpdateCommand), nameof(CompleteUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateCheckButton))]
    private bool _isUpdateDownloaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(StartUpdateCommand), nameof(CompleteUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateCheckButton))]
    private bool _isUpdateDownloading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateCheckButton))]
    private bool _isUpdateUnsupported;

    [ObservableProperty]
    private string? _availableVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPromoCodeCommand))]
    private string _promoCodeInput = string.Empty;

    [ObservableProperty]
    private string _promoCodeStatus = string.Empty;

    [ObservableProperty]
    private bool _isPromoCodeStatusSuccess;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPromoCodeCommand))]
    private bool _isApplyingPromoCode;

    [ObservableProperty]
    private string _developerPassword = string.Empty;

    [ObservableProperty]
    private bool _isDeveloperModeActive;

    partial void OnIsDeveloperModeActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTogglePremiumForTesting));
    }

    partial void OnDeveloperPasswordChanged(string value)
    {
        var envPassword = Environment.GetEnvironmentVariable("DEV_PASSWORD");
        if (!string.IsNullOrEmpty(envPassword) && value == envPassword)
        {
            IsDeveloperModeActive = true;
            DeveloperPassword = string.Empty; // clear
        }
        else if (value == "close")
        {
            IsDeveloperModeActive = false;
            DeveloperPassword = string.Empty;
        }
    }

    [RelayCommand]
    private void TogglePremium()
    {
        if (_licenseService.IsEditionLockedPremium)
        {
            return;
        }

        if (_licenseService.IsPremium)
        {
            _licenseService.DeactivatePremium();
        }
        else
        {
            _licenseService.ActivatePremium();
        }
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(IsFreeEdition));
        OnPropertyChanged(nameof(CanTogglePremiumForTesting));
    }

    private GlobalSettings _settings = new();
    public GlobalSettings Settings
    {
        get => _settings;
        set
        {
            if (ReferenceEquals(_settings, value))
            {
                return;
            }

            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            SetProperty(ref _settings, value);
            _settings.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    public GlobalSettingsViewModel(
        IThemeService themeService,
        IDialogService dialogService,
        ISettingsService settingsService,
        ICacheService cacheService,
        IAppVersionService appVersionService,
        IDispatcherService dispatcherService,
        IDiagnosticReportService diagnosticService,
        ILicenseService licenseService,
        IProfileService profileService,
        IEpgService epgService,
        ILocalizationService localizationService,
        IAppUpdateService appUpdateService)
    {
        _themeService = themeService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _cacheService = cacheService;
        _appVersionService = appVersionService;
        _dispatcherService = dispatcherService;
        _diagnosticService = diagnosticService;
        _licenseService = licenseService;
        _profileService = profileService;
        _epgService = epgService;
        _localizationService = localizationService;
        _appUpdateService = appUpdateService;
        
        CurrentVersion = _appVersionService.DisplayVersion;
        UpdateStatusText = string.Empty;
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        _licenseService.SubscriptionChanged += OnLicenseSubscriptionChanged;
        
        // Güncelleme durum değişikliklerini dinle
        _appUpdateService.UpdateStateChanged += OnUpdateStateChanged;
        
        LoadSettings();
        _ = UpdateCacheSizeAsync();
        // Bekleyen flexible update kontrolü
        _ = CheckPendingUpdateAsync();
    }

    private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs e)
    {
        _dispatcherService.BeginInvoke(() => ApplyUpdateState(e));
    }

    private void ApplyUpdateState(UpdateStateChangedEventArgs e)
    {
        IsCheckingForUpdates = false;

        switch (e.Status)
        {
            case UpdateCheckStatus.Downloading:
                IsUpdateUnsupported = false;
                IsStartingUpdate = false;
                IsUpdateAvailable = false;
                IsUpdateDownloaded = false;
                IsUpdateDownloading = true;
                UpdateStatusText = e.ProgressPercent.HasValue
                    ? string.Format(
                        _localizationService.GetString("Settings.Update.DownloadingProgressFormat"),
                        e.ProgressPercent.Value)
                    : _localizationService.GetString("Settings.Update.Downloading");
                break;

            case UpdateCheckStatus.Downloaded:
                IsUpdateUnsupported = false;
                IsStartingUpdate = false;
                IsUpdateAvailable = false;
                IsUpdateDownloading = false;
                IsUpdateDownloaded = true;
                UpdateStatusText = _localizationService.GetString("Settings.Update.Downloaded");
                break;

            case UpdateCheckStatus.UpdateAvailable:
                IsUpdateUnsupported = false;
                IsStartingUpdate = false;
                IsUpdateDownloading = false;
                IsUpdateDownloaded = false;
                IsUpdateAvailable = true;
                UpdateStatusText = GetAvailableUpdateText(AvailableVersion);
                break;

            case UpdateCheckStatus.UpToDate:
                IsUpdateUnsupported = false;
                IsStartingUpdate = false;
                IsUpdateAvailable = false;
                IsUpdateDownloading = false;
                IsUpdateDownloaded = false;
                AvailableVersion = null;
                UpdateStatusText = _localizationService.GetString("Settings.Update.UpToDate");
                break;

            case UpdateCheckStatus.Canceled:
            {
                IsUpdateUnsupported = false;
                var hadDownloadedUpdate = IsUpdateDownloaded;
                var hadKnownUpdate = IsUpdateAvailable || IsUpdateDownloading || IsStartingUpdate;

                IsStartingUpdate = false;
                IsUpdateDownloading = false;

                if (hadDownloadedUpdate)
                {
                    IsUpdateAvailable = false;
                    IsUpdateDownloaded = true;
                    UpdateStatusText = _localizationService.GetString("Settings.Update.Downloaded");
                }
                else if (hadKnownUpdate)
                {
                    IsUpdateDownloaded = false;
                    IsUpdateAvailable = true;
                    UpdateStatusText = GetAvailableUpdateText(AvailableVersion);
                }
                else
                {
                    ResetUpdateFlags();
                    UpdateStatusText = _localizationService.GetString("Settings.Update.CheckFailed");
                }

                break;
            }

            case UpdateCheckStatus.Unsupported:
                ResetUpdateFlags();
                IsUpdateUnsupported = true;
                UpdateStatusText = _localizationService.GetString("Settings.Update.Unsupported");
                break;

            case UpdateCheckStatus.Error:
            {
                var wasInstalling = IsStartingUpdate || IsUpdateDownloading || IsUpdateDownloaded;
                ResetUpdateFlags();
                IsUpdateUnsupported = false;
                UpdateStatusText = _localizationService.GetString(
                    wasInstalling
                        ? "Settings.Update.StartFailed"
                        : "Settings.Update.CheckFailed");
                break;
            }
        }
    }

    private string GetAvailableUpdateText(string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            ? _localizationService.GetString("Settings.Update.AvailableGeneric")
            : string.Format(
                _localizationService.GetString("Settings.Update.AvailableFormat"),
                version);
    }

    private void ResetUpdateFlags()
    {
        IsStartingUpdate = false;
        IsUpdateAvailable = false;
        IsUpdateDownloading = false;
        IsUpdateDownloaded = false;
    }

    public bool IsPremium => _licenseService.IsPremium;
    public bool IsFreeEdition => !_licenseService.IsEditionLockedPremium;
    public bool CanTogglePremiumForTesting
    {
        get
        {
#if DEBUG
            return IsDeveloperModeActive && !_licenseService.IsEditionLockedPremium;
#else
            return false;
#endif
        }
    }

    public string PremiumStatusText
    {
        get
        {
            if (!_licenseService.IsPremium)
            {
                return string.Empty;
            }

            var expiresAt = _licenseService.PromoPremiumExpiresAtUtc;
            if (expiresAt.HasValue)
            {
                return string.Format(_localizationService.GetString("GlobalSettings.Promo.Status.PremiumFormat"), expiresAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
            }

            return _localizationService.GetString("GlobalSettings.Promo.Status.Premium");
        }
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

    private async Task UpdateCacheSizeAsync()
    {
        CacheSizeString = await _cacheService.GetCacheSizeStringAsync();
    }

    private void OnSettingsService_Changed()
    {
        LoadSettings();
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(PremiumStatusText));
    }

    private void OnLicenseSubscriptionChanged()
    {
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(PremiumStatusText));
        PromoCodeStatus = PremiumStatusText;
        IsPromoCodeStatusSuccess = IsPremium;
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        Settings = new GlobalSettings
        {
            IsDarkTheme = s.IsDarkTheme,
            Language = s.Language,
            HardwareAcceleration = s.HardwareAcceleration,
            DiagnosticDataConsent = s.DiagnosticDataConsent
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        SaveSettings();
    }

    public bool ShowUpdateCheckButton => CanCheckForUpdates;

    private bool CanCheckForUpdates =>
        !IsCheckingForUpdates &&
        !IsStartingUpdate &&
        !IsUpdateAvailable &&
        !IsUpdateDownloading &&
        !IsUpdateDownloaded &&
        !IsUpdateUnsupported;

    private bool CanStartUpdate =>
        IsUpdateAvailable &&
        !IsCheckingForUpdates &&
        !IsStartingUpdate &&
        !IsUpdateDownloading &&
        !IsUpdateDownloaded;

    private bool CanCompleteUpdate =>
        IsUpdateDownloaded &&
        !IsCheckingForUpdates &&
        !IsStartingUpdate &&
        !IsUpdateDownloading;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        if (!CanCheckForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        IsUpdateUnsupported = false;
        IsUpdateAvailable = false;
        AvailableVersion = null;
        UpdateStatusText = _localizationService.GetString("Settings.Update.Checking");

        try
        {
            var result = await _appUpdateService.CheckAsync();
            AvailableVersion = result.LatestVersion;

            ApplyUpdateState(new UpdateStateChangedEventArgs(
                result.Status,
                result.ErrorMessage ?? string.Empty));
        }
        catch (OperationCanceledException)
        {
            ApplyUpdateState(new UpdateStateChangedEventArgs(UpdateCheckStatus.Canceled));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSettingsViewModel] CheckForUpdates failed: {ex.Message}");
            ApplyUpdateState(new UpdateStateChangedEventArgs(UpdateCheckStatus.Error));
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartUpdate))]
    private async Task StartUpdateAsync()
    {
        if (!CanStartUpdate)
        {
            return;
        }

        IsStartingUpdate = true;
        var started = false;
        try
        {
            started = await _appUpdateService.StartUpdateAsync();
            // Platform servisi terminal bir state event'i yayınladıysa o mesajı koru.
            // Hiç event gelmeden false dönerse kontrollü bir başlangıç hatası göster.
            if (!started && IsStartingUpdate)
            {
                ResetUpdateFlags();
                UpdateStatusText = _localizationService.GetString("Settings.Update.StartFailed");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSettingsViewModel] StartUpdate failed: {ex.Message}");
            ResetUpdateFlags();
            UpdateStatusText = _localizationService.GetString("Settings.Update.StartFailed");
        }
        finally
        {
            // Android'de Store onay ekranı sonuçlanana, Windows'ta Store işlemi event ile
            // terminal duruma geçene kadar butonları kilitli tut.
            if (!started)
            {
                IsStartingUpdate = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompleteUpdate))]
    private async Task CompleteUpdateAsync()
    {
        if (!CanCompleteUpdate)
        {
            return;
        }

        IsStartingUpdate = true;
        try
        {
            var completed = await _appUpdateService.CompleteUpdateAsync();
            if (!completed && IsUpdateDownloaded)
            {
                UpdateStatusText = _localizationService.GetString("Settings.Update.StartFailed");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSettingsViewModel] CompleteUpdate failed: {ex.Message}");
            UpdateStatusText = _localizationService.GetString("Settings.Update.StartFailed");
        }
        finally
        {
            IsStartingUpdate = false;
        }
    }

    private async Task CheckPendingUpdateAsync()
    {
        try
        {
            var pending = await _appUpdateService.CheckPendingUpdateAsync();
            if (pending.Status is UpdateCheckStatus.Downloaded or UpdateCheckStatus.Downloading)
            {
                AvailableVersion = pending.LatestVersion;
                ApplyUpdateState(new UpdateStateChangedEventArgs(pending.Status));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSettingsViewModel] CheckPendingUpdate failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ShowUpsell()
    {
        await _dialogService.ShowUpsellAsync();
    }

    private bool CanApplyPromoCode => !IsApplyingPromoCode && !string.IsNullOrWhiteSpace(PromoCodeInput);

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
            PromoCodeStatus = result.Message;
            IsPromoCodeStatusSuccess = result.Success;
            if (result.Success)
            {
                PromoCodeInput = string.Empty;
                OnPropertyChanged(nameof(IsPremium));
                OnPropertyChanged(nameof(PremiumStatusText));
            }
        }
        catch (Exception ex)
        {
            PromoCodeStatus = string.Format(_localizationService.GetString("GlobalSettings.Promo.Error.ApplyFailedFormat"), ex.Message);
            IsPromoCodeStatusSuccess = false;
        }
        finally
        {
            IsApplyingPromoCode = false;
        }
    }



    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("GlobalSettings.Cache.Clear.ConfirmTitle"),
            _localizationService.GetString("GlobalSettings.Cache.Clear.ConfirmMessage")
        );

        if (confirmed)
        {
            try
            {
                // 0. Auto-save current settings before clearing (to avoid memory loss)
                await _settingsService.SaveAsync();

                // 1. Clear file system folders
                await _cacheService.ClearCacheAsync();
                
                // 2. Clear orphaned settings
                var profiles = await _profileService.GetProfilesAsync();
                var activeIds = new HashSet<int>(profiles.Select(p => p.Id));
                activeIds.Add(0); // Master settings is always active
                activeIds.Add(_settingsService.Settings.ProfileId); // Always protect current profile!
                int cleanedSettings = await _settingsService.CleanOrphanedSettingsAsync(activeIds);
                
                // 3. Clear EPG Data
                await _epgService.ClearEpgAsync();
                
                // 4. Shrink DB
                await _epgService.VacuumAsync();
                
                // Update size string
                await UpdateCacheSizeAsync();
                
                var successMsg = _localizationService.GetString("GlobalSettings.Cache.Clear.SuccessMessage");
                if (cleanedSettings > 0)
                {
                    successMsg += string.Format(_localizationService.GetString("GlobalSettings.Cache.Clear.OrphanedSuffix"), cleanedSettings);
                }
                
                await _dialogService.ShowMessageAsync(
                    _localizationService.GetString("GlobalSettings.Cache.Clear.SuccessTitle"),
                    successMsg
                );
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    _localizationService.GetString("GlobalSettings.Cache.Clear.ErrorTitle"),
                    _localizationService.GetString("GlobalSettings.Cache.Clear.ErrorMessage"),
                    ex
                );
            }
        }
    }

    private void SaveSettings()
    {
        var s = _settingsService.Settings;
        s.IsDarkTheme = Settings.IsDarkTheme;
        s.Language = Settings.Language;
        s.HardwareAcceleration = Settings.HardwareAcceleration;
        s.DiagnosticDataConsent = Settings.DiagnosticDataConsent;
        
        _ = _settingsService.SaveAsyncBestEffort();
    }

    public void Dispose()
    {
        _appUpdateService.UpdateStateChanged -= OnUpdateStateChanged;

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= OnSettingsService_Changed;
        }
        
        if (_licenseService != null)
        {
            _licenseService.SubscriptionChanged -= OnLicenseSubscriptionChanged;
        }

        if (_settings != null)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
        }
    }
}

// Global Settings Model
public partial class GlobalSettings : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private bool _hardwareAcceleration = true;

    [ObservableProperty]
    private bool _diagnosticDataConsent = false;
}
