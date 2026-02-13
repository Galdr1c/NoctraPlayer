using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Data;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IServiceScopeFactory _scopeFactory;

    // ============ Oynatma Ayarları ============
    
    [ObservableProperty]
    private bool _autoPlayNext;
    
    [ObservableProperty]
    private bool _autoSkipIntro;

    [ObservableProperty]
    private bool _autoSkipCredits;
    
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

    [ObservableProperty]
    private string _appLanguage = "tr";

    [ObservableProperty]
    private int _channelListRefreshFrequencyHours;

    [ObservableProperty]
    private int _epgRefreshFrequencyHours;

    [ObservableProperty]
    private string _customEpgUrl = string.Empty;

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
        MainViewModel mainViewModel,
        IServiceScopeFactory scopeFactory)
    {
        _settingsService = settingsService;
        _playlistService = playlistService;
        _epgService = epgService;
        _themeService = themeService;
        _mainViewModel = mainViewModel;
        _scopeFactory = scopeFactory;
        
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        
        LoadSettings();
        LoadProfileInfo();
        _ = ScanChannelListStatsAsync();
        _ = ScanEpgStatsAsync();
        _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
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
            _ = ScanChannelListStatsAsync();
            _ = ScanEpgStatsAsync();
            _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedPlaylist))
        {
            _ = ScanChannelListStatsAsync();
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
                ProviderUrl = GetProviderBaseUrl(account.Url);
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
                    ExpirationStatus = "Bilinmiyor";
                }
            }
        }
    }

    private static string GetProviderBaseUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        var normalized = rawUrl.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }

        return rawUrl;
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        
        // Playback
        AutoPlayNext = s.AutoPlayNext;
        AutoSkipIntro = s.AutoSkipIntro;
        AutoSkipCredits = s.AutoSkipCredits;
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
        AppLanguage = string.IsNullOrWhiteSpace(s.Language) ? "tr" : s.Language;
        ChannelListRefreshFrequencyHours = s.ChannelListRefreshFrequencyHours;
        EpgRefreshFrequencyHours = s.EpgRefreshFrequencyHours;
        CustomEpgUrl = s.CustomEpgUrl ?? string.Empty;
        
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
        s.AutoSkipCredits = AutoSkipCredits;
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
        s.Language = string.IsNullOrWhiteSpace(AppLanguage) ? "tr" : AppLanguage;
        s.ChannelListRefreshFrequencyHours = Math.Max(0, ChannelListRefreshFrequencyHours);
        s.EpgRefreshFrequencyHours = Math.Max(0, EpgRefreshFrequencyHours);
        s.CustomEpgUrl = string.IsNullOrWhiteSpace(CustomEpgUrl) ? null : CustomEpgUrl.Trim();
        
        // TMDB
        s.TmdbApiKey = string.IsNullOrWhiteSpace(TmdbApiKey) ? null : TmdbApiKey;
        
        await _settingsService.SaveAsync();
        StatusMessage = "Ayarlar kaydedildi";
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

    [ObservableProperty]
    private int _totalEpgPrograms;

    [ObservableProperty]
    private int _totalEpgChannels;

    [ObservableProperty]
    private DateTime? _channelListLastUpdated;

    [ObservableProperty]
    private DateTime? _lastEpgUpdate;

    [ObservableProperty]
    private string? _epgLastError;

    [RelayCommand]
    private async Task ScanEpgStatsAsync()
    {
        try 
        {
            StatusMessage = "İstatistikler okunuyor...";
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            TotalEpgPrograms = await db.EpgPrograms.CountAsync();
            TotalEpgChannels = await db.EpgPrograms
                .Select(p => p.ChannelId)
                .Distinct()
                .CountAsync();

            LastEpgUpdate = await db.Playlists
                .AsNoTracking()
                .Where(p => p.IsActive && p.EpgLastUpdated != null)
                .OrderByDescending(p => p.EpgLastUpdated)
                .Select(p => p.EpgLastUpdated)
                .FirstOrDefaultAsync();

            EpgLastError = _epgService.LastError;
            
            if (!string.IsNullOrEmpty(EpgLastError))
            {
                StatusMessage = "EPG hatası bulundu";
            }
            else
            {
                StatusMessage = "EPG istatistikleri güncellendi";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ScanChannelListStatsAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (_mainViewModel.SelectedPlaylist != null)
            {
                ChannelListLastUpdated = await db.Playlists
                    .AsNoTracking()
                    .Where(p => p.Id == _mainViewModel.SelectedPlaylist.Id)
                    .Select(p => p.LastUpdated)
                    .FirstOrDefaultAsync();
                return;
            }

            var profileId = _mainViewModel.CurrentProfile?.Id;
            if (profileId.HasValue)
            {
                ChannelListLastUpdated = await db.Playlists
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.ProfileId == profileId.Value)
                    .OrderByDescending(p => p.LastUpdated)
                    .Select(p => p.LastUpdated)
                    .FirstOrDefaultAsync();
            }
            else
            {
                ChannelListLastUpdated = null;
            }
        }
        catch
        {
            ChannelListLastUpdated = null;
        }
    }

    [RelayCommand]
    private Task ForceUpdateEpgAsync()
    {
        if (IsEpgLoading)
        {
            StatusMessage = "EPG zaten arka planda indiriliyor...";
            return Task.CompletedTask;
        }

        IsEpgLoading = true;
        StatusMessage = "EPG arka planda indiriliyor...";
        _ = ForceUpdateEpgInBackgroundAsync();
        return Task.CompletedTask;
    }

    private async Task ForceUpdateEpgInBackgroundAsync()
    {
        try
        {
            await _mainViewModel.ForceRefreshEpgAsync();
            await ScanEpgStatsAsync();
            StatusMessage = "EPG indirme tamamlandi (arka plan).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"EPG Indirme Hatasi: {ex.Message}";
        }
        finally
        {
            IsEpgLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshChannelListNowAsync()
    {
        try
        {
            StatusMessage = "Kanal listesi yenileniyor...";
            await _mainViewModel.RefreshSelectedPlaylistAsync();
            await ScanChannelListStatsAsync();
            StatusMessage = "Kanal listesi yenileme tamamlandı";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kanal listesi yenileme hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshEpgNowAsync()
    {
        try
        {
            StatusMessage = "EPG yenileniyor...";
            await _mainViewModel.ForceRefreshEpgAsync();
            await ScanEpgStatsAsync();
            StatusMessage = "EPG yenileme tamamlandı";
        }
        catch (Exception ex)
        {
            StatusMessage = $"EPG yenileme hatası: {ex.Message}";
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



