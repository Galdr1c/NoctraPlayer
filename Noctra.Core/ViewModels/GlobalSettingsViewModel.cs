using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;
using Noctra.Services;
using Noctra.Core.Services;
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

    [ObservableProperty]
    private string _cacheSizeString = "0 B";

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
        ICacheService cacheService)
    {
        _themeService = themeService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _cacheService = cacheService;
        
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        
        LoadSettings();
        _ = UpdateCacheSizeAsync();
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
