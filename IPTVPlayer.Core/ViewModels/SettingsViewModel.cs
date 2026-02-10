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

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeService.SetTheme(value);
        
        // Tema değiştiği an kaydet (user request)
        if (_settingsService != null)
        {
            _settingsService.Settings.IsDarkTheme = value;
            _ = _settingsService.SaveAsync();
        }
    }
    
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

    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private string _currentProfileName = string.Empty;
    
    [ObservableProperty]
    private string _currentProfileAvatar = string.Empty;

    [ObservableProperty]
    private string _providerName = string.Empty;
    
    [ObservableProperty]
    private string _providerUrl = string.Empty;
    
    [ObservableProperty]
    private string _providerUsername = string.Empty;
    
    [ObservableProperty]
    private string _providerPassword = string.Empty;
    
    [ObservableProperty]
    private DateTime? _expirationDate;
    
    [ObservableProperty]
    private string _expirationStatus = string.Empty;
    
    [ObservableProperty]
    private DateTime _profileCreatedAt;

    public SettingsViewModel(
        ISettingsService settingsService,
        IPlaylistService playlistService, 
        IEpgService epgService, 
        IThemeService themeService,
        MainViewModel mainViewModel)
    {
        _settingsService = settingsService;
        _playlistService = playlistService;
        _epgService = epgService;
        _themeService = themeService;
        _mainViewModel = mainViewModel;
        
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        
        LoadSettings();
        LoadProfileInfo();
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentProfile))
        {
            // If the profile object itself changed
            if (_mainViewModel.CurrentProfile != null)
            {
                _mainViewModel.CurrentProfile.PropertyChanged -= CurrentProfile_PropertyChanged;
                _mainViewModel.CurrentProfile.PropertyChanged += CurrentProfile_PropertyChanged;
                
                if (_mainViewModel.CurrentProfile.ProviderAccount != null)
                {
                   _mainViewModel.CurrentProfile.ProviderAccount.PropertyChanged -= ProviderAccount_PropertyChanged;
                   _mainViewModel.CurrentProfile.ProviderAccount.PropertyChanged += ProviderAccount_PropertyChanged;
                }
            }
            LoadProfileInfo();
        }
    }

    private void CurrentProfile_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Profile.ProviderAccount))
        {
             if (_mainViewModel.CurrentProfile?.ProviderAccount != null)
             {
                 _mainViewModel.CurrentProfile.ProviderAccount.PropertyChanged -= ProviderAccount_PropertyChanged;
                 _mainViewModel.CurrentProfile.ProviderAccount.PropertyChanged += ProviderAccount_PropertyChanged;
             }
             LoadProfileInfo();
        }
    }

    private void ProviderAccount_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        LoadProfileInfo();
    }
    
    private void LoadProfileInfo()
    {
        if (_mainViewModel.CurrentProfile != null)
        {
            CurrentProfileName = _mainViewModel.CurrentProfile.Name;
            CurrentProfileAvatar = _mainViewModel.CurrentProfile.Avatar;
            ProfileCreatedAt = _mainViewModel.CurrentProfile.CreatedAt;
            
            // If CreatedAt is default (min value), set it to Now for display or handle it
            if (ProfileCreatedAt == DateTime.MinValue) ProfileCreatedAt = DateTime.Now;

            if (_mainViewModel.CurrentProfile.ProviderAccount != null)
            {
                var account = _mainViewModel.CurrentProfile.ProviderAccount;
                ProviderName = account.Name;
                ProviderUrl = account.Url;
                ProviderUsername = account.Username ?? "Yok";
                
                // Mask password
                var pass = account.Password;
                ProviderPassword = !string.IsNullOrEmpty(pass) ? new string('*', 10) : "Yok";
                
                ExpirationDate = account.ExpirationDate;
                
                if (ExpirationDate.HasValue)
                {
                    var daysLeft = (ExpirationDate.Value - DateTime.Now).TotalDays;
                    if (daysLeft < 0) ExpirationStatus = "Süresi Dolmuş";
                    else if (daysLeft < 7) ExpirationStatus = $"{Math.Ceiling(daysLeft)} Gün Kaldı (Yakında Bitiyor)";
                    else ExpirationStatus = $"{Math.Ceiling(daysLeft)} Gün Kaldı";
                }
                else
                {
                    ExpirationStatus = "Süresiz / Bilinmiyor";
                }
            }
        }
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
