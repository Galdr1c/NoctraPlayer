using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;
    private readonly IEpgService _epgService;
    private readonly IThemeService _themeService;
    private readonly ILicenseService _licenseService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private Profile? _currentProfile;

    [ObservableProperty]
    private UserSettings _settings = new();

    [ObservableProperty]
    private UserSettings _originalSettings = new();

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _isPremium;

    public SettingsViewModel(
        IPlaylistService playlistService,
        IEpgService epgService,
        IThemeService themeService,
        ILicenseService licenseService,
        IDialogService dialogService)
    {
        _playlistService = playlistService;
        _epgService = epgService;
        _themeService = themeService;
        _licenseService = licenseService;
        _dialogService = dialogService;

        IsPremium = _licenseService.IsPremium;
        LoadSettings();
    }

    public void Initialize(Profile profile)
    {
        CurrentProfile = profile;
    }

    private void LoadSettings()
    {
        Settings = new UserSettings
        {
            AutoPlayNext = true,
            AutoSkipIntro = false,
            AutoPlayPreviews = false,
            Quality = "Auto",
            SubtitleFontSize = 20,
            SubtitleBackgroundOpacity = 75,
            DefaultAudioLanguage = "tr",
            DefaultSubtitleLanguage = "off"
        };

        OriginalSettings = Settings.Clone();
        TrackChanges();
    }

    private void TrackChanges()
    {
        Settings.PropertyChanged += (s, e) =>
        {
            HasUnsavedChanges = !Settings.Equals(OriginalSettings);
        };
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            await SaveToStorageAsync(Settings);
            OriginalSettings = Settings.Clone();
            HasUnsavedChanges = false;

            await _dialogService.ShowMessageAsync(
                "Başarılı",
                "Ayarlar kaydedildi"
            );
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(
                "Hata",
                "Ayarlar kaydedilemedi",
                ex
            );
        }
    }

    [RelayCommand]
    private void CancelChanges()
    {
        Settings = OriginalSettings.Clone();
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task EditProfileAsync()
    {
        if (CurrentProfile != null)
        {
            await _dialogService.ShowEditProfileAsync(CurrentProfile.Id);
        }
    }

    [RelayCommand]
    private async Task ChangeProviderAsync()
    {
        await _dialogService.ShowMessageAsync(
            "Sağlayıcı Değiştir",
            "Bu özellik yakında eklenecek"
        );
    }

    [RelayCommand]
    private async Task UpgradePremiumAsync()
    {
        await _dialogService.ShowUpsellAsync();
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Çıkış Yap",
            "Bu profilden çıkmak istediğinize emin misiniz?"
        );

        if (confirmed)
        {
            RequestClose?.Invoke();
        }
    }

    public event Action? RequestClose;

    private async Task SaveToStorageAsync(UserSettings settings)
    {
        await Task.CompletedTask;
    }
}
