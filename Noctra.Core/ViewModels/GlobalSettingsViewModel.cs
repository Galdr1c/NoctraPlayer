using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;
using Noctra.Services;
using Noctra.Core.Services;
using Noctra.Models;
using System;

using System.ComponentModel;
using System.Threading.Tasks;

namespace Noctra.ViewModels;

public partial class GlobalSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;
    private readonly IUpdateService _updateService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IDiagnosticReportService _diagnosticService;

    [ObservableProperty]
    private string _cacheSizeString = "0 B";

    [ObservableProperty]
    private string _currentVersion = "1.0.0";

    [ObservableProperty]
    private string _updateStatusText = "Güncel";

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    private UpdateInfo? _latestUpdate;

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
        IUpdateService updateService,
        IDispatcherService dispatcherService,
        IDiagnosticReportService diagnosticService)
    {
        _themeService = themeService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _cacheService = cacheService;
        _updateService = updateService;
        _dispatcherService = dispatcherService;
        _diagnosticService = diagnosticService;
        
        CurrentVersion = _updateService.CurrentVersion;
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        
        LoadSettings();
        _ = UpdateCacheSizeAsync();
    }

    [RelayCommand]
    private void ReportBug()
    {
        _diagnosticService.OpenBugReport();
    }

    private async Task UpdateCacheSizeAsync()
    {
        CacheSizeString = await _cacheService.GetCacheSizeStringAsync();
    }

    private void OnSettingsService_Changed()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        Settings = new GlobalSettings
        {
            IsDarkTheme = s.IsDarkTheme,
            Language = s.Language,
            AutoUpdate = s.AutoUpdate,
            HardwareAcceleration = s.HardwareAcceleration,
            Analytics = s.Analytics
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        _themeService.SetTheme(Settings.IsDarkTheme);
        SaveSettings();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates) return;

        IsCheckingUpdates = true;
        UpdateStatusText = "Kontrol ediliyor...";
        IsUpdateAvailable = false;
        _latestUpdate = null;

        try
        {
            await Task.Delay(800); // UI feedback
            var update = await _updateService.CheckForUpdatesAsync();
            
            if (update != null)
            {
                _latestUpdate = update;
                IsUpdateAvailable = true;
                UpdateStatusText = $"Yeni Sürüm: v{update.Version}";
            }
            else
            {
                UpdateStatusText = "Uygulama güncel";
            }
        }
        catch
        {
            UpdateStatusText = "Kontrol başarısız";
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        if (_latestUpdate == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Güncelleme",
            $"v{_latestUpdate.Version} sürümünü şimdi indirmek istiyor musunuz?\n\nDeğişiklikler:\n{_latestUpdate.Changelog}"
        );

        if (confirmed)
        {
            await _updateService.StartUpdateAsync(_latestUpdate);
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Önbelleği Temizle",
            "Tüm önbellek dosyaları silinecek. Devam etmek istiyor musunuz?"
        );

        if (confirmed)
        {
            try
            {
                await _cacheService.ClearCacheAsync();
                await UpdateCacheSizeAsync();
                
                await _dialogService.ShowMessageAsync(
                    "Başarılı",
                    "Önbellek temizlendi"
                );
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    "Hata",
                    "Önbellek temizlenemedi",
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
        s.AutoUpdate = Settings.AutoUpdate;
        s.HardwareAcceleration = Settings.HardwareAcceleration;
        s.Analytics = Settings.Analytics;
        
        _ = _settingsService.SaveAsync();
    }

    public void Dispose()
    {
        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= OnSettingsService_Changed;
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
    private string _language = "tr";

    [ObservableProperty]
    private bool _autoUpdate = true;

    [ObservableProperty]
    private bool _hardwareAcceleration = true;

    [ObservableProperty]
    private bool _analytics = false;
}
