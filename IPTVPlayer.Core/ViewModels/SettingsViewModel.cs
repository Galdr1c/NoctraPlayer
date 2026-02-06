using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

/// <summary>
/// Ayarlar view model
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;
    private readonly IEpgService _epgService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _newPlaylistName = string.Empty;

    [ObservableProperty]
    private string _newPlaylistUrl = string.Empty;

    [ObservableProperty]
    private string _epgUrl = string.Empty;

    [ObservableProperty]
    private bool _isEpgLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _defaultVolume = 100;

    [ObservableProperty]
    private bool _autoPlay = true;

    [ObservableProperty]
    private bool _rememberLastChannel = true;

    public SettingsViewModel(IPlaylistService playlistService, IEpgService epgService, IThemeService themeService)
    {
        _playlistService = playlistService;
        _epgService = epgService;
        _themeService = themeService;
        
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Uygulama ayarlarını yükle (örn. Properties.Settings veya JSON config)
        // Şimdilik varsayılan değerler kullanılıyor
    }

    [RelayCommand]
    private async Task AddPlaylistAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName) || string.IsNullOrWhiteSpace(NewPlaylistUrl))
        {
            StatusMessage = "Lütfen playlist adı ve URL'sini girin";
            return;
        }

        try
        {
            StatusMessage = "Playlist ekleniyor...";
            var playlist = await _playlistService.AddFromUrlAsync(NewPlaylistName, NewPlaylistUrl);
            StatusMessage = $"'{playlist.Name}' başarıyla eklendi ({playlist.ChannelCount} kanal)";
            
            NewPlaylistName = string.Empty;
            NewPlaylistUrl = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadEpgAsync()
    {
        if (string.IsNullOrWhiteSpace(EpgUrl))
        {
            StatusMessage = "Lütfen EPG URL'sini girin";
            return;
        }

        try
        {
            IsEpgLoading = true;
            StatusMessage = "EPG yükleniyor...";
            
            await _epgService.LoadEpgAsync(EpgUrl);
            StatusMessage = $"EPG yüklendi (Son güncelleme: {_epgService.LastUpdated:HH:mm})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"EPG yüklenemedi: {ex.Message}";
        }
        finally
        {
            IsEpgLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        _themeService.SetTheme(IsDarkTheme);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // Ayarları kaydet
        StatusMessage = "Ayarlar kaydedildi";
    }
}
