using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

/// <summary>
/// Ayarlar view model
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IPlaylistService _playlistService;
    private readonly IEpgService _epgService;
    private readonly IThemeService _themeService;

    // ============ Oynatma Ayarları ============
    
    [ObservableProperty]
    private bool _autoPlayNext;
    
    [ObservableProperty]
    private bool _autoSkipIntro;
    
    [ObservableProperty]
    private int _selectedDataUsage;
    
    [ObservableProperty]
    private int _defaultVolume;
    
    [ObservableProperty]
    private bool _rememberLastChannel;
    
    // ============ Altyazı Ayarları ============
    
    [ObservableProperty]
    private int _selectedSubtitleLanguage;
    
    [ObservableProperty]
    private int _subtitleFontSize;
    
    [ObservableProperty]
    private int _subtitleBackgroundOpacity;
    
    // ============ İndirme Ayarları ============
    
    [ObservableProperty]
    private int _selectedDownloadQuality;
    
    [ObservableProperty]
    private bool _downloadWifiOnly;
    
    [ObservableProperty]
    private string _downloadPath = string.Empty;
    
    // ============ Görünüm ============
    
    [ObservableProperty]
    private bool _isDarkTheme;
    
    // ============ EPG & Playlist ============
    
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
    
    // ============ TMDB ============
    
    [ObservableProperty]
    private string _tmdbApiKey = string.Empty;

    public SettingsViewModel(
        ISettingsService settingsService,
        IPlaylistService playlistService, 
        IEpgService epgService, 
        IThemeService themeService)
    {
        _settingsService = settingsService;
        _playlistService = playlistService;
        _epgService = epgService;
        _themeService = themeService;
        
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        
        // Playback
        AutoPlayNext = s.AutoPlayNext;
        AutoSkipIntro = s.AutoSkipIntro;
        SelectedDataUsage = (int)s.DataUsage;
        DefaultVolume = s.DefaultVolume;
        RememberLastChannel = s.RememberLastChannel;
        
        // Subtitles
        SelectedSubtitleLanguage = s.SubtitleLanguage switch
        {
            "tr" => 0,
            "en" => 1,
            _ => 2  // none
        };
        SubtitleFontSize = s.SubtitleFontSize;
        SubtitleBackgroundOpacity = s.SubtitleBackgroundOpacity;
        
        // Downloads
        SelectedDownloadQuality = (int)s.DownloadQuality;
        DownloadWifiOnly = s.DownloadWifiOnly;
        DownloadPath = s.DownloadPath;
        
        // Appearance
        IsDarkTheme = s.IsDarkTheme;
        
        // TMDB
        TmdbApiKey = s.TmdbApiKey ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var s = _settingsService.Settings;
        
        // Playback
        s.AutoPlayNext = AutoPlayNext;
        s.AutoSkipIntro = AutoSkipIntro;
        s.DataUsage = (DataUsageLevel)SelectedDataUsage;
        s.DefaultVolume = DefaultVolume;
        s.RememberLastChannel = RememberLastChannel;
        
        // Subtitles
        s.SubtitleLanguage = SelectedSubtitleLanguage switch
        {
            0 => "tr",
            1 => "en",
            _ => "none"
        };
        s.SubtitleFontSize = SubtitleFontSize;
        s.SubtitleBackgroundOpacity = SubtitleBackgroundOpacity;
        
        // Downloads
        s.DownloadQuality = (DownloadQuality)SelectedDownloadQuality;
        s.DownloadWifiOnly = DownloadWifiOnly;
        s.DownloadPath = DownloadPath;
        
        // Appearance
        s.IsDarkTheme = IsDarkTheme;
        
        // TMDB
        s.TmdbApiKey = string.IsNullOrWhiteSpace(TmdbApiKey) ? null : TmdbApiKey;
        
        await _settingsService.SaveAsync();
        StatusMessage = "Ayarlar kaydedildi ✓";
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
        _ = SaveSettingsAsync();
    }
    
    [RelayCommand]
    private void ResetToDefaults()
    {
        _settingsService.ResetToDefaults();
        LoadSettings();
        StatusMessage = "Ayarlar varsayılana sıfırlandı";
    }
}
