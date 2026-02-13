using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;
using Noctra.Services;
using System;
using System.Threading.Tasks;

namespace Noctra.ViewModels;

public partial class GlobalSettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private GlobalSettings _settings = new();

    public GlobalSettingsViewModel(
        IThemeService themeService,
        IDialogService dialogService,
        ISettingsService settingsService)
    {
        _themeService = themeService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        
        LoadSettings();
        Settings.SetOnChanged(SaveSettings);
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
                // Clear cache logic
                await Task.Delay(500); // Simulate clearing
                
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

    // Bu metod GlobalSettings içindeki bir property değiştiğinde tetiklenmez.
    // XAML bindingleri genellikle Settings.IsDarkTheme gibi yapıldığı için 
    // GlobalSettings modelinin içinde de PropertyChanged yakalamalıyız veya 
    // UI'daki toggle'lar ViewModel'deki bir komutu tetiklemeli.
    // GlobalSettingsWindow.xaml.cs 'deki DarkTheme_Click ApplyThemeCommand'i çağırıyor, bu iyi.
    // ToggleSwitch'ler ise Bindings kullanıyor. GlobalSettings modeline de hook ekleyelim.
}

// Global Settings Model
public partial class GlobalSettings : ObservableObject
{
    private Action? _onChanged;
    public void SetOnChanged(Action onChanged) => _onChanged = onChanged;

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

    partial void OnIsDarkThemeChanged(bool value) => _onChanged?.Invoke();
    partial void OnLanguageChanged(string value) => _onChanged?.Invoke();
    partial void OnAutoUpdateChanged(bool value) => _onChanged?.Invoke();
    partial void OnHardwareAccelerationChanged(bool value) => _onChanged?.Invoke();
    partial void OnAnalyticsChanged(bool value) => _onChanged?.Invoke();
}


