using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Noctra.ViewModels;

/// <summary>
/// Ayarlar view model
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;
    private readonly ISettingsService _settingsService;
    private readonly IEpgService _epgService;
    private readonly IThemeService _themeService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private CancellationTokenSource? _epgRefreshWatchCts;
    private int _isRefreshOperationRunning;

    // ============ Oynatma Ayarları ============
    
    [ObservableProperty]
    private bool _autoPlayNext;
    
    [ObservableProperty]
    private int _selectedDataUsage;

    [ObservableProperty]
    private bool _subtitleEnabled;

    [ObservableProperty]
    private string _subtitleLanguage = "tr";

    [ObservableProperty]
    private string _preferredAudioLanguage = "tr";
    
    // ============ İndirme Ayarları ============
    
    [ObservableProperty]
    private int _selectedDownloadQuality;
    
    [ObservableProperty]
    private bool _downloadWifiOnly;
    
    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _showDownloadNotification;
    
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
    private int _channelListRefreshFrequencyIndex;

    [ObservableProperty]
    private int _epgRefreshFrequencyIndex;

    [ObservableProperty]
    private bool _epgEnabled;

    partial void OnChannelListRefreshFrequencyIndexChanged(int value)
    {
        ChannelListRefreshFrequencyHours = value switch
        {
            1 => 1,
            2 => 3,
            3 => 6,
            4 => 12,
            5 => 24,
            _ => 0
        };
    }

    partial void OnEpgRefreshFrequencyIndexChanged(int value)
    {
        EpgRefreshFrequencyHours = value switch
        {
            1 => 1,
            2 => 3,
            3 => 6,
            4 => 12,
            5 => 24,
            _ => 0
        };
    }

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
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _refreshProgressPercent;
    

    public bool IsGlobalLoading => _mainViewModel.IsGlobalLoading;
    public string GlobalLoadingMessage => _mainViewModel.GlobalLoadingMessage;

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
        IEpgService epgService, 
        IThemeService themeService,
        MainViewModel mainViewModel,
        IPlaylistService playlistService,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        _settingsService = settingsService;
        _epgService = epgService;
        _themeService = themeService;
        _mainViewModel = mainViewModel;
        _playlistService = playlistService;
        _contextFactory = contextFactory;
        
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        
        ChannelListLastError = _mainViewModel.ChannelListLastError;
        
        LoadSettings();
        LoadProfileInfo();
        _ = ScanChannelListStatsCoreAsync(updateStatusMessage: false);
        _ = ScanEpgStatsCoreAsync(updateStatusMessage: false);
        _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
    }

    private void OnSettingsService_Changed()
    {
        LoadSettings();
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentProfile))
        {
            LoadProfileInfo();
            _ = ScanChannelListStatsCoreAsync(updateStatusMessage: false);
            _ = ScanEpgStatsCoreAsync(updateStatusMessage: false);
            _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedPlaylist))
        {
            _ = ScanChannelListStatsCoreAsync(updateStatusMessage: false);
        }
        else if (e.PropertyName == nameof(MainViewModel.ChannelListLastError))
        {
            ChannelListLastError = _mainViewModel.ChannelListLastError;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsGlobalLoading))
        {
            OnPropertyChanged(nameof(IsGlobalLoading));
        }
        else if (e.PropertyName == nameof(MainViewModel.GlobalLoadingMessage))
        {
            OnPropertyChanged(nameof(GlobalLoadingMessage));
        }
    }

    private void LoadProfileInfo()
    {
        if (_mainViewModel.CurrentProfile != null)
        {
            CurrentProfileName = _mainViewModel.CurrentProfile.Name;
            CurrentProfileAvatar = _mainViewModel.CurrentProfile.Avatar;
            ProfileCreatedAt = _mainViewModel.CurrentProfile.CreatedAt;
            
            // If CreatedAt is default (min value), set it to Now for display or handle it
            if (ProfileCreatedAt == DateTime.MinValue) ProfileCreatedAt = DateTime.UtcNow;

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
                    var daysLeft = (ExpirationDate.Value - DateTime.UtcNow).TotalDays;
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
        SelectedDataUsage = (int)s.DataUsage;
        SubtitleEnabled = s.SubtitleEnabled;
        SubtitleLanguage = string.IsNullOrWhiteSpace(s.SubtitleLanguage) ? "tr" : s.SubtitleLanguage;
        PreferredAudioLanguage = string.IsNullOrWhiteSpace(s.PreferredAudioLanguage) ? "tr" : s.PreferredAudioLanguage;
        
        // Downloads
        SelectedDownloadQuality = (int)s.DownloadQuality;
        DownloadWifiOnly = s.DownloadWifiOnly;
        DownloadPath = NormalizeDownloadPath(s.DownloadPath);
        ShowDownloadNotification = s.ShowDownloadNotification;
        
        // Appearance
        IsDarkTheme = s.IsDarkTheme;
        AppLanguage = string.IsNullOrWhiteSpace(s.Language) ? "tr" : s.Language;
        ChannelListRefreshFrequencyHours = s.ChannelListRefreshFrequencyHours;
        EpgRefreshFrequencyHours = s.EpgRefreshFrequencyHours;
        EpgEnabled = s.EpgEnabled;

        ChannelListRefreshFrequencyIndex = ChannelListRefreshFrequencyHours switch
        {
            1 => 1,
            3 => 2,
            6 => 3,
            12 => 4,
            24 => 5,
            _ => 0
        };

        EpgRefreshFrequencyIndex = EpgRefreshFrequencyHours switch
        {
            1 => 1,
            3 => 2,
            6 => 3,
            12 => 4,
            24 => 5,
            _ => 0
        };

        CustomEpgUrl = s.CustomEpgUrl ?? string.Empty;
        
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var s = _settingsService.Settings;
        
        // Playback
        s.AutoPlayNext = AutoPlayNext;
        s.DataUsage = (DataUsageLevel)SelectedDataUsage;
        s.SubtitleEnabled = SubtitleEnabled;
        s.SubtitleLanguage = SubtitleLanguage;
        s.PreferredAudioLanguage = PreferredAudioLanguage;
        
        // Downloads
        s.DownloadQuality = (DownloadQuality)SelectedDownloadQuality;
        s.DownloadWifiOnly = DownloadWifiOnly;
        DownloadPath = NormalizeDownloadPath(DownloadPath);
        s.DownloadPath = DownloadPath;
        s.ShowDownloadNotification = ShowDownloadNotification;
        
        // Appearance
        s.IsDarkTheme = IsDarkTheme;
        s.Language = string.IsNullOrWhiteSpace(AppLanguage) ? "tr" : AppLanguage;
        s.ChannelListRefreshFrequencyHours = Math.Max(0, ChannelListRefreshFrequencyHours);
        s.EpgRefreshFrequencyHours = Math.Max(0, EpgRefreshFrequencyHours);
        s.CustomEpgUrl = string.IsNullOrWhiteSpace(CustomEpgUrl) ? null : CustomEpgUrl.Trim();
        s.EpgEnabled = EpgEnabled;
        
        
        await _settingsService.SaveAsync();
        StatusMessage = "Ayarlar kaydedildi";
    }

    [ObservableProperty]
    private int _totalChannels;

    [ObservableProperty]
    private int _totalEpgPrograms;

    [ObservableProperty]
    private int _totalEpgChannels;

    [ObservableProperty]
    private DateTime? _channelListLastUpdated;

    [ObservableProperty]
    private string? _channelListLastError;

    [ObservableProperty]
    private DateTime? _lastEpgUpdate;

    [ObservableProperty]
    private string? _epgLastError;

    [RelayCommand]
    private async Task ScanEpgStatsAsync()
        => await ScanEpgStatsCoreAsync(updateStatusMessage: true);

    private async Task ScanEpgStatsCoreAsync(bool updateStatusMessage)
    {
        try 
        {
            if (updateStatusMessage)
            {
                StatusMessage = "[Istatistik] EPG verileri okunuyor...";
            }

            using var db = await _contextFactory.CreateDbContextAsync();

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

            var profileId = _mainViewModel.CurrentProfile?.Id;
            var errorQuery = db.Playlists.AsNoTracking().Where(p => p.IsActive && !string.IsNullOrWhiteSpace(p.EpgLastError));
            if (profileId.HasValue)
            {
                errorQuery = errorQuery.Where(p => p.ProfileId == profileId.Value);
            }

            EpgLastError = await errorQuery
                .OrderByDescending(p => p.EpgLastUpdated ?? p.LastUpdated ?? p.CreatedAt)
                .Select(p => p.EpgLastError)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(EpgLastError))
            {
                EpgLastError = _epgService.LastError;
            }

            if (!string.IsNullOrWhiteSpace(EpgLastError))
            {
                EpgLastError = UserFriendlyErrorMessage.FromText(EpgLastError);
            }
            
            if (updateStatusMessage && !string.IsNullOrEmpty(EpgLastError))
            {
                StatusMessage = "[Istatistik] EPG hatasi bulundu";
            }
            else if (updateStatusMessage)
            {
                StatusMessage = "[Istatistik] EPG istatistikleri guncellendi";
            }
        }
        catch (Exception ex)
        {
            if (updateStatusMessage)
            {
                StatusMessage = $"[Istatistik] {UserFriendlyErrorMessage.FromException(ex)}";
            }
        }
    }

    [RelayCommand]
    private async Task ScanChannelListStatsAsync()
        => await ScanChannelListStatsCoreAsync(updateStatusMessage: true);

    private async Task ScanChannelListStatsCoreAsync(bool updateStatusMessage)
    {
        try
        {
            if (updateStatusMessage)
            {
                StatusMessage = "[İstatistik] Kanal listesi verileri okunuyor...";
            }

            using var db = await _contextFactory.CreateDbContextAsync();

            if (_mainViewModel.SelectedPlaylist != null)
            {
                ChannelListLastUpdated = await db.Playlists
                    .AsNoTracking()
                    .Where(p => p.Id == _mainViewModel.SelectedPlaylist.Id)
                    .Select(p => p.LastUpdated)
                    .FirstOrDefaultAsync();

                TotalChannels = await db.Channels
                    .CountAsync(c => c.PlaylistId == _mainViewModel.SelectedPlaylist.Id);
            }
            else
            {
                var profileId = _mainViewModel.CurrentProfile?.Id;
                if (profileId.HasValue)
                {
                    ChannelListLastUpdated = await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.IsActive && p.ProfileId == profileId.Value)
                        .OrderByDescending(p => p.LastUpdated)
                        .Select(p => p.LastUpdated)
                        .FirstOrDefaultAsync();

                    TotalChannels = await db.Channels
                        .CountAsync(c => c.Playlist.ProfileId == profileId.Value && c.Playlist.IsActive);
                }
                else
                {
                    ChannelListLastUpdated = null;
                    TotalChannels = 0;
                }
            }

            if (updateStatusMessage)
            {
                StatusMessage = "[İstatistik] Kanal listesi istatistikleri güncellendi";
            }
        }
        catch
        {
            ChannelListLastUpdated = null;
            if (updateStatusMessage)
            {
                StatusMessage = "[İstatistik] Kanal listesi istatistikleri okunamadı";
            }
        }
    }

    [RelayCommand]
    private async Task RefreshChannelListNowAsync()
    {
        if (!TryBeginRefreshOperation("Kanal listesi yenileniyor"))
        {
            return;
        }

        try
        {
            _mainViewModel.IsGlobalLoading = true;
            _mainViewModel.GlobalLoadingMessage = "Kanal listesi yenileniyor...";

            SetProgressStatus("Kanal", 12, "Kanal listesi yenileniyor...");
            await _mainViewModel.RefreshSelectedPlaylistAsync();

            SetProgressStatus("Kanal", 60, "Kanal listesi verileri güncelleniyor...");
            _mainViewModel.GlobalLoadingMessage = "Kanal listesi verileri güncelleniyor...";
            await ScanChannelListStatsCoreAsync(updateStatusMessage: false);

            // Kanal listesi yenilenirken bitiş süresini de güncelle
            SetProgressStatus("Kanal", 80, "Hesap bilgileri kontrol ediliyor...");
            _mainViewModel.GlobalLoadingMessage = "Hesap bilgileri kontrol ediliyor...";
            await _mainViewModel.RefreshCurrentProfileExpirationAsync();
            LoadProfileInfo();

            if (!string.IsNullOrWhiteSpace(ChannelListLastError))
            {
                SetProgressStatus("Kanal", 100, ChannelListLastError);
            }
            else
            {
                // Ekstra kontrol: Eğer error yok ama kanal sayısı hala 0 ise uyar
                var afterCount = await _playlistService.GetChannelCountAsync(_mainViewModel.SelectedPlaylist?.Id ?? 0);
                if (afterCount == 0)
                {
                    SetProgressStatus("Kanal", 100, "Uyarı: Liste indirildi ancak içerik bulunamadı (0 kanal)");
                }
                else
                {
                    SetProgressStatus("Kanal", 100, "Kanal listesi yenileme tamamlandı");
                }
            }
        }
        catch (Exception ex)
        {
            ChannelListLastError = UserFriendlyErrorMessage.FromException(ex);
            SetProgressStatus("Kanal", RefreshProgressPercent, UserFriendlyErrorMessage.WithPrefix("Kanal listesi yenileme hatası", ex));
        }
        finally
        {
            _mainViewModel.IsGlobalLoading = false;
            _mainViewModel.GlobalLoadingMessage = string.Empty;
            EndRefreshOperation();
        }
    }

    [RelayCommand]
    private async Task RefreshEpgNowAsync()
    {
        if (!TryBeginRefreshOperation("EPG yenileme başlatılıyor"))
        {
            return;
        }

        SetProgressStatus("EPG", 8, "EPG yenileme arka planda başlatıldı...");
        var started = _mainViewModel.ForceRefreshEpgInBackground();
        if (!started)
        {
            SetProgressStatus("EPG", 8, "Başka bir yenileme işlemi zaten devam ediyor...");
            EndRefreshOperation();
            return;
        }

        EpgLastError = null;
        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var pId = _mainViewModel.CurrentProfile?.Id;
            var activePlaylists = await db.Playlists.Where(p => p.IsActive && p.ProfileId == pId).ToListAsync();
            foreach (var p in activePlaylists)
            {
                p.EpgLastError = null;
            }
            await db.SaveChangesAsync();
        }
        catch { }

        _epgRefreshWatchCts?.Cancel();
        _epgRefreshWatchCts?.Dispose();
        _epgRefreshWatchCts = new CancellationTokenSource();

        _ = WatchEpgRefreshOutcomeAsync(_epgRefreshWatchCts.Token);
    }

    private async Task WatchEpgRefreshOutcomeAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow.AddSeconds(-2);
        var timeoutAt = DateTime.UtcNow.AddMinutes(10); // Match EpgService timeout

        try
        {
            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < timeoutAt)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                    
                    // MainViewModel'daki progres verisini yakala
                    var currentProgress = _mainViewModel.EpgProgress;
                    if (currentProgress != null)
                    {
                        var percent = (int)currentProgress.ProgressPercent;
                        SetProgressStatus("EPG", percent, currentProgress.Message);
                    }
                    else 
                    {
                        // Progress nesnesi yoksa istatistik taramaya devam et (fall-back)
                        await ScanEpgStatsCoreAsync(updateStatusMessage: false);
                        
                        if (LastEpgUpdate.HasValue && LastEpgUpdate.Value >= startedAt)
                        {
                            SetProgressStatus("EPG", 100, "EPG yenileme tamamlandı");
                            return;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(EpgLastError))
                    {
                        SetProgressStatus(
                            "EPG",
                            RefreshProgressPercent,
                            $"EPG yenileme hatası: {EpgLastError}");
                        return;
                    }
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    SetProgressStatus("EPG", RefreshProgressPercent, UserFriendlyErrorMessage.WithPrefix("EPG izleme hatası", ex));
                    return;
                }
            }

            if (DateTime.UtcNow >= timeoutAt)
            {
                SetProgressStatus("EPG", RefreshProgressPercent, "EPG yenileme zaman aşımına uğradı (10 dk)");
            }
        }
        finally
        {
            EndRefreshOperation();
        }
    }

    private bool TryBeginRefreshOperation(string operationLabel)
    {
        if (Interlocked.Exchange(ref _isRefreshOperationRunning, 1) == 1)
        {
            StatusMessage = $"[Yenileme] Başka bir işlem devam ediyor. Önce mevcut yenilemenin bitmesini bekleyin.";
            return false;
        }

        RefreshProgressPercent = 0;
        ChannelListLastError = null;
        StatusMessage = $"[Yenileme] {operationLabel}";
        return true;
    }

    private void EndRefreshOperation()
    {
        Interlocked.Exchange(ref _isRefreshOperationRunning, 0);
    }

    private void SetProgressStatus(string scope, int percent, string message)
    {
        var normalized = Math.Clamp(percent, 0, 100);
        RefreshProgressPercent = normalized;
        StatusMessage = $"[{scope}] {message} (%{normalized})";
        
        // Settings penceresi kapatılsa bile ana pencerenin sol altındaki bar güncellenmeye devam etsin
        if (scope == "EPG" || scope == "Kanal")
        {
            _mainViewModel.StatusMessage = StatusMessage;
        }
    }

    private static string NormalizeDownloadPath(string? rawPath)
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra",
            "Downloads");

        var candidate = string.IsNullOrWhiteSpace(rawPath)
            ? fallback
            : rawPath.Trim().Trim('"');

        try
        {
            var full = Path.GetFullPath(candidate);
            Directory.CreateDirectory(full);
            return full;
        }
        catch
        {
            Directory.CreateDirectory(fallback);
            return Path.GetFullPath(fallback);
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
        StatusMessage = "Ayarlar varsayılanına sıfırlandı";
    }
}
