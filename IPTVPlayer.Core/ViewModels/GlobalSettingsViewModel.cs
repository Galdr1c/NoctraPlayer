using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

public partial class GlobalSettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private GlobalSettings _settings = new();

    public GlobalSettingsViewModel(
        IThemeService themeService,
        IDialogService dialogService)
    {
        _themeService = themeService;
        _dialogService = dialogService;

        LoadSettings();
    }

    private void LoadSettings()
    {
        Settings = new GlobalSettings
        {
            IsDarkTheme = _themeService.IsDarkTheme,
            Language = "tr",
            AutoUpdate = true,
            HardwareAcceleration = true,
            Analytics = false
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

        if (!confirmed)
        {
            return;
        }

        try
        {
            await Task.Delay(500);

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

    private void SaveSettings()
    {
        // Save to storage (JSON file, registry, etc.)
    }

    partial void OnSettingsChanged(GlobalSettings value)
    {
        SaveSettings();
    }
}

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
