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
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isStartingUpdate;

    [ObservableProperty]
    private bool _isUpdateDownloaded;

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
        
        LoadSettings();
        _ = UpdateCacheSizeAsync();
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

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates)
            return;

        IsCheckingForUpdates = true;
        IsUpdateAvailable = false;
        IsUpdateDownloaded = false;
        UpdateStatusText = _localizationService.GetString("Settings.Update.Checking");

        try
        {
            var result = await _appUpdateService.CheckAsync();

            IsUpdateAvailable = result.IsUpdateAvailable;
            AvailableVersion = result.LatestVersion;

            UpdateStatusText = result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable =>
                    string.Format(_localizationService.GetString("Settings.Update.AvailableFormat"), result.LatestVersion ?? ""),
                UpdateCheckStatus.UpToDate =>
                    _localizationService.GetString("Settings.Update.UpToDate"),
                UpdateCheckStatus.Error =>
                    _localizationService.GetString("Settings.Update.CheckFailed"),
                UpdateCheckStatus.Unsupported =>
                    _localizationService.GetString("Settings.Update.Unsupported"),
                _ => _localizationService.GetString("Settings.Update.CheckFailed")
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSettingsViewModel] CheckForUpdates failed: {ex.Message}");
            UpdateStatusText = _localizationService.GetString("Settings.Update.CheckFailed");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        if (!IsUpdateAvailable || IsStartingUpdate)
            return;

        IsStartingUpdate = true;
        try
        {
            var started = await _appUpdateService.StartUpdateAsync();
            if (!started)
            {
                UpdateStatusText = _localizationService.GetString("Settings.Update.StartFailed");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSettingsViewModel] StartUpdate failed: {ex.Message}");
            UpdateStatusText = _localizationService.GetString("Settings.Update.StartFailed");
        }
        finally
        {
            IsStartingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task CompleteUpdateAsync()
    {
        if (!IsUpdateDownloaded || IsStartingUpdate)
            return;

        IsStartingUpdate = true;
        try
        {
            var completed = await _appUpdateService.CompleteUpdateAsync();
            if (!completed)
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
            if (pending.Status == UpdateCheckStatus.Downloaded)
            {
                IsUpdateDownloaded = true;
                AvailableVersion = pending.LatestVersion;
                UpdateStatusText = _localizationService.GetString("Settings.Update.Downloaded");
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
        
        _ = _settingsService.SaveAsync();
    }

    public void Dispose()
    {
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
