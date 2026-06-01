using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Collections.ObjectModel;

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
    private readonly IDialogService _dialogService;
    private readonly IWatchHistoryService _watchHistoryService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDiagnosticReportService _diagnosticService;
    private readonly ILicenseService _licenseService;
    private readonly IUpdateService _updateService;
    private readonly ILocalizationService _localizationService;
    private CancellationTokenSource? _epgRefreshWatchCts;
    private int _isRefreshOperationRunning;
    private string? _activeRefreshScope;

    // ============ Oynatma Ayarları ============
    [ObservableProperty]
    private string _userAgent = string.Empty;

    [ObservableProperty]
    private bool _autoPlayNext;

    [ObservableProperty]
    private bool _isBufferSmall;

    [ObservableProperty]
    private bool _isBufferNormal;

    [ObservableProperty]
    private bool _isBufferLarge;
    
    [ObservableProperty]
    private int _selectedDataUsage;

    [ObservableProperty]
    private bool _subtitleEnabled;

    [ObservableProperty]
    private string _subtitleLanguage = "en";

    [ObservableProperty]
    private int _subtitleFontSize = 40;

    [ObservableProperty]
    private string _preferredAudioLanguage = "en";
    
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
    private string _appLanguage = "en";

    [ObservableProperty]
    private int _channelListRefreshFrequencyHours;

    [ObservableProperty]
    private int _epgRefreshFrequencyHours;

    [ObservableProperty]
    private int _channelListRefreshFrequencyIndex;

    [ObservableProperty]
    private int _epgRefreshFrequencyIndex;

    [ObservableProperty]
    private int _epgTimeOffsetHours;

    [ObservableProperty]
    private int _epgTimeOffsetIndex;

    [ObservableProperty]
    private bool _epgEnabled;

    partial void OnChannelListRefreshFrequencyIndexChanged(int value)
    {
        ChannelListRefreshFrequencyHours = value switch
        {
            1 => 1,
            2 => 3,
            3 => 12,
            4 => 24,
            5 => 48,  // 2 gün
            6 => 72,  // 3 gün
            7 => 168, // 7 gün
            _ => 0
        };
    }

    partial void OnEpgRefreshFrequencyIndexChanged(int value)
    {
        EpgRefreshFrequencyHours = value switch
        {
            1 => 1,
            2 => 3,
            3 => 12,
            4 => 24,
            5 => 48,  // 2 gün
            6 => 72,  // 3 gün
            7 => 168, // 7 gün
            _ => 0
        };
    }

    partial void OnEpgTimeOffsetIndexChanged(int value)
    {
        EpgTimeOffsetHours = value - 12; // Index 12 is '0 (Otomatik)', so value 12 - 12 = 0
    }

    [ObservableProperty]
    private string _customEpgUrl = string.Empty;

    [ObservableProperty]
    private ObservableCollection<EpgUrlItem> _customEpgUrls = new();

    // ============ _localizationService.GetString("Settings.Privacy.Title") ============

    [ObservableProperty]
    private bool _saveWatchHistory;

    [ObservableProperty]
    private int _watchHistoryRetentionIndex;

    [ObservableProperty]
    private bool _clearHistoryOnExit;


    partial void OnIsDarkThemeChanged(bool value)
    {
        // Tema değiştiği an kaydet (user request)
        if (_settingsService != null && _settingsService.Settings.IsDarkTheme != value)
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
        IDialogService dialogService,
        IWatchHistoryService watchHistoryService,
        MainViewModel mainViewModel,
        IPlaylistService playlistService,
        IDbContextFactory<AppDbContext> contextFactory,
        IDiagnosticReportService diagnosticService,
        ILicenseService licenseService,
        IUpdateService updateService, ILocalizationService localizationService)
    {
        _settingsService = settingsService;
        _epgService = epgService;
        _themeService = themeService;
        _dialogService = dialogService;
        _watchHistoryService = watchHistoryService;
        _mainViewModel = mainViewModel;
        _playlistService = playlistService;
        _contextFactory = contextFactory;
        _diagnosticService = diagnosticService;
        _licenseService = licenseService;
        _updateService = updateService;
        _localizationService = localizationService;
        
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        
        ChannelListLastError = _mainViewModel.ChannelListLastError;
        
        LoadSettings();
        LoadProfileInfo();
        _ = ScanChannelListStatsCoreAsync(updateStatusMessage: false);
        _ = ScanEpgStatsCoreAsync(updateStatusMessage: false);
        _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();

        _ = _mainViewModel.RefreshCurrentProfileExpirationAsync();
    }

    public string CurrentVersion => _updateService.CurrentVersion;
    public bool IsPremium => _licenseService.IsPremium;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isCheckingUpdates;

    public bool IsIdle => !IsUpdateAvailable && !IsCheckingUpdates;

    private UpdateInfo? _latestUpdate;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates) return;

        IsCheckingUpdates = true;
        UpdateStatusText = _localizationService.GetString("Settings.Update.Checking");
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
                UpdateStatusText = string.Format(_localizationService.GetString("Settings.Update.NewVersionFormat"), update.Version);
            }
            else
            {
                UpdateStatusText = _localizationService.GetString("Settings.Update.UpToDate");
            }
        }
        catch (Exception)
        {
            UpdateStatusText = _localizationService.GetString("Settings.Update.CheckFailed");
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
            _localizationService.GetString("Settings.Update.Title"),
            string.Format(_localizationService.GetString("Settings.Update.ConfirmationFormat"), _latestUpdate.Version, _latestUpdate.Changelog)
        );

        if (confirmed)
        {
            await _updateService.StartUpdateAsync(_latestUpdate);
        }
    }

    [RelayCommand]
    private async Task ShowUpsell()
    {
        await _dialogService.ShowUpsellAsync();
    }

    [RelayCommand]
    private void ReportBug()
    {
        _diagnosticService.OpenBugReport();
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
        else if (e.PropertyName == nameof(MainViewModel.ChannelLoadingProgress) ||
                 e.PropertyName == nameof(MainViewModel.ChannelLoadingStats))
        {
            SyncChannelProgressFromMain();
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
                ProviderUsername = account.Username ?? _localizationService.GetString("Common.None");
                
                // Mask password
                var pass = account.Password;
                ProviderPassword = !string.IsNullOrEmpty(pass) ? new string('*', 10) : _localizationService.GetString("Common.None");
                
                ExpirationDate = account.ExpirationDate;
                
                if (ExpirationDate.HasValue)
                {
                    var daysLeft = (ExpirationDate.Value - DateTime.UtcNow).TotalDays;
                    if (daysLeft < 0) ExpirationStatus = _localizationService.GetString("Settings.Expiration.Expired");
                    else if (daysLeft < 7) ExpirationStatus = string.Format(_localizationService.GetString("Settings.Expiration.SoonFormat"), Math.Ceiling(daysLeft));
                    else ExpirationStatus = string.Format(_localizationService.GetString("Settings.Expiration.RemainingFormat"), Math.Ceiling(daysLeft));
                }
                else
                {
                    ExpirationStatus = _localizationService.GetString("Common.Unknown");
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

    [ObservableProperty]
    private ObservableCollection<string> _hiddenLiveGroups = new();

    [ObservableProperty]
    private ObservableCollection<string> _hiddenMovieGroups = new();

    [ObservableProperty]
    private ObservableCollection<string> _hiddenSeriesGroups = new();

    public int TotalHiddenGroupsCount => HiddenLiveGroups.Count + HiddenMovieGroups.Count + HiddenSeriesGroups.Count;

    [RelayCommand]
    private async Task UnhideGroupAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        var s = _settingsService.Settings;
        bool removed = false;
        
        if (s.HiddenLiveGroups.Remove(groupName))
        {
            HiddenLiveGroups.Remove(groupName);
            removed = true;
        }
        if (s.HiddenMovieGroups.Remove(groupName))
        {
            HiddenMovieGroups.Remove(groupName);
            removed = true;
        }
        if (s.HiddenSeriesGroups.Remove(groupName))
        {
            HiddenSeriesGroups.Remove(groupName);
            removed = true;
        }

        if (removed)
        {
            await _settingsService.SaveAsync();
            _mainViewModel.ScheduleImmediateFilter();
        }
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        
        // Playback
        UserAgent = s.UserAgent ?? string.Empty;
        AutoPlayNext = s.AutoPlayNext;
        
        IsBufferSmall = s.VideoBufferSize == BufferSize.Small;
        IsBufferNormal = s.VideoBufferSize == BufferSize.Normal;
        IsBufferLarge = s.VideoBufferSize == BufferSize.Large;

        SelectedDataUsage = (int)s.DataUsage;
        SubtitleEnabled = s.SubtitleEnabled;
        SubtitleLanguage = string.IsNullOrWhiteSpace(s.SubtitleLanguage) ? "en" : s.SubtitleLanguage;
        SubtitleFontSize = s.SubtitleFontSize;
        PreferredAudioLanguage = string.IsNullOrWhiteSpace(s.PreferredAudioLanguage) ? "en" : s.PreferredAudioLanguage;
        
        // Downloads
        SelectedDownloadQuality = (int)s.DownloadQuality;
        DownloadWifiOnly = s.DownloadWifiOnly;
        DownloadPath = NormalizeDownloadPath(s.DownloadPath);
        ShowDownloadNotification = s.ShowDownloadNotification;

        // Privacy
        SaveWatchHistory = s.SaveWatchHistory;
        ClearHistoryOnExit = s.ClearHistoryOnExit;
        WatchHistoryRetentionIndex = s.WatchHistoryRetentionDays switch
        {
            3 => 1,
            7 => 2,
            14 => 3,
            30 => 4,
            _ => 0
        };
        
        // Appearance
        IsDarkTheme = s.IsDarkTheme;
        AppLanguage = string.IsNullOrWhiteSpace(s.Language) ? "en" : s.Language;
        ChannelListRefreshFrequencyHours = s.ChannelListRefreshFrequencyHours;
        EpgRefreshFrequencyHours = s.EpgRefreshFrequencyHours;
        EpgEnabled = s.EpgEnabled;

        ChannelListRefreshFrequencyIndex = ChannelListRefreshFrequencyHours switch
        {
            1 => 1,
            3 => 2,
            12 => 3,
            24 => 4,
            48 => 5,
            72 => 6,
            168 => 7,
            _ => 0
        };

        EpgRefreshFrequencyIndex = EpgRefreshFrequencyHours switch
        {
            1 => 1,
            3 => 2,
            12 => 3,
            24 => 4,
            48 => 5,
            72 => 6,
            168 => 7,
            _ => 0
        };

        EpgTimeOffsetHours = s.EpgTimeOffsetHours;
        EpgTimeOffsetIndex = EpgTimeOffsetHours + 12;

        CustomEpgUrl = s.CustomEpgUrl ?? string.Empty;

        // Custom EPG URLs List (with Migration)
        var urls = s.CustomEpgUrls ?? new List<string>();
        if (urls.Count == 0 && !string.IsNullOrWhiteSpace(s.CustomEpgUrl))
        {
            urls = new List<string> { s.CustomEpgUrl };
        }
        CustomEpgUrls = new ObservableCollection<EpgUrlItem>(urls.Select(u => new EpgUrlItem { Url = u }));

        // Hidden Groups
        HiddenLiveGroups = new ObservableCollection<string>(s.HiddenLiveGroups);
        HiddenMovieGroups = new ObservableCollection<string>(s.HiddenMovieGroups);
        HiddenSeriesGroups = new ObservableCollection<string>(s.HiddenSeriesGroups);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var s = _settingsService.Settings;
        
        // Playback
        s.UserAgent = UserAgent?.Trim() ?? string.Empty;
        s.AutoPlayNext = AutoPlayNext;

        if (IsBufferSmall && IsPremium) s.VideoBufferSize = BufferSize.Small;
        else if (IsBufferLarge && IsPremium) s.VideoBufferSize = BufferSize.Large;
        else s.VideoBufferSize = BufferSize.Normal;

        s.DataUsage = (DataUsageLevel)SelectedDataUsage;
        s.SubtitleEnabled = SubtitleEnabled;
        s.SubtitleLanguage = SubtitleLanguage;
        s.SubtitleFontSize = SubtitleFontSize;
        s.PreferredAudioLanguage = PreferredAudioLanguage;
        
        // Downloads
        s.DownloadQuality = (DownloadQuality)SelectedDownloadQuality;
        s.DownloadWifiOnly = DownloadWifiOnly;
        DownloadPath = NormalizeDownloadPath(DownloadPath);
        s.DownloadPath = DownloadPath;
        s.ShowDownloadNotification = ShowDownloadNotification;
        
        // Appearance
        s.IsDarkTheme = IsDarkTheme;
        s.Language = string.IsNullOrWhiteSpace(AppLanguage) ? "en" : AppLanguage;
        s.ChannelListRefreshFrequencyHours = Math.Max(0, ChannelListRefreshFrequencyHours);
        s.EpgRefreshFrequencyHours = Math.Max(0, EpgRefreshFrequencyHours);
        s.CustomEpgUrl = string.IsNullOrWhiteSpace(CustomEpgUrl) ? null : CustomEpgUrl.Trim();
        s.CustomEpgUrls = CustomEpgUrls.Where(u => !string.IsNullOrWhiteSpace(u.Url)).Select(u => u.Url.Trim()).ToList();
        s.EpgEnabled = EpgEnabled;
        s.EpgTimeOffsetHours = EpgTimeOffsetHours;

        // Privacy
        s.SaveWatchHistory = SaveWatchHistory;
        s.ClearHistoryOnExit = ClearHistoryOnExit;
        s.WatchHistoryRetentionDays = WatchHistoryRetentionIndex switch
        {
            1 => 3,
            2 => 7,
            3 => 14,
            4 => 30,
            _ => 0
        };
        
        await _settingsService.SaveAsync();
        StatusMessage = _localizationService.GetString("Settings.Status.Saved");
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
    private void AddCustomEpg()
    {
        if (CustomEpgUrls.Count >= AppSettings.EPG_URL_LIMIT)
        {
            StatusMessage = string.Format(_localizationService.GetString("Settings.Error.EpgLimitFormat"), AppSettings.EPG_URL_LIMIT);
            return;
        }

        if (!IsPremium && CustomEpgUrls.Count >= AppSettings.EPG_URL_FREE_LIMIT)
        {
            StatusMessage = string.Format(_localizationService.GetString("Settings.Error.EpgFreeLimitFormat"), AppSettings.EPG_URL_FREE_LIMIT);
            return;
        }

        CustomEpgUrls.Add(new EpgUrlItem());
    }

    [RelayCommand]
    private void RemoveCustomEpg(EpgUrlItem item)
    {
        CustomEpgUrls.Remove(item);
    }

    [RelayCommand]
    private async Task ScanEpgStatsAsync()
        => await ScanEpgStatsCoreAsync(updateStatusMessage: true);

    private async Task ScanEpgStatsCoreAsync(bool updateStatusMessage)
    {
        try 
        {
            if (updateStatusMessage)
            {
                StatusMessage = _localizationService.GetString("Settings.Status.EpgReading");
            }

            using var db = await _contextFactory.CreateDbContextAsync();

            var profileId = _mainViewModel.CurrentProfile?.Id;
            if (profileId.HasValue)
            {
                var activeChannels = await db.Channels
                    .AsNoTracking()
                    .Where(c => c.Playlist != null && c.Playlist.ProfileId == profileId.Value && c.Playlist.IsActive)
                    .Select(c => new { c.Id, c.TvgId })
                    .ToListAsync();

                var searchIds = activeChannels
                    .Select(c => c.TvgId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Concat(activeChannels.Select(c => c.Id.ToString()))
                    .Distinct()
                    .ToList();

                TotalEpgPrograms = await db.EpgPrograms
                    .CountAsync(p => searchIds.Contains(p.ChannelId));

                TotalEpgChannels = await db.EpgPrograms
                    .Where(p => searchIds.Contains(p.ChannelId))
                    .Select(p => p.ChannelId)
                    .Distinct()
                    .CountAsync();

                LastEpgUpdate = await db.Playlists
                    .AsNoTracking()
                    .Where(p => p.ProfileId == profileId.Value && p.IsActive && p.EpgLastUpdated != null)
                    .OrderByDescending(p => p.EpgLastUpdated)
                    .Select(p => p.EpgLastUpdated)
                    .FirstOrDefaultAsync();
            }
            else
            {
                TotalEpgPrograms = 0;
                TotalEpgChannels = 0;
                LastEpgUpdate = null;
            }

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
                var isWarning = EpgLastError.Contains("eşleşen yayın bilgisi bulunamadı") || EpgLastError.Contains("0 program");
                if (isWarning)
                {
                    StatusMessage = _localizationService.GetString("Settings.Status.EpgUpdated");
                }
                else
                {
                    StatusMessage = _localizationService.GetString("Settings.Status.EpgError");
                }
            }
            else if (updateStatusMessage)
            {
                StatusMessage = _localizationService.GetString("Settings.Status.EpgUpdated");
            }
        }
        catch (Exception ex)
        {
            if (updateStatusMessage)
            {
                StatusMessage = string.Format(_localizationService.GetString("Settings.Status.Stats.ErrorFormat"), UserFriendlyErrorMessage.FromException(ex));
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
                StatusMessage = _localizationService.GetString("Settings.Status.ChannelsReading");
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
                        .CountAsync(c => c.Playlist != null && c.Playlist.ProfileId == profileId.Value && c.Playlist.IsActive);
                }
                else
                {
                    ChannelListLastUpdated = null;
                    TotalChannels = 0;
                }
            }

            if (updateStatusMessage)
            {
                StatusMessage = _localizationService.GetString("Settings.Status.ChannelsUpdated");
            }
        }
        catch
        {
            ChannelListLastUpdated = null;
            if (updateStatusMessage)
            {
                StatusMessage = _localizationService.GetString("Settings.Status.Channel.Error");
            }
        }
    }

    [RelayCommand]
    private async Task RefreshChannelListNowAsync()
    {
        if (!TryBeginRefreshOperation(_localizationService.GetString("Settings.Refresh.Channel.Started"), "Channel"))
        {
            return;
        }

        try
        {
            _mainViewModel.IsGlobalLoading = true;
            _mainViewModel.GlobalLoadingMessage = _localizationService.GetString("Settings.Refresh.Channel.Started");

            SetProgressStatus("Channel", 0, _localizationService.GetString("Settings.Refresh.Channel.Started"));
            await _mainViewModel.RefreshSelectedPlaylistAsync();
            SyncChannelProgressFromMain(minimumPercent: 88, fallbackMessage: _localizationService.GetString("Settings.Refresh.Channel.UpdatingData"));

            SetProgressStatus("Channel", Math.Max(RefreshProgressPercent, 90), _localizationService.GetString("Settings.Refresh.Channel.UpdatingData"));
            _mainViewModel.GlobalLoadingMessage = _localizationService.GetString("Settings.Refresh.Channel.UpdatingData");
            await ScanChannelListStatsCoreAsync(updateStatusMessage: false);

            // Kanal listesi yenilenirken bitiş süresini de güncelle
            SetProgressStatus("Channel", Math.Max(RefreshProgressPercent, 95), _localizationService.GetString("Settings.Refresh.Channel.CheckingAccount"));
            _mainViewModel.GlobalLoadingMessage = _localizationService.GetString("Settings.Refresh.Channel.CheckingAccount");
            await _mainViewModel.RefreshCurrentProfileExpirationAsync();
            LoadProfileInfo();

            if (!string.IsNullOrWhiteSpace(ChannelListLastError))
            {
                SetProgressStatus("Channel", 100, ChannelListLastError);
            }
            else
            {
                // Ekstra kontrol: Eğer error yok ama kanal sayısı hala 0 ise uyar
                var afterCount = await _playlistService.GetChannelCountAsync(_mainViewModel.SelectedPlaylist?.Id ?? 0);
                if (afterCount == 0)
                {
                    SetProgressStatus("Channel", 100, _localizationService.GetString("Settings.Refresh.Channel.Warning.NoContent"));
                }
                else
                {
                    SetProgressStatus("Channel", 100, _localizationService.GetString("Settings.Refresh.Channel.Completed"));
                }
            }
        }
        catch (Exception ex)
        {
            ChannelListLastError = UserFriendlyErrorMessage.FromException(ex);
            SetProgressStatus("Channel", RefreshProgressPercent, UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Settings.Refresh.Channel.Error"), ex));
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
        // Önce ayarları kaydet ki arka plan görevi yeni URL'yi görebilsin
        await SaveSettingsAsync();

        if (!TryBeginRefreshOperation(_localizationService.GetString("Settings.Refresh.Epg.Started"), "EPG"))
        {
            return;
        }

        SetProgressStatus("EPG", 8, _localizationService.GetString("Settings.Refresh.Epg.Started"));
        var started = _mainViewModel.ForceRefreshEpgInBackground();
        if (!started)
        {
            SetProgressStatus("EPG", 8, _localizationService.GetString("Settings.Refresh.Epg.InProgress"));
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
                            SetProgressStatus("EPG", 100, _localizationService.GetString("Settings.Refresh.Epg.Completed"));
                            return;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(EpgLastError))
                    {
                        var isWarning = EpgLastError.Contains("eşleşen yayın bilgisi bulunamadı") || EpgLastError.Contains("0 program");
                        
                        if (isWarning)
                        {
                            SetProgressStatus("EPG", 100, _localizationService.GetString("Settings.Refresh.Epg.Completed"));
                        }
                        else
                        {
                            SetProgressStatus(
                                "EPG",
                                100,
                                string.Format(_localizationService.GetString("Settings.Refresh.Epg.ErrorFormat"), EpgLastError));
                        }
                        return;
                    }
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    SetProgressStatus("EPG", RefreshProgressPercent, UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Settings.Refresh.Epg.WatchingError"), ex));
                    return;
                }
            }

            if (DateTime.UtcNow >= timeoutAt)
            {
                SetProgressStatus("EPG", RefreshProgressPercent, _localizationService.GetString("Settings.Refresh.Epg.Timeout"));
            }
        }
        finally
        {
            EndRefreshOperation();
        }
    }

    private bool TryBeginRefreshOperation(string operationLabel, string scope)
    {
        if (Interlocked.Exchange(ref _isRefreshOperationRunning, 1) == 1)
        {
            StatusMessage = _localizationService.GetString("Settings.Refresh.OperationInProgress");
            return false;
        }

        _activeRefreshScope = scope;
        RefreshProgressPercent = 0;
        ChannelListLastError = null;
        StatusMessage = string.Format(_localizationService.GetString("Settings.Status.Refresh.Label"), operationLabel);
        return true;
    }

    private void EndRefreshOperation()
    {
        _activeRefreshScope = null;
        Interlocked.Exchange(ref _isRefreshOperationRunning, 0);
    }

    private void SyncChannelProgressFromMain(int minimumPercent = 0, string? fallbackMessage = null)
    {
        if (Volatile.Read(ref _isRefreshOperationRunning) != 1 ||
            !string.Equals(_activeRefreshScope, "Channel", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var percent = Math.Max(minimumPercent, (int)Math.Round(_mainViewModel.ChannelLoadingProgress));
        var message = !string.IsNullOrWhiteSpace(_mainViewModel.ChannelLoadingStats)
            ? _mainViewModel.ChannelLoadingStats
            : !string.IsNullOrWhiteSpace(_mainViewModel.StatusMessage)
                ? _mainViewModel.StatusMessage
                : fallbackMessage ?? _localizationService.GetString("Settings.Refresh.Channel.Started");

        SetProgressStatus("Channel", percent, message, updateMainStatus: false);
    }

    private void SetProgressStatus(string scope, int percent, string message, bool updateMainStatus = true)
    {
        var normalized = Math.Clamp(percent, 0, 100);
        var localizedScope = scope.Equals("Channel", StringComparison.OrdinalIgnoreCase)
            ? _localizationService.GetString("Settings.Refresh.Scope.Channel")
            : scope.Equals("EPG", StringComparison.OrdinalIgnoreCase)
                ? _localizationService.GetString("Settings.Refresh.Scope.Epg")
                : scope;

        RefreshProgressPercent = normalized;
        StatusMessage = string.Format(
            CultureInfo.CurrentCulture,
            _localizationService.GetString("Settings.Status.ProgressFormat"),
            localizedScope,
            message,
            normalized);
        
        // Settings penceresi kapatılsa bile ana pencerenin sol altındaki bar güncellenmeye devam etsin
        if (updateMainStatus &&
            (scope.Equals("EPG", StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("Channel", StringComparison.OrdinalIgnoreCase)))
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
        StatusMessage = _localizationService.GetString("Settings.Status.Reset");
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var profileId = _mainViewModel.CurrentProfile?.Id;
        if (!profileId.HasValue) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("Settings.Privacy.Clear.Title"),
            _localizationService.GetString("Settings.Privacy.Clear.Confirm"));

        if (confirmed)
        {
            try
            {
                await _watchHistoryService.DeleteProfileHistoryAsync(profileId.Value);
                StatusMessage = _localizationService.GetString("Settings.Privacy.Clear.Success");
                _mainViewModel.ResetWatchHistoryUI();
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(_localizationService.GetString("Common.ErrorFormat"), ex.Message);
            }
        }
    }
}

/// <summary>
/// Özel EPG URL öğesi
/// </summary>
public partial class EpgUrlItem : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;
}
