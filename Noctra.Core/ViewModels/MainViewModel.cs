using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using Noctra.Services.Interfaces;
using Noctra.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;
using System.Collections.ObjectModel;
using Noctra.Core.Services;
using System.Diagnostics;
using System.Collections.Concurrent;
using Noctra.Core.Collections;
using System.Runtime.CompilerServices;

namespace Noctra.ViewModels;

public enum AppView
{
    Home,
    Live,
    Movies,
    Series,
    Search,
    MyList,
    Favorites,
    History,
    Downloads
}

public enum DownloadSortOrder
{
    Latest,
    NameAZ,
    SizeLarge
}

/// <summary>
/// Ana sayfa view model
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const int IncrementalPageSize = 30;
    private const double LoadMoreThreshold = 0.8;

    private readonly IDispatcherService _dispatcherService;
    private readonly ISettingsService _settingsService;
    private readonly IMetadataService _metadataService;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<MainViewModel>? _logger;
    private readonly ISecurityService _securityService;
    private readonly IChannelService _channelService;
    private readonly IMediaService _mediaService;
    private readonly IEpgService _epgService;
    private readonly IPlaylistService _playlistService;
    private readonly IWatchHistoryService _watchHistoryService;
    private readonly IXtreamCodesService _xtreamCodesService;
    private readonly IStalkerPortalService _stalkerPortalService;
    private readonly LanguageDetectionService _languageDetectionService;
    private readonly EpgSourceResolver _epgSourceResolver;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _httpClient;
    private readonly ITmdbSyncService _tmdbSyncService;
    private readonly ILicenseService _licenseService;
    private readonly IUpdateService _updateService;
    private readonly IPerformanceTraceService? _perfTrace;
    private readonly DateTime _downloadCenterSessionStartUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, byte> _pendingVisualEnrichmentKeys = new(StringComparer.OrdinalIgnoreCase);
    private long _downloadLandingStoredBytes;
    private readonly SemaphoreSlim _channelVisualEnrichmentSemaphore = new(3, 3);
    private readonly SemaphoreSlim _seriesVisualEnrichmentSemaphore = new(3, 3);
    private const int ProviderImageFallbackAttempts = 2;
    private const int ProviderImageFallbackDelayMs = 250;
    private CancellationTokenSource? _slowLoadingWarnCts;
    private readonly ILocalizationService _localizationService;
    private bool _suppressNavigationFilterRefresh;
    private int _navigationTraceId;

    // Guards media-selection flows before they reach MainWindow playback.
    // This prevents rapid Live/VOD/Series clicks from completing out of order
    // after an async URL/context lookup and starting an older item.
    private int _mediaSelectionVersion;

    private int BeginMediaSelectionIntent()
        => Interlocked.Increment(ref _mediaSelectionVersion);

    private bool IsMediaSelectionIntentCurrent(int selectionVersion)
        => selectionVersion == Volatile.Read(ref _mediaSelectionVersion);

    [ObservableProperty]
    private AppView _activeView = AppView.Home;

    [ObservableProperty]
    private BatchObservableCollection<Channel> _continueWatching = new();

    private List<Series> _allSeriesCache = new();

    [ObservableProperty]
    private BatchObservableCollection<Series> _seriesViewItems = new();


    [ObservableProperty]
    private BatchObservableCollection<Playlist> _playlists = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _channels = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _filteredChannels = new(c => !IsDummyChannel(c));

    [ObservableProperty]
    private BatchObservableCollection<string> _groups = new();

    private int _historyPage = 0;
    private bool _hasMoreHistory = true;
    private bool _isLoadingMoreHistory = false;

    [ObservableProperty]
    private Playlist? _selectedPlaylist;

    [ObservableProperty]
    private Channel? _selectedChannel;

    [ObservableProperty]
    private Series? _selectedSeries;

    [ObservableProperty]
    private bool _isSeriesDetailVisible;

    public Episode? CurrentEpisodePlaybackContext { get; private set; }
    public Episode? NextEpisodePlaybackContext { get; private set; }
    public Series? CurrentSeriesPlaybackContext { get; private set; }

    [ObservableProperty]
    private string? _selectedSeriesPosterUrl;

    [ObservableProperty]
    private string? _selectedSeriesBackdropUrl;

    [ObservableProperty]
    private string? _selectedSeriesTrailerUrl;

    [ObservableProperty]
    private string? _selectedSeriesNetworkLogoUrl;

    [ObservableProperty]
    private string _selectedSeriesOverview = string.Empty;

    [ObservableProperty]
    private string _selectedSeriesCast = string.Empty;

    [ObservableProperty]
    private string _selectedSeriesYears = string.Empty;

    [ObservableProperty]
    private int _selectedSeriesTotalEpisodesCount;

    [ObservableProperty]
    private int _selectedSeriesTotalSeasonsCount;

    [ObservableProperty]
    private string _selectedSeriesAgeRating = string.Empty;

    [ObservableProperty]
    private string _selectedSeriesGenres = string.Empty;

    [ObservableProperty]
    private bool _selectedSeriesIsHd = true;

    [ObservableProperty]
    private string? _selectedSeriesContinueText;

    [ObservableProperty]
    private Episode? _selectedSeriesContinueEpisode;

    [ObservableProperty]
    private Season? _selectedSeason;

    [ObservableProperty]
    private bool _isSelectedSeriesMetadataLoading;

    [ObservableProperty]
    private string? _selectedGroup;

    [ObservableProperty]
    private ChannelSortOrder _selectedSortOrder = ChannelSortOrder.NewestFirst;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ChannelType? _selectedChannelType;


    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private BatchObservableCollection<object> _searchResults = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isGlobalLoading;

    [ObservableProperty]
    private string _globalLoadingMessage = string.Empty;

    [ObservableProperty]
    private bool _showOnlyFavorites;

    [ObservableProperty]
    private EpgProgressInfo? _epgProgress;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isChannelLoading;

    private int CurrentViewItemCount => ActiveView == AppView.Series ? SeriesViewItems.Count : FilteredChannels.CountedItemCount;

    private bool IsContentStillLoading => IsLoading || IsChannelLoading;

    public bool IsContentLoading => IsContentStillLoading && CurrentViewItemCount == 0;
    public bool ShowEmptyChannels => !IsContentStillLoading && CurrentViewItemCount == 0;
    public bool ShowContentFilters => !IsContentLoading && !ShowEmptyChannels;
    public bool ShowGroupFilter => ShowContentFilters && Groups.Count > 0;

    private static bool IsDummyChannel(Channel c) =>
        c.StreamUrl != null && (c.StreamUrl.StartsWith("xtream-dummy://") || c.StreamUrl.StartsWith("stalker-dummy://"));

    partial void OnIsChannelLoadingChanged(bool value)
    {
        NotifyContentStateChanged();
    }

    partial void OnActiveViewChanged(AppView value)
    {
        // Görünüm değiştiğinde kategori tipini de senkronize et
        SelectedChannelType = value switch
        {
            AppView.Live => ChannelType.Live,
            AppView.Movies => ChannelType.VOD,
            AppView.Series => ChannelType.Series,
            _ => (ChannelType?)null
        };

        NotifyContentStateChanged();
    }

    partial void OnGroupsChanged(BatchObservableCollection<string> value)
    {
        NotifyContentStateChanged();
    }

    [ObservableProperty]
    private double _channelLoadingProgress;

    [ObservableProperty]
    private string _channelLoadingStats = string.Empty;

    [ObservableProperty]
    private ConnectionHealth _connectionQuality = ConnectionHealth.Good;

    [ObservableProperty]
    private string _connectionStatusText = "";

    [ObservableProperty]
    private string? _channelListLastError;

    [ObservableProperty]
    private string _loadingWarningMessage = string.Empty;

    [ObservableProperty]
    private string _newPlaylistName = string.Empty;

    [ObservableProperty]
    private string _newPlaylistUrl = string.Empty;

    private List<Channel>? _livePlaybackContext;

    public WatermarkViewModel WatermarkViewModel { get; }

    [ObservableProperty]
    private IReadOnlyList<KeyValuePair<ChannelSortOrder, string>> _sortOptions =
        Array.Empty<KeyValuePair<ChannelSortOrder, string>>();

    public MainViewModel(
        ISettingsService settingsService,
        IContentDownloadService contentDownloadService,
        IMetadataService metadataService,
        IDispatcherService dispatcherService,
        IDialogService dialogService,
        WatermarkViewModel watermarkViewModel,
        IChannelService channelService,
        IMediaService mediaService,
        IEpgService epgService,
        IPlaylistService playlistService,
        IWatchHistoryService watchHistoryService,
        IXtreamCodesService xtreamCodesService,
        IStalkerPortalService stalkerPortalService,
        LanguageDetectionService languageDetectionService,
        EpgSourceResolver epgSourceResolver,
        IDbContextFactory<AppDbContext> contextFactory,
        ISecurityService securityService,
        HttpClient httpClient,
        ITmdbSyncService tmdbSyncService,
        ILicenseService licenseService,
        IUpdateService updateService,
        ILocalizationService localizationService,
        ILogger<MainViewModel>? logger = null,
        IPerformanceTraceService? perfTraceService = null)
    {
        _localizationService = localizationService;
        _settingsService = settingsService;
        _contentDownloadService = contentDownloadService;
        _metadataService = metadataService;
        _dispatcherService = dispatcherService;
        _dialogService = dialogService;
        _logger = logger;
        WatermarkViewModel = watermarkViewModel;
        _channelService = channelService;
        _mediaService = mediaService;
        _epgService = epgService;
        _playlistService = playlistService;
        _watchHistoryService = watchHistoryService;
        _xtreamCodesService = xtreamCodesService;
        _stalkerPortalService = stalkerPortalService;
        _languageDetectionService = languageDetectionService;
        _epgSourceResolver = epgSourceResolver;
        _contextFactory = contextFactory;
        _securityService = securityService;
        _httpClient = httpClient;
        _tmdbSyncService = tmdbSyncService;
        _licenseService = licenseService;
        _updateService = updateService;
        _perfTrace = perfTraceService;
        RebuildSortOptions();
        StatusMessage = _localizationService.GetString("Common.Ready");
        RefreshConnectionStatusText();
        _downloadLandingStoredBytes = 0;
        TotalDownloadedCount = 0;
        RefreshDownloadLandingLocalizedTexts();
        _localizationService.LanguageChanged += () =>
        {
            _dispatcherService.BeginInvoke(() =>
            {
                RefreshConnectionStatusText();
                RebuildSortOptions();
                RefreshDownloadLandingLocalizedTexts();
            });
        };
        _settingsService.SettingsChanged += OnSettingsService_Changed;
        InitializeAsync();
        _contentDownloadService.DownloadsChanged += (_, _) =>
        {
            _dispatcherService.BeginInvoke(() =>
            {
                if (CurrentProfileId.HasValue)
                {
                    _ = RefreshDownloadsFromServiceAsync(CurrentProfileId.Value);
                    
                    // IF we are in the Downloads view, we want the landing page (grids) to update too.
                    // We use a short delay to debounce multiple updates and ensure DB is ready.
                    if (ActiveView == AppView.Downloads && !IsDownloadCenterVisible)
                    {
                        ScheduleDownloadsLandingRefresh(CurrentProfileId.Value);
                    }
                }
            });
        };

        _contentDownloadService.DownloadCompleted += (s, item) =>
        {
            _dispatcherService.BeginInvoke(async () =>
            {
                if (_settingsService.Settings.ShowDownloadNotification)
                {
                    await _dialogService.ShowNotificationAsync(
                        _localizationService.GetString("Main.Notification.DownloadCompleted.Title"),
                        string.Format(CultureInfo.CurrentCulture,
                            _localizationService.GetString("Main.Notification.DownloadCompleted.MessageFormat"),
                            item.DisplayName));
                }
            });
        };

        // When background aggregation completes, reload series data
        _mediaService.OnAggregationCompleted += (playlistId) =>
        {
            _dispatcherService.BeginInvoke(async () =>
            {
                if (SelectedPlaylist?.Id == playlistId)
                {
                    try
                    {
                        // Refresh content from DB (Aggregation finished)
                        await LoadHomeContentAsync();
                        StatusMessage = _localizationService.GetString("Main.Status.ChannelsReady"); 
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] Series refreshed after background aggregation for playlist {playlistId}");

                        // If series detail is open, refresh it with the newly aggregated data
                        if (IsSeriesDetailVisible && SelectedSeries != null)
                        {
                            try
                            {
                                var refreshedSeries = await LoadSeriesWithProfileProgressAsync(SelectedSeries);
                                if (CurrentProfile?.ProviderAccount?.Type != ProfileType.XtreamCodes)
                                {
                                    EnsureSeriesEpisodes(refreshedSeries);
                                }
                                SelectedSeries = refreshedSeries;
                                _ = LoadSelectedSeriesMetadataAsync(refreshedSeries);
                                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Series detail auto-refreshed after aggregation: {refreshedSeries.Name}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Series detail auto-refresh failed: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] Series refresh after aggregation failed: {ex.Message}");
                    }
                }
            });
        };
    }

    public bool IsPremium => _licenseService.IsPremium;
    public string CurrentVersion => _updateService.CurrentVersion;

    public Task InitializeAsync()
    {
        // Otomatik yükleme yerine profil yüklenmesini bekle
        return Task.CompletedTask;
    }

    partial void OnConnectionQualityChanged(ConnectionHealth value)
    {
        RefreshConnectionStatusText();
    }

    private void RefreshConnectionStatusText()
    {
        ConnectionStatusText = ConnectionQuality switch
        {
            ConnectionHealth.Good => _localizationService.GetString("Main.Connection.Good"),
            ConnectionHealth.Weak => _localizationService.GetString("Main.Connection.Weak"),
            ConnectionHealth.Bad => _localizationService.GetString("Main.Connection.Weak"),
            ConnectionHealth.Critical => _localizationService.GetString("Main.Connection.Critical"),
            _ => _localizationService.GetString("Main.Connection.Good")
        };
    }

    private void RebuildSortOptions()
    {
        SortOptions = new List<KeyValuePair<ChannelSortOrder, string>>
        {
            new(ChannelSortOrder.NewestFirst, _localizationService.GetString("Main.Sort.Newest")),
            new(ChannelSortOrder.OldestFirst, _localizationService.GetString("Main.Sort.Oldest")),
            new(ChannelSortOrder.NameAsc, _localizationService.GetString("Main.Sort.AZ")),
            new(ChannelSortOrder.NameDesc, _localizationService.GetString("Main.Sort.ZA"))
        };
    }

    private void RefreshDownloadLandingLocalizedTexts()
    {
        var downloadsInfoFmt = _localizationService.GetString("Downloads.Info.Format");
        if (string.IsNullOrWhiteSpace(downloadsInfoFmt))
        {
            downloadsInfoFmt = "{0} items   {1} used";
        }

        TotalDownloadsInfoText = string.Format(CultureInfo.CurrentCulture,
            downloadsInfoFmt,
            TotalDownloadedCount,
            FormatDownloadBytes(_downloadLandingStoredBytes));

        if (CurrentProfileId is { } profileId)
        {
            DownloadFreeDiskSpaceText = ResolveDownloadFreeSpaceText(profileId);
            UpdateDownloadCenterSummary(profileId);
        }
    }

    private void OnSettingsService_Changed()
    {
        ApplyRefreshSchedulesFromSettings();
    }

    private async Task StartSlowLoadingWarningAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(8_000, ct);
            if (ct.IsCancellationRequested) return;

            ConnectionQuality = ConnectionHealth.Weak;
            LoadingWarningMessage = _localizationService.GetString("Main.Loading.Warning.Slow");

            await Task.Delay(12_000, ct);
            if (ct.IsCancellationRequested) return;

            ConnectionQuality = ConnectionHealth.Critical;
            LoadingWarningMessage = _localizationService.GetString("Main.Loading.Warning.Unreachable");
        }
        catch (TaskCanceledException)
        {
            /* normal */
        }
    }

    private async Task CheckPlaylistUrlHealthAsync(string url)
    {
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _dispatcherService.BeginInvoke(() =>
                {
                    StatusMessage = string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("Main.Error.PlaylistUnreachableFormat"),
                        (int)response.StatusCode);
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[HealthCheck] Playlist URL erişim hatası: {Msg}", ex.Message);
            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = _localizationService.GetString("Main.Status.PlaylistUnreachable");
            });
        }
    }

    private int _activeLoadingOperations;

    private void BeginLoading()
    {
        if (Interlocked.Increment(ref _activeLoadingOperations) == 1)
            IsLoading = true;
    }

    private void EndLoading()
    {
        if (Interlocked.Decrement(ref _activeLoadingOperations) <= 0)
        {
            Interlocked.Exchange(ref _activeLoadingOperations, 0);
            IsLoading = false;
        }
    }

    [ObservableProperty]
    private int? _currentProfileId;

    [ObservableProperty]
    private Profile? _currentProfile;

    public async Task LoadProfileAsync(Profile profile)
    {
        if (profile == null) return;
        using var trace = _perfTrace?.BeginOperation("PROFILE", "LoadProfileAsync", $"profile={profile.Id} type={profile.ProviderAccount?.Type}");

        // Clear UI state from previous profile
        _perfTrace?.Event("PROFILE", "ClearProfileState begin", $"profile={profile.Id}");
        ClearProfileState();
        _perfTrace?.Event("PROFILE", "ClearProfileState end", $"profile={profile.Id}");

        IsLoading = true;
        IsChannelLoading = true;
        ChannelLoadingProgress = 0;
        ChannelLoadingStats = string.Empty;
        ConnectionQuality = ConnectionHealth.Good;
        StatusMessage = _localizationService.GetString("Main.Status.PreparingContent");
        CurrentProfileId = profile.Id;
        CurrentProfile = profile;

        // Load profile-specific settings
        using (_perfTrace?.BeginOperation("PROFILE", "LoadProfileSettingsAsync", $"profile={profile.Id}"))
        {
            await _settingsService.LoadProfileSettingsAsync(profile.Id);
        }
        
        try
        {
            // Ensure provider account is loaded
            if (profile.ProviderAccount == null)
            {
                StatusMessage = _localizationService.GetString("Main.Status.LoadingAccountFailed");
                IsLoading = false;
                IsChannelLoading = false;
                return;
            }

                        // Check if playlist already exists (cache-first approach)
                        List<Playlist> existingPlaylists;
                        using (_perfTrace?.BeginOperation("PROFILE", "GetAllPlaylists", $"profile={profile.Id}"))
                        {
                            existingPlaylists = await _playlistService.GetAllAsync(profile.Id);
                        }
                        _perfTrace?.Counter("PROFILE", "ExistingPlaylists", existingPlaylists.Count, $"profile={profile.Id}");
            
                        if (existingPlaylists.Count > 0)
                        {
                            // Use cached playlist - much faster!
                            _logger?.LogDebug($"[MainViewModel] Using cached playlist for profile {profile.Id}");
                            StatusMessage = _localizationService.GetString("Main.Status.FastLoading");
                            using (_perfTrace?.BeginOperation("PROFILE", "LoadPlaylists cached", $"profile={profile.Id}"))
                            {
                                await LoadPlaylistsAsync();
                            }
            
                            // Arka planda URL sağlık kontrolü yap (M3U için geçerli)
                            var playlistUrl = existingPlaylists[0].Url;
                            if (!string.IsNullOrWhiteSpace(playlistUrl) && profile.ProviderAccount.Type == ProfileType.M3U)
                            {
                                _ = CheckPlaylistUrlHealthAsync(playlistUrl);
                            }
            
                                            if (profile.ProviderAccount.Type == ProfileType.StalkerPortal)
                                            {
                                                _perfTrace?.Event("PROFILE", "ResumeStalkerProgressiveLoading queued", $"profile={profile.Id} playlist={existingPlaylists[0].Id}");
                                                _ = ResumeStalkerProgressiveLoadingAsync(profile, existingPlaylists[0]);
                                            }
                                            else if (profile.ProviderAccount.Type == ProfileType.XtreamCodes)
                                            {
                                                _perfTrace?.Event("PROFILE", "ResumeXtreamProgressiveLoading queued", $"profile={profile.Id} playlist={existingPlaylists[0].Id}");
                                                _ = ResumeXtreamProgressiveLoadingAsync(profile, existingPlaylists[0]);
                                            }
                                            else
                                            {
                                                IsChannelLoading = false;
                                                ChannelLoadingProgress = 100;
                                            }
                                        }
                                        else
                                        {
                                            // No cache - download and parse M3U
                                            _logger?.LogDebug($"[MainViewModel] No cache found, downloading playlist for profile {profile.Id}");
                                            
                                            switch (profile.ProviderAccount.Type)
                                            {
                                                case ProfileType.M3U:
                                                {
                                                    var m3uUrl = profile.ProviderAccount.Url;
                                                    _ = CheckM3UExpirationAsync(profile.ProviderAccount);
                                                    StatusMessage = _localizationService.GetString("Main.Status.BackgroundLoading");
                                                    
                                                    _ = Task.Run(async () =>
                                                    {
                                                        try
                                                        {
                                                            await _playlistService.AddFromUrlAsync(profile.Name, m3uUrl, profile.Id);
                                                            _dispatcherService.BeginInvoke(async () =>
                                                            {
                                                                await LoadPlaylistsAsync();
                                                                StatusMessage = _localizationService.GetString("Main.Status.ChannelsReadyOrganizingSeries");
                                                                IsChannelLoading = false;
                                                                ChannelLoadingProgress = 100;
                                                            });
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _dispatcherService.BeginInvoke(() =>
                                                            {
                                                                StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.M3uLoad"), ex);
                                                                IsChannelLoading = false;
                                                            });
                                                        }
                                                    });
                                                    break;
                                                }
                                                                    case ProfileType.XtreamCodes:
                                                                    {
                                                                        StatusMessage = _localizationService.GetString("Main.Status.ConnectingToServer");
                                                                        var baseUrl = profile.ProviderAccount.Url.TrimEnd('/');
                                                                        if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
                                                 
                                                                        _ = CheckXtreamExpirationAsync(profile.ProviderAccount);
                                                                        var username = profile.ProviderAccount.Username ?? string.Empty;
                                                                        var password = _securityService.Decrypt(profile.ProviderAccount.Password) ?? string.Empty;
                                                                        var epgUrl = _xtreamCodesService.GetEpgUrl(baseUrl, username, password);
                                                 
                                                                        // ── Adım 1: Boş playlist oluştur — UI hemen açılabilir ──────────
                                                                        var sourceUrl = $"{baseUrl}#{username}";
                                                                        var playlist = await _playlistService.CreateEmptyPlaylistAsync(
                                                                            profile.Name, sourceUrl, profile.Id, epgUrl);
                                                                        await LoadPlaylistsAsync();                            
                                                    _ = Task.Run(async () =>
                                                    {
                                                        try
                                                        {
                                                            int loadedCats = 0;
                                                            int totalCats = 1;

                                                            await _xtreamCodesService.GetChannelsProgressiveAsync(
                                                                baseUrl, username, password,
                                                                includeVod: true,
                                                                onCategoriesDiscovered: async (categories, prioritizeAction) =>
                                                                {
                                                                    _prioritizeCategoryAction = prioritizeAction;
                                                                    totalCats = categories.Count;
                                                                    _dispatcherService.BeginInvoke(() => 
                                                                    {
                                                                        ChannelLoadingStats = string.Format(CultureInfo.CurrentCulture,
                                                                            _localizationService.GetString("Main.Status.CategoryProgressFormat"), 0, totalCats);
                                                                    });

                                                                    // Arayüzün anında dolması için kategori isimleriyle "sahte" kanallar ekle
                                                                    var dummyChannels = categories.Select(c => new Channel
                                                                    {
                                                                        Name = _localizationService.GetString("Main.Status.LoadingContent"),
                                                                        StreamUrl = $"xtream-dummy://{c.Id}",
                                                                        GroupTitle = c.Name,
                                                                        Type = c.Type == "live" ? ChannelType.Live : (c.Type == "series" ? ChannelType.Series : ChannelType.VOD)
                                                                    }).ToList();
                                                            
                                                                    await _playlistService.AppendChannelsAsync(playlist.Id, dummyChannels);
                                                                    _dispatcherService.BeginInvoke(() => 
                                                                    {
                                                                        if (SelectedPlaylist?.Id == playlist.Id) _ = LoadChannelsAsync(playlist.Id);
                                                                    });
                                                                    return categories;
                                                                },
                                                                onCategoryLoaded: async (channels, groupName) =>
                                                                {
                                                                    await _playlistService.ReplaceDummyWithRealChannelsAsync(playlist.Id, groupName, channels);
                                                                    
                                                                    loadedCats++;
                                                                    _dispatcherService.BeginInvoke(() =>
                                                                    {
                                                                        ChannelLoadingProgress = (double)loadedCats / totalCats * 100;
                                                                        ChannelLoadingStats = string.Format(CultureInfo.CurrentCulture,
                                                                            _localizationService.GetString("Main.Status.CategoryLoadedFormat"), loadedCats, totalCats, groupName);
                                                                    });

                                                                    if (SelectedPlaylist?.Id == playlist.Id && SelectedGroup == groupName)
                                                                    {
                                                                        _ = ThrottledLoadChannelsAsync(playlist.Id);
                                                                    }
                                                                    },
                                                                    cancellationToken: CancellationToken.None);

                                                                    try
                                                                    {
                                                                        _dispatcherService.BeginInvoke(() => StatusMessage = _localizationService.GetString("Main.Status.OrganizingMedia"));
                                                                        await _mediaService.AggregateContentAsync(playlist.Id);
                                                                        _mediaService.RaiseAggregationCompleted(playlist.Id);
                                                                    }
                                                                    catch { }

                                                                    _dispatcherService.BeginInvoke(async () =>
                                                                    {
                                                                        StatusMessage = _localizationService.GetString("Main.Status.XtreamLoaded");
                                                                        
                                                                        // Temizlik: Yüklenemeyen kategorilerin taslak kanallarını sil
                                                                        await _playlistService.DeleteAllDummiesAsync(playlist.Id);

                                                                        IsChannelLoading = false;
                                                                        ChannelLoadingProgress = 100;
                                                                        
                                                                        // Başarı durumunda son bir yükleme yaparak 
                                                                        // dummy kanalların temizlendiğinden emin olalım.
                                                                        if (SelectedPlaylist?.Id == playlist.Id) _ = LoadChannelsAsync(playlist.Id);
                                                                    });
                                                                }
                                                        catch (Exception ex)
                                                        {
                                                            _logger?.LogDebug($"[Xtream] Error: {ex}");
                                                            _dispatcherService.BeginInvoke(async () =>
                                                            {
                                                                StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.XtreamServer"), ex);
                                                                await _playlistService.DeleteAllDummiesAsync(playlist.Id);
                                                                IsChannelLoading = false;
                                                            });
                                                        }
                                                    });
                                                    break;
                                                }
                    case ProfileType.StalkerPortal:
                    {
                        StatusMessage = _localizationService.GetString("Main.Status.StalkerCategoriesLoading");
                        var portalUrl  = profile.ProviderAccount.Url;
                        var macAddress = profile.ProviderAccount.Username ?? string.Empty;
                        var sourceUrl  = $"{portalUrl.TrimEnd('/')}/stalker_portal#{macAddress}";
                        var epgUrl     = _stalkerPortalService.GetEpgUrl(portalUrl);

                        // ── Adım 1: Boş playlist oluştur — UI hemen açılabilir ──────────
                        // Kanallar geldikçe buraya eklenecek
                        var playlist = await _playlistService.CreateEmptyPlaylistAsync(
                            profile.Name, sourceUrl, profile.Id, epgUrl);
                        await LoadPlaylistsAsync();

                        StatusMessage = _localizationService.GetString("Main.Status.StalkerLoadingPatient");

                        // ── Adım 2: Aşamalı yükleme — fire-and-forget ──────────────────
                        // Kullanıcı uygulamayı hemen kullanabilir, içerikler arka planda gelir
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var lastProgressUpdate = DateTime.MinValue;
                                var progress = new Progress<StalkerLoadProgress>(p =>
                                {
                                    var now = DateTime.UtcNow;
                                    if ((now - lastProgressUpdate).TotalMilliseconds > 100)
                                    {
                                        lastProgressUpdate = now;
                                        _dispatcherService.BeginInvoke(() =>
                                        {
                                            StatusMessage = p.Message;
                                            ChannelLoadingStats = p.Message;
                                            if (p.TotalCategories > 0)
                                            {
                                                ChannelLoadingProgress = (double)p.LoadedCategories / p.TotalCategories * 100;
                                            }
                                        });
                                    }
                                });

                                await _stalkerPortalService.GetChannelsProgressiveAsync(
                                    portalUrl,
                                    macAddress,
                                    includeVod: true,
                                    onCategoriesDiscovered: async (categories, prioritizeAction) =>
                                    {
                                        _prioritizeCategoryAction = prioritizeAction;

                                        // Arayüzün anında dolması için kategori isimleriyle "sahte" kanallar ekle
                                        var dummyChannels = categories.Select(c => new Channel
                                        {
                                            Name = _localizationService.GetString("Main.Status.LoadingContent"),
                                            StreamUrl = $"stalker-dummy://{c.Id}",
                                            GroupTitle = c.Name,
                                            Type = c.Type == "itv" ? ChannelType.Live : (c.Type == "series" ? ChannelType.Series : ChannelType.VOD)
                                        }).ToList();

                                        await _playlistService.AppendChannelsAsync(playlist.Id, dummyChannels);

                                        // UI'yi hemen güncelle — gruplar (sol menü) anında dolacak
                                        _dispatcherService.BeginInvoke(() =>
                                        {
                                            if (SelectedPlaylist?.Id == playlist.Id)
                                            {
                                                _ = LoadChannelsAsync(playlist.Id);
                                            }
                                        });
                                        
                                        return categories;
                                    },
                                    onCategoryLoaded: async (channels, category) =>
                                    {
                                        // Kategori dolduğunda sahte kanalı silip gerçekleriyle değiştir
                                        await _playlistService.ReplaceDummyWithRealChannelsAsync(playlist.Id, category.Name, channels);

                                        // Eğer ekranda bu kategori açıksa anlık göster, değilse sol menü zaten yüklü
                                        if (SelectedPlaylist?.Id == playlist.Id && SelectedGroup == category.Name)
                                        {
                                            _ = ThrottledLoadChannelsAsync(playlist.Id);
                                        }
                                    },
                                    progress: progress,
                                    cancellationToken: CancellationToken.None);

                                // Dizi yapısını oluştur
                                _dispatcherService.BeginInvoke(() => StatusMessage = _localizationService.GetString("Main.Status.OrganizingMedia"));
                                
                                try
                                {
                                    await _mediaService.AggregateContentAsync(playlist.Id);
                                    _mediaService.RaiseAggregationCompleted(playlist.Id);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogError(ex, "[Stalker] AggregateContent failed for playlist {Id}", playlist.Id);
                                }

                                // Tüm içerik yüklendi
                                _dispatcherService.BeginInvoke(async () =>
                                {
                                    StatusMessage = _localizationService.GetString("Main.Status.AllContentReady");

                                    // Temizlik: Yüklenemeyen kategorilerin taslak kanallarını sil
                                    await _playlistService.DeleteAllDummiesAsync(playlist.Id);

                                    IsChannelLoading = false;
                                    ChannelLoadingProgress = 100;

                                    // Başarı durumunda son bir yükleme yaparak 
                                    // dummy kanalların temizlendiğinden emin olalım.
                                    if (SelectedPlaylist?.Id == playlist.Id) _ = LoadChannelsAsync(playlist.Id);
                                });
                            }
                            catch (Exception ex)
                            {
                                _dispatcherService.BeginInvoke(async () =>
                                {
                                    StatusMessage = UserFriendlyErrorMessage.WithPrefix(
                                        _localizationService.GetString("Main.Error.ContentLoad"), ex);
                                    
                                    await _playlistService.DeleteAllDummiesAsync(playlist.Id);
                                    IsChannelLoading = false;
                                });
                            }
                        });

                        // ── Bu satıra kadar geçen süre: ~3-5 saniye ─────────────────────
                        // Kullanıcı artık uygulamayı kullanabilir
                        break;
                    }
                    default:
                        throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture,
                            _localizationService.GetString("Main.Error.UnsupportedProfileFormat"),
                            profile.ProviderAccount.Type));
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.ProfileLoad"), ex);
            _logger?.LogDebug($"LoadProfile Error: {ex}");
            IsChannelLoading = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ClearProfileState()
    {
        // Reset selections and filters
        SelectedPlaylist = null;
        SelectedChannel = null;
        SelectedSeries = null;
        SelectedGroup = null;
        IsSeriesDetailVisible = false;
        SearchText = string.Empty;
        SearchQuery = string.Empty;
        SelectedSortOrder = ChannelSortOrder.NewestFirst;

        // Reset pagination and internal caches
        _currentPage = 0;
        _hasMoreChannels = false;
        _isLoadingMoreChannels = false;
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = false;
        _isLoadingMoreSeriesItems = false;
        _seriesFilteredSource.Clear();
        _allGroupsCache.Clear();
        _liveGroupsCache.Clear();
        _vodGroupsCache.Clear();
        _seriesGroupsCache.Clear();
        _isEpisodeContinueDirty = true;
        _cachedEpisodeContinue = null;

        // Clear collections
        SetItems(Playlists, Enumerable.Empty<Playlist>());
        SetItems(Channels, Enumerable.Empty<Channel>());
        SetItems(FilteredChannels, Enumerable.Empty<Channel>());
        SetItems(ContinueWatching, Enumerable.Empty<Channel>());
        SetItems(SeriesViewItems, Enumerable.Empty<Series>());
        SetItems(SearchResults, Enumerable.Empty<object>());
        SetItems(Groups, Enumerable.Empty<string>());
        
        // My List, Favorites, History
        SetItems(MyList, Enumerable.Empty<object>());
        SetItems(FavoriteChannels, Enumerable.Empty<object>());
        SetItems(HistoryChannels, Enumerable.Empty<Channel>());
        SetItems(HistoryLiveChannels, Enumerable.Empty<Channel>());
        SetItems(HistorySeriesItems, Enumerable.Empty<Series>());
        SetItems(HistoryVodChannels, Enumerable.Empty<Channel>());

        // Downloads
        SetItems(ActiveDownloadItems, Enumerable.Empty<DownloadItem>());
        SetItems(ActiveDownloadingItems, Enumerable.Empty<DownloadItem>());
        SetItems(QueuedDownloadItems, Enumerable.Empty<DownloadItem>());
        SetItems(CompletedDownloadItems, Enumerable.Empty<DownloadItem>());
        SetItems(DownloadedSeriesItems, Enumerable.Empty<Series>());
        SetItems(DownloadedVodChannels, Enumerable.Empty<Channel>());

        // Search Results
        SetItems(SearchLiveChannels, Enumerable.Empty<Channel>());
        SetItems(SearchSeriesChannels, Enumerable.Empty<Series>());
        SetItems(SearchVodChannels, Enumerable.Empty<Channel>());
        SetItems(SearchSimilarLiveChannels, Enumerable.Empty<Channel>());
        SetItems(SearchSimilarSeriesChannels, Enumerable.Empty<Series>());
        SetItems(SearchSimilarVodChannels, Enumerable.Empty<Channel>());

        StatusMessage = string.Empty;
        _prioritizeCategoryAction = null;
        NotifyContentStateChanged();
    }

    private void ResetUIForRefresh()
    {
        // Reset selections and filters
        SelectedChannel = null;
        SelectedSeries = null;
        SelectedGroup = null;
        IsSeriesDetailVisible = false;
        SearchText = string.Empty;
        SearchQuery = string.Empty;

        // Reset pagination and internal caches
        _currentPage = 0;
        _hasMoreChannels = false;
        _isLoadingMoreChannels = false;
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = false;
        _isLoadingMoreSeriesItems = false;
        _seriesFilteredSource.Clear();
        _allGroupsCache.Clear();
        _liveGroupsCache.Clear();
        _vodGroupsCache.Clear();
        _seriesGroupsCache.Clear();
        _isEpisodeContinueDirty = true;
        _cachedEpisodeContinue = null;

        // Clear UI collections
        SetItems(Channels, Enumerable.Empty<Channel>());
        SetItems(FilteredChannels, Enumerable.Empty<Channel>());
        SetItems(Groups, Enumerable.Empty<string>());
        SetItems(SeriesViewItems, Enumerable.Empty<Series>());

        NotifyContentStateChanged();
        
        StatusMessage = _localizationService.GetString("Main.Status.RefreshingChannels");
    }

    public Task RefreshCurrentProfileExpirationAsync()
    {
        var account = CurrentProfile?.ProviderAccount;
        if (account == null)
        {
            return Task.CompletedTask;
        }

        return account.Type switch
        {
            ProfileType.M3U => CheckM3UExpirationAsync(account),
            ProfileType.XtreamCodes => CheckXtreamExpirationAsync(account),
            _ => Task.CompletedTask
        };
    }

    private async Task ResumeXtreamProgressiveLoadingAsync(Profile profile, Playlist playlist, bool isFullRefresh = false)
    {
        if (profile.ProviderAccount == null) return;

        try
        {
            var uiResetDone = false;
            List<string> pendingGroups = new();
            if (!isFullRefresh)
            {
                pendingGroups = await _playlistService.GetPendingDummyGroupsAsync(playlist.Id);
                if (pendingGroups.Count == 0) return;
            }

            var baseUrl = profile.ProviderAccount.Url.TrimEnd('/');
            if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
            var username = profile.ProviderAccount.Username ?? string.Empty;
            var password = _securityService.Decrypt(profile.ProviderAccount.Password) ?? string.Empty;

            await _xtreamCodesService.GetChannelsProgressiveAsync(
                baseUrl, username, password,
                includeVod: true,
                onCategoriesDiscovered: async (categories, prioritizeAction) =>
                {
                    if (isFullRefresh) 
                    {
                        if (!uiResetDone)
                        {
                            uiResetDone = true;
                            // Önce eski kanalları DB'den sil, sonra UI'yı sıfırla
                            await _playlistService.DeleteAllChannelsForRefreshAsync(playlist.Id);
                            _dispatcherService.BeginInvoke(() => ResetUIForRefresh());
                        }

                        // Repopulate UI with dummy channels for the discovered categories
                        var dummyChannels = categories.Select(c => new Channel
                        {
                            Name = _localizationService.GetString("Main.Status.LoadingContent"),
                            StreamUrl = $"xtream-dummy://{c.Id}",
                            GroupTitle = c.Name,
                            Type = c.Type == "live" ? ChannelType.Live : (c.Type == "series" ? ChannelType.Series : ChannelType.VOD)
                        }).ToList();
                
                        await _playlistService.AppendChannelsAsync(playlist.Id, dummyChannels);
                        _dispatcherService.BeginInvoke(() => 
                        {
                            if (SelectedPlaylist?.Id == playlist.Id) _ = LoadChannelsAsync(playlist.Id);
                        });

                        return categories;
                    }

                    var toLoad = categories
                        .Where(c => pendingGroups.Contains(c.Name, StringComparer.OrdinalIgnoreCase) || 
                                    pendingGroups.Contains(c.Id, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    return toLoad;
                },
                onCategoryLoaded: async (channels, groupName) =>
                {
                    await _playlistService.ReplaceDummyWithRealChannelsAsync(playlist.Id, groupName, channels);

                    if (SelectedPlaylist?.Id == playlist.Id && SelectedGroup == groupName)
                    {
                        _ = ThrottledLoadChannelsAsync(playlist.Id);
                    }
                },
                cancellationToken: CancellationToken.None);

            // WatchHistory onarımı: Eski kanal fingerprint'lerini yeni kanal ID'leriyle eşleştir
            await _playlistService.RepairWatchHistoryChannelIdsAsync(playlist.Id);

            _dispatcherService.BeginInvoke(() => StatusMessage = _localizationService.GetString("Main.Status.OrganizingMedia"));
            try
            {
                await _mediaService.AggregateContentAsync(playlist.Id);
                _mediaService.RaiseAggregationCompleted(playlist.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Xtream] Resume AggregateContent failed for playlist {Id}", playlist.Id);
            }

            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = _localizationService.GetString("Main.Status.XtreamUpdated");
                IsChannelLoading = false;
                ChannelLoadingProgress = 100;
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[Xtream] Resume error: {ex}");
            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.XtreamServer"), ex);
            });
            
            if (isFullRefresh)
            {
                throw;
            }
        }
    }

    private async Task ResumeStalkerProgressiveLoadingAsync(Profile profile, Playlist playlist, bool isFullRefresh = false)
    {
        if (profile.ProviderAccount == null) return;

        try
        {
            var uiResetDone = false;
            List<string> pendingGroups = new();
            if (!isFullRefresh)
            {
                var totalChannelCount = await _playlistService.GetChannelCountAsync(playlist.Id);
                pendingGroups = await _playlistService.GetPendingDummyGroupsAsync(playlist.Id);
                
                _logger?.LogInformation($"[Stalker] Startup check for playlist {playlist.Id}: Total channels={totalChannelCount}, Pending dummy groups={pendingGroups.Count}");

                if (pendingGroups.Count == 0)
                {
                    _logger?.LogInformation($"[Stalker] Skipping background load - All content already fully loaded in DB (Total channels: {totalChannelCount}).");
                    _dispatcherService.BeginInvoke(() => IsChannelLoading = false);
                    return; // Everything is loaded!
                }

                if (pendingGroups.Count > 0 && pendingGroups.Count < 15)
                {
                    _logger?.LogInformation($"[Stalker] Pending clusters: {string.Join(", ", pendingGroups)}");
                }
            }

            _logger?.LogInformation($"[Stalker] {(isFullRefresh ? "Full Refresh" : "Resume background load")} starting for {pendingGroups.Count} categories.");
            var portalUrl = profile.ProviderAccount.Url;
            var macAddress = profile.ProviderAccount.Username ?? string.Empty;

            var lastProgressUpdate = DateTime.MinValue;
            var progress = new Progress<StalkerLoadProgress>(p =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastProgressUpdate).TotalMilliseconds > 100)
                {
                    lastProgressUpdate = now;
                    _dispatcherService.BeginInvoke(() =>
                    {
                        StatusMessage = p.Message;
                    });
                }
            });

            await _stalkerPortalService.GetChannelsProgressiveAsync(
                portalUrl,
                macAddress,
                includeVod: true,
                onCategoriesDiscovered: async (categories, prioritizeAction) =>
                {
                    _prioritizeCategoryAction = prioritizeAction;

                    if (isFullRefresh) 
                    {
                        if (!uiResetDone)
                        {
                            uiResetDone = true;
                            // Önce eski kanalları DB'den sil, sonra UI'yı sıfırla
                            await _playlistService.DeleteAllChannelsForRefreshAsync(playlist.Id);
                            _dispatcherService.BeginInvoke(() => ResetUIForRefresh());
                        }

                        // Repopulate UI with dummy channels for the discovered categories
                        var dummyChannels = categories.Select(c => new Channel
                        {
                            Name = _localizationService.GetString("Main.Status.LoadingContent"),
                            StreamUrl = $"stalker-dummy://{c.Name}",
                            GroupTitle = c.Name,
                            Type = c.Type.Equals("series", StringComparison.OrdinalIgnoreCase)
                                ? ChannelType.Series
                                : (c.Type.Equals("vod", StringComparison.OrdinalIgnoreCase)
                                    ? ChannelType.VOD
                                    : ChannelType.Live)
                        }).ToList();

                        await _playlistService.AppendChannelsAsync(playlist.Id, dummyChannels);
                        _dispatcherService.BeginInvoke(() => 
                        {
                            if (SelectedPlaylist?.Id == playlist.Id) _ = LoadChannelsAsync(playlist.Id);
                        });

                        return categories;
                    }

                    // Yalnızca pendingCategories içinde olanları indirilecek listeye filtrele
                    var categoriesToDownload = categories
                        .Where(c => pendingGroups.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    _logger?.LogInformation($"[Stalker] Filtered categories for resume: {categoriesToDownload.Count} categories will be downloaded (from {categories.Count} total).");
                    return categoriesToDownload;
                },
                onCategoryLoaded: async (channels, category) =>
                {
                    _logger?.LogInformation($"[Stalker] Category Loaded: {category.Name} ({channels.Count} real channels replacing dummy)");
                    await _playlistService.ReplaceDummyWithRealChannelsAsync(playlist.Id, category.Name, channels);

                    if (SelectedPlaylist?.Id == playlist.Id && SelectedGroup == category.Name)
                    {
                        _ = ThrottledLoadChannelsAsync(playlist.Id);
                    }
                },
                progress: progress,
                cancellationToken: CancellationToken.None);

            // WatchHistory onarımı: Eski kanal fingerprint'lerini yeni kanal ID'leriyle eşleştir
            await _playlistService.RepairWatchHistoryChannelIdsAsync(playlist.Id);

            _dispatcherService.BeginInvoke(() => StatusMessage = _localizationService.GetString("Main.Status.OrganizingMedia"));
            try
            {
                await _mediaService.AggregateContentAsync(playlist.Id);
                _mediaService.RaiseAggregationCompleted(playlist.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Stalker] Resume AggregateContent failed for playlist {Id}", playlist.Id);
            }

            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = isFullRefresh
                    ? _localizationService.GetString("Main.Status.RefreshComplete")
                    : _localizationService.GetString("Main.Status.AllContentReady");
                IsChannelLoading = false;
                ChannelLoadingStats = string.Empty;
                ChannelLoadingProgress = 100;
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[Stalker] Failed to {(isFullRefresh ? "refresh" : "resume")} load: {ex}");
            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.StalkerServer"), ex);
            });
            
            if (isFullRefresh)
            {
                throw;
            }
        }
    }

    private async Task CheckM3UExpirationAsync(ProviderAccount account)
    {
        try
        {
            // Try to find username and password in URL
            var uri = new Uri(account.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            // Some providers append expiry directly in M3U URL query.
            var queryExpiration = TryParseExpirationFromQuery(query);
            if (queryExpiration.HasValue)
            {
                await UpdateProviderExpirationAsync(account.Id, queryExpiration.Value);
            }
            
            string? username = query["username"];
            string? password = query["password"];

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                // Construct base URL (scheme + host + port)
                var baseUrl = $"{uri.Scheme}://{uri.Host}";
                if (!uri.IsDefaultPort) baseUrl += $":{uri.Port}";
                
                await CheckExpirationInternalAsync(account.Id, baseUrl, username, password);
                
                if (string.IsNullOrEmpty(account.Username))
                {
                     account.Username = username;
                }
            }
        }
        catch
        {
            // URL geçersiz — eski (stale) bitiş tarihini temizle
            await ClearProviderExpirationAsync(account.Id);
        }
    }

    private async Task UpdateProviderExpirationAsync(int accountId, DateTime expirationDate)
    {
        await _playlistService.UpdateProviderExpirationAsync(accountId, expirationDate);

        _dispatcherService.BeginInvoke(() =>
        {
            if (CurrentProfile?.ProviderAccount?.Id == accountId)
            {
                CurrentProfile.ProviderAccount.ExpirationDate = expirationDate;
            }
        });
    }

    private async Task ClearProviderExpirationAsync(int accountId)
    {
        await _playlistService.ClearProviderExpirationAsync(accountId);

        _dispatcherService.BeginInvoke(() =>
        {
            if (CurrentProfile?.ProviderAccount?.Id == accountId)
            {
                CurrentProfile.ProviderAccount.ExpirationDate = null;
            }
        });
    }

    private static DateTime? TryParseExpirationFromQuery(System.Collections.Specialized.NameValueCollection query)
    {
        var candidateKeys = new[] { "exp", "expires", "expiry", "expiration", "expire", "exp_date" };
        foreach (var key in candidateKeys)
        {
            var raw = query[key];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (long.TryParse(raw, out var unix) && unix > 0)
            {
                // Handle both seconds and milliseconds epochs.
                if (unix > 9999999999)
                {
                    unix /= 1000;
                }

                return DateTimeOffset.FromUnixTimeSeconds(unix).DateTime;
            }

            if (DateTime.TryParse(raw, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private async Task CheckXtreamExpirationAsync(ProviderAccount account)
    {
        var baseUrl = account.Url.TrimEnd('/');
        if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
        var decryptedPassword = _securityService.Decrypt(account.Password);
        await CheckExpirationInternalAsync(account.Id, baseUrl, account.Username, decryptedPassword);
    }

    private async Task CheckExpirationInternalAsync(int accountId, string baseUrl, string? username, string? password)
    {
        try
        {
            var apiUrl = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username ?? "")}&password={Uri.EscapeDataString(password ?? "")}";
            _logger?.LogDebug($"[CheckExpiration] Checking: {apiUrl}");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var response = await client.GetAsync(apiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogDebug($"[CheckExpiration] Failed with status: {response.StatusCode}. Clearing stale expiration.");
                await ClearProviderExpirationAsync(accountId);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("user_info", out var userInfo))
            {
                if (userInfo.TryGetProperty("exp_date", out var expDateElement)) 
                {
                    long? expTimestamp = null;
                    
                    if (expDateElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        expTimestamp = expDateElement.GetInt64();
                    }
                    else if (expDateElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var str = expDateElement.GetString();
                        if (long.TryParse(str, out var val)) 
                        {
                            expTimestamp = val;
                        }
                        else if (DateTime.TryParse(str, out var dt))
                        {
                             expTimestamp = new DateTimeOffset(dt).ToUnixTimeSeconds();
                        }
                    }

                    if (expTimestamp.HasValue && expTimestamp > 0)
                    {
                        var expirationDate = DateTimeOffset.FromUnixTimeSeconds(expTimestamp.Value).DateTime;
                        
                        await UpdateProviderExpirationAsync(accountId, expirationDate);
                        return;
                    }
                }
            }

            // API yanıtı geldi ama exp_date bulunamadı — bilinmiyor olarak işaretle
            _logger?.LogDebug("[CheckExpiration] API responded but no exp_date found. Clearing.");
            await ClearProviderExpirationAsync(accountId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[CheckExpiration] Error: {ex.Message}. Clearing stale expiration.");
            // URL geçersiz veya sunucuya erişilemiyor — eski (stale) tarihi temizle
            await ClearProviderExpirationAsync(accountId);
        }
    }

    [RelayCommand]
    private async Task LoadPlaylistsAsync()
    {
        try
        {
            BeginLoading();
            SetItems(Playlists, await _playlistService.GetAllAsync(CurrentProfileId));
            
            if (Playlists.Count > 0 && SelectedPlaylist == null)
            {
                SelectedPlaylist = Playlists[0];
            }
            else if (Playlists.Count == 0)
            {
                Channels.Clear();
                FilteredChannels.Clear();
            }
        }
        finally
        {
            EndLoading();
        }
    }

    partial void OnSelectedPlaylistChanged(Playlist? value)
    {
        if (value != null)
        {
            _ = LoadChannelsAsync(value.Id);
        }
    }

    private async Task LoadChannelsAsync(int playlistId)
    {
        try
        {
            BeginLoading();
            StatusMessage = _localizationService.GetString("Main.Status.OptimizingLayout");
            
            // Single-pass query: Fetch all groups and total count at once (Significantly faster)
            var meta = await _playlistService.GetChannelGroupMetadataAsync(playlistId);
            
            _allGroupsCache = OrderGroupsByLanguagePreference(meta.AllGroups);
            _liveGroupsCache = OrderGroupsByLanguagePreference(meta.LiveGroups);
            _vodGroupsCache = OrderGroupsByLanguagePreference(meta.VodGroups);
            _seriesGroupsCache = OrderGroupsByLanguagePreference(meta.SeriesGroups);

            UpdateGroupsForSelectedType();
            ResetIncrementalState();

            await Task.WhenAll(
                LoadMoreChannelsAsync(),
                LoadHomeContentAsync());

            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Main.Status.ContentsReadyFormat"), meta.TotalCount);
            // Fire-and-forget tasks are wrapped to avoid unobserved failures and task races.
            StartPostChannelLoadBackgroundTasks();
            EnsureChannelBackgroundRefresh();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"LoadChannels error: {ex}");
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Status.LoadingChannelsFailed"), ex);
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task WarmupAfterInitialChannelLoadAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadHomeContentAsync(),
                LoadFavoritesAsync());
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"WarmupAfterInitialChannelLoadAsync error: {ex}");
        }
    }

    private void StartPostChannelLoadBackgroundTasks()
    {
        _ = RunPostChannelLoadBackgroundTasksAsync();
    }

    private async Task RunPostChannelLoadBackgroundTasksAsync()
    {
        using var trace = _perfTrace?.BeginOperation("BG", "RunPostChannelLoadBackgroundTasksAsync");
        try
        {
            await WarmupAfterInitialChannelLoadAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"Post-load warmup failed: {ex}");
        }

        if (Interlocked.Exchange(ref _isBackgroundEpgSyncRunning, 1) == 1)
        {
            _perfTrace?.Event("BG", "Post-load EPG skipped", "already running");
            return;
        }

        try
        {
            await LoadEpgInternalAsync(isBackgroundSync: true, forceRefresh: false, setBusyState: false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"Post-load EPG sync failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _isBackgroundEpgSyncRunning, 0);
        }
    }

    private async Task LoadHomeContentAsync()
    {
        using var trace = _perfTrace?.BeginOperation("HOME", "LoadHomeContentAsync", $"playlist={SelectedPlaylist?.Id ?? 0}");
        try 
        {
            var playlistId = SelectedPlaylist?.Id ?? 0;
            _allSeriesCache = await _mediaService.GetSeriesListAsync(playlistId);
            _perfTrace?.Counter("HOME", "AllSeriesCache", _allSeriesCache.Count, $"playlist={playlistId}");

            _isEpisodeContinueDirty = true;
            _cachedEpisodeContinue = null;
            UpdateSeriesViewItems();

            // Arka planda tüm dizi ilerlemelerini verimli şekilde yükle (Bulk sync)
            if (CurrentProfileId.HasValue && _allSeriesCache.Count > 0)
            {
                _ = Task.Run(async () => {
                    await _dispatcherService.InvokeAsync(async () => {
                        await UpdateHistoryBucketsAsync();
                        await UpdateContinueWatchingRailAsync();
                    });
                });
            }
            else
            {
                // Profil yoksa direkt UI güncellemesi yap
                await UpdateContinueWatchingRailAsync();
                await UpdateHistoryBucketsAsync();
            }
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading series cache for home content");
        }

    }

    private async Task UpdateContinueWatchingRailAsync()
    {
        // Debounce: cancel any pending execution within 100ms
        _continueWatchingDebounceCts?.Cancel();
        _continueWatchingDebounceCts?.Dispose();
        _continueWatchingDebounceCts = new CancellationTokenSource();
        var token = _continueWatchingDebounceCts.Token;
        try
        {
            await Task.Delay(100, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var playlistId = SelectedPlaylist?.Id ?? 0;
        var profileId = CurrentProfileId;

        if (playlistId <= 0 || !profileId.HasValue)
        {
            _dispatcherService.Invoke(() => SetItems(ContinueWatching, Enumerable.Empty<Channel>()));
            return;
        }

        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            // VOD: Read from WatchHistories instead of in-memory Channels collection
            var vodRows = await db.WatchHistories
                .AsNoTracking()
                .Include(h => h.Channel)
                .Where(h => h.ProfileId == profileId.Value
                         && h.ChannelId.HasValue
                         && h.Channel != null
                         && h.Channel.PlaylistId == playlistId
                         && h.Channel.Type == ChannelType.VOD)
                .OrderByDescending(h => h.WatchedAt)
                .Take(100)
                .ToListAsync();

            var vodContinue = vodRows
                .Where(h => h.Channel != null)
                .Select(h =>
                {
                    var channel = h.Channel!;
                    channel.LastWatched = h.WatchedAt;
                    channel.WatchedPosition = h.StoppedAt;
                    channel.IsCompleted = h.Completed;
                    if ((!channel.Duration.HasValue || channel.Duration.Value.TotalSeconds <= 0) &&
                        channel.WatchedPosition.HasValue &&
                        channel.WatchedPosition.Value.TotalSeconds > 0)
                    {
                        channel.Duration = channel.WatchedPosition.Value + TimeSpan.FromMinutes(30);
                    }
                    return channel;
                })
                .Where(c => IsContinueWatchingCandidate(c.WatchedPosition, c.Duration, c.IsCompleted))
                .ToList();

            List<Channel> episodeContinue;

            if (!_isEpisodeContinueDirty && _cachedEpisodeContinue != null)
            {
                episodeContinue = _cachedEpisodeContinue;
            }
            else
            {
                var episodeRows = await db.WatchHistories
                    .AsNoTracking()
                    .Include(h => h.Episode)
                        .ThenInclude(e => e!.Season)
                            .ThenInclude(s => s!.Series)
                    .Where(h => h.ProfileId == profileId.Value
                             && h.EpisodeId.HasValue
                             && h.Episode != null
                             && h.Episode.Season != null
                             && h.Episode.Season.Series != null
                             && h.Episode.Season.Series.PlaylistId == playlistId)
                    .OrderByDescending(h => h.WatchedAt)
                    .Take(200)
                    .ToListAsync();

                episodeContinue = episodeRows
                    .Where(h => h.Episode != null
                             && h.Episode.Season != null
                             && h.Episode.Season.Series != null
                             && IsContinueWatchingCandidate(h.StoppedAt, h.Episode.Duration, h.Completed))
                    .GroupBy(h => h.Episode!.Season!.SeriesId)
                    .Select(g => g.OrderByDescending(h => h.WatchedAt).First())
                    .Select(h =>
                    {
                        var episode = h.Episode!;
                        episode.LastWatched = h.WatchedAt;
                        episode.WatchedPosition = h.StoppedAt;
                        episode.IsCompleted = h.Completed;

                        return BuildSeriesEpisodeChannel(episode, episode.Season?.Series);
                    })
                    .ToList();

                _cachedEpisodeContinue = episodeContinue;
                _isEpisodeContinueDirty = false;
            }

            var combinedContinue = vodContinue
                .Concat(episodeContinue)
                .Where(c => IsContinueWatchingCandidate(c.WatchedPosition, c.Duration, c.IsCompleted))
                .GroupBy(c => string.IsNullOrWhiteSpace(c.StreamUrl)
                    ? $"{c.Type}:{c.Id}:{c.Name}"
                    : c.StreamUrl,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.LastWatched).First())
                .OrderByDescending(c => c.LastWatched)
                .Take(10)
                .ToList();

            _dispatcherService.Invoke(() => SetItems(ContinueWatching, combinedContinue));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating Continue Watching rail");
            _dispatcherService.Invoke(() => SetItems(ContinueWatching, Enumerable.Empty<Channel>()));
        }
    }

    private static bool IsContinueWatchingCandidate(TimeSpan? watchedPosition, TimeSpan? duration, bool completed)
    {
        if (completed)
        {
            return false;
        }

        if (!watchedPosition.HasValue || watchedPosition.Value.TotalSeconds <= 120)
        {
            return false;
        }

        // Bazi VOD streamlerde duration yok/0 geliyor.
        // Duration yok diye karti dusurmeyelim.
        if (!duration.HasValue || duration.Value.TotalSeconds <= 0)
        {
            return true;
        }

        return watchedPosition.Value.TotalSeconds / duration.Value.TotalSeconds < 0.92;
    }

    private static bool HasDisplayImage(Channel channel)
        => IsDisplayImageUrl(channel.CoverUrl) || IsDisplayImageUrl(channel.LogoUrl);

    private static bool HasDisplayImage(Series series)
        => IsDisplayImageUrl(series.CoverUrl);

    private static bool ShouldRefreshProviderImageUrl(string? url)
    {
        if (!IsDisplayImageUrl(url))
        {
            return true;
        }

        var normalized = url!.Trim().Trim('"', '\'');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return true;
        }

        if (uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return !(host == "image.tmdb.org" ||
                 host.EndsWith(".tmdb.org", StringComparison.OrdinalIgnoreCase) ||
                 host.Contains("themoviedb", StringComparison.OrdinalIgnoreCase) ||
                 host.Contains("tmdb", StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeImageHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "empty";
        }

        var normalized = url.Trim().Trim('"', '\'');
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? uri.Host
            : "invalid";
    }

    private static bool IsDisplayImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var normalized = url.Trim().Trim('"', '\'');
        if (normalized.Length < 12)
        {
            return false;
        }

        if (normalized.Equals("logo n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("//", StringComparison.Ordinal) ||
               normalized.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDownloadedStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        var normalized = streamUrl.Trim().Trim('"', '\'');
        if (normalized.Length < 4)
        {
            return false;
        }

        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri))
            {
                return File.Exists(fileUri.LocalPath);
            }

            return File.Exists(normalized);
        }

        if (!Regex.IsMatch(normalized, @"^[a-zA-Z]:[\\/]"))
        {
            return false;
        }

        return File.Exists(normalized);
    }

    private static bool SeriesHasDownloadedEpisode(Series series)
    {
        if (series?.Seasons == null || series.Seasons.Count == 0)
        {
            return false;
        }

        foreach (var season in series.Seasons)
        {
            if (season?.Episodes == null || season.Episodes.Count == 0)
            {
                continue;
            }

            if (season.Episodes.Any(e => IsDownloadedStreamUrl(e.StreamUrl)))
            {
                return true;
            }
        }

        return false;
    }

    private static Series BuildDownloadedOnlySeries(Series series)
    {
        var filteredSeasons = series.Seasons
            .OrderBy(s => s.SeasonNumber)
            .Select(season => new Season
            {
                Id = season.Id,
                SeasonNumber = season.SeasonNumber,
                Name = season.Name,
                CoverUrl = season.CoverUrl,
                SeriesId = season.SeriesId,
                Episodes = season.Episodes
                    .Where(e => IsDownloadedStreamUrl(e.StreamUrl))
                    .OrderBy(e => e.EpisodeNumber)
                    .GroupBy(e => e.Id > 0 ? $"id:{e.Id}" : $"url:{e.StreamUrl}")
                    .Select(g => g.First())
                    .Select(e => new Episode
                    {
                        Id = e.Id,
                        EpisodeNumber = e.EpisodeNumber,
                        Name = e.Name,
                        StreamUrl = e.StreamUrl,
                        Plot = e.Plot,
                        CoverUrl = e.CoverUrl,
                        Duration = e.Duration,
                        LastWatched = e.LastWatched,
                        WatchedPosition = e.WatchedPosition,
                        SeasonId = e.SeasonId,
                        IntroStartSec = e.IntroStartSec,
                        IntroEndSec = e.IntroEndSec,
                        CreditsStartSec = e.CreditsStartSec,
                        IsCompleted = e.IsCompleted
                    })
                    .ToList()
            })
            .Where(s => s.Episodes.Count > 0)
            .ToList();

        return new Series
        {
            Id = series.Id,
            Name = series.Name,
            CoverUrl = series.CoverUrl,
            Plot = series.Plot,
            Genre = series.Genre,
            ReleaseYear = series.ReleaseYear,
            Rating = series.Rating,
            PlaylistId = series.PlaylistId,
            IsInMyList = series.IsInMyList,
            IsFavorite = series.IsFavorite,
            Seasons = filteredSeasons
        };
    }

    private async Task LoadFavoritesAsync()
    {
        await RefreshPersonalListsFromDatabaseAsync();
    }

    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _downloadsLandingRefreshCts;
    private int _isDownloadsLandingRefreshing;
    private const int ImmediateFilterCoalesceDelayMs = 75;
    private const int DuplicateFilterSuppressWindowMs = 350;
    private readonly int _filterDelayMs = 300;
    private readonly int _searchDelayMs = 200;
    private int _filterRequestVersion;
    private int _isFilterApplyRunning;
    private int _pendingFilterRequest;
    private string? _pendingFilterReason;
    private string? _pendingFilterCaller;
    private string? _lastCompletedFilterSignature;
    private DateTime _lastCompletedFilterUtc;
    private int _currentPage;
    private bool _hasMoreChannels;
    private bool _isLoadingMoreChannels;
    private int _currentSeriesPage;
    private bool _hasMoreSeriesItems;
    private bool _isLoadingMoreSeriesItems;
    private List<Series> _seriesFilteredSource = new();
    private List<string> _allGroupsCache = new();
    private List<string> _liveGroupsCache = new();
    private List<string> _vodGroupsCache = new();
    private List<string> _seriesGroupsCache = new();
    private Timer? _epgSyncTimer;
    private Timer? _uiEpgRefreshTimer;
    private Timer? _channelSyncTimer;
    private int _isBackgroundEpgSyncRunning;
    private int _isBackgroundChannelSyncRunning;
    private int _isManualEpgRefreshRunning;
    private int _isRefreshingPlaylist;
    private int _isAddingPlaylist;
    private int _isManualRefreshRunning;
    private readonly Dictionary<int, DateTime> _playlistNoChangeUntilUtc = new();
    private bool _suppressFilterRefresh;
    private Action<string>? _prioritizeCategoryAction;
    private int _isThrottledLoadPending = 0;
    private bool _needsThrottledLoad;
    private bool _seriesDetailDownloadedOnlyMode;
    private List<Channel>? _cachedEpisodeContinue;
    private bool _isEpisodeContinueDirty = true;
    private CancellationTokenSource? _continueWatchingDebounceCts;
    private async Task ThrottledLoadChannelsAsync(int playlistId)
    {
        lock (this)
        {
            if (Interlocked.CompareExchange(ref _isThrottledLoadPending, 1, 0) == 1)
            {
                _needsThrottledLoad = true;
                return;
            }
        }

        try
        {
            do
            {
                _needsThrottledLoad = false;
                // 500ms bekle (Aynı anda biten diğer kategorilerin de veritabanına yazılmasına izin ver)
                await Task.Delay(500);
                await LoadChannelsAsync(playlistId);
            } while (_needsThrottledLoad);
        }
        catch
        {
            // Hata durumunda loglanabilir
        }
        finally
        {
            Interlocked.Exchange(ref _isThrottledLoadPending, 0);
        }
    }

    public bool IsDownloadedSeriesDetailMode => _seriesDetailDownloadedOnlyMode;

    internal void UpdateSeriesLastWatchedEpisodeAt(int seriesId, DateTime lastWatchedUtc)
    {
        for (int i = 0; i < _allSeriesCache.Count; i++)
        {
            if (_allSeriesCache[i].Id == seriesId)
            {
                _allSeriesCache[i].LastWatchedEpisodeAt = lastWatchedUtc;
                break;
            }
        }

        RefreshContinueWatchingRail(episodeContinueDirty: true);
        _ = UpdateHistoryBucketsAsync();
    }

    internal void RefreshContinueWatchingRail(bool episodeContinueDirty = true)
    {
        if (episodeContinueDirty)
        {
            _isEpisodeContinueDirty = true;
            _cachedEpisodeContinue = null;
        }

        _ = UpdateContinueWatchingRailAsync();
    }

    private void ResetIncrementalState()
    {
        _currentPage = 0;
        _hasMoreChannels = true;
        _isLoadingMoreChannels = false;
        Channels = new BatchObservableCollection<Channel>();
        FilteredChannels = new BatchObservableCollection<Channel>(c => !IsDummyChannel(c));
        NotifyContentStateChanged();
    }

    private void ResetSeriesIncrementalState()
    {
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = true;
        _isLoadingMoreSeriesItems = false;
        _seriesFilteredSource = new List<Series>();
        SeriesViewItems = new BatchObservableCollection<Series>();
        NotifyContentStateChanged();
    }

    public async Task LoadMoreChannelsAsync(CancellationToken cancellationToken = default)
    {
        using var trace = _perfTrace?.BeginOperation(
            "LOAD",
            "LoadMoreChannelsAsync",
            $"nav={Volatile.Read(ref _navigationTraceId)} view={ActiveView} type={SelectedChannelType} group={SelectedGroup ?? "<all>"} page={_currentPage}");
        if (cancellationToken.IsCancellationRequested)
        {
            _perfTrace?.Event("LOAD", "LoadMoreChannelsAsync canceled-before-start");
            return;
        }

        if (SelectedPlaylist == null || !_hasMoreChannels || _isLoadingMoreChannels)
        {
            return;
        }

        _isLoadingMoreChannels = true;

        try
        {
            var hasSearch = !string.IsNullOrWhiteSpace(SearchText);
            var effectiveGroup = hasSearch ? null : SelectedGroup;
            var effectiveType = hasSearch ? null : SelectedChannelType;

            var s = _settingsService.Settings;
            var hiddenGroups = effectiveType switch
            {
                ChannelType.Live => s.HiddenLiveGroups,
                ChannelType.VOD => s.HiddenMovieGroups,
                ChannelType.Series => s.HiddenSeriesGroups,
                _ => s.HiddenLiveGroups.Concat(s.HiddenMovieGroups).Concat(s.HiddenSeriesGroups).ToList()
            };

            List<Channel> page;
            using (_perfTrace?.BeginOperation(
                "LOAD",
                "GetChannelsFilteredPageAsync",
                $"playlist={SelectedPlaylist.Id} skip={_currentPage * IncrementalPageSize} take={IncrementalPageSize} type={effectiveType} group={effectiveGroup ?? "<all>"} hidden={hiddenGroups.Count}"))
            {
                page = await _playlistService.GetChannelsFilteredPageAsync(
                    SelectedPlaylist.Id,
                    skip: _currentPage * IncrementalPageSize,
                    take: IncrementalPageSize,
                    searchText: SearchText,
                    group: effectiveGroup,
                    type: effectiveType,
                    onlyFavorites: ShowOnlyFavorites,
                    sortOrder: SelectedSortOrder,
                    hiddenGroups: hiddenGroups);
            }
            _perfTrace?.Counter("LOAD", "GetChannelsFilteredPageAsync count", page.Count, $"page={_currentPage}");

            // If selected group returns nothing on first page, fallback to "all" to avoid false empty UI.
            if (_currentPage == 0 &&
                page.Count == 0 &&
                !hasSearch &&
                !string.IsNullOrWhiteSpace(effectiveGroup))
            {
                List<Channel> fallbackPage;
                using (_perfTrace?.BeginOperation("LOAD", "GetChannelsFilteredPageAsync fallback", $"playlist={SelectedPlaylist.Id} type={effectiveType}"))
                {
                    fallbackPage = await _playlistService.GetChannelsFilteredPageAsync(
                        SelectedPlaylist.Id,
                        skip: 0,
                        take: IncrementalPageSize,
                        searchText: SearchText,
                        group: null,
                        type: effectiveType,
                        onlyFavorites: ShowOnlyFavorites,
                        sortOrder: SelectedSortOrder);
                }

                if (fallbackPage.Count > 0)
                {
                    _suppressFilterRefresh = true;
                    try
                    {
                        SelectedGroup = null;
                    }
                    finally
                    {
                        _suppressFilterRefresh = false;
                    }

                    page = fallbackPage;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (page.Count == 0)
            {
                _hasMoreChannels = false;
                _perfTrace?.Event("LOAD", "LoadMoreChannelsAsync empty-page", $"page={_currentPage}");
                UpdateSearchBuckets();
                return;
            }

            _currentPage++;
            _hasMoreChannels = page.Count == IncrementalPageSize;

            using var uiTrace = _perfTrace?.BeginOperation("LOAD", "LoadMoreChannels UI update", $"items={page.Count}");
            _dispatcherService.Invoke(() =>
            {
                FilteredChannels.AddRange(page);
                if (!ReferenceEquals(Channels, FilteredChannels))
                {
                    Channels.AddRange(page);
                }
                _perfTrace?.Counter("LOAD", "LoadMoreChannelsAsync added", page.Count, $"filtered={FilteredChannels.CountedItemCount} channels={Channels.Count}");
                
                // Fire and forget EPG enrichment for the new page
                _ = EnrichChannelsWithEpgAsync(page);

                OnPropertyChanged(nameof(FilteredChannels));
                NotifyContentStateChanged();
            });

            var isPersonalView = ActiveView == AppView.MyList || ActiveView == AppView.Favorites;
            if (!isPersonalView)
            {
                UpdateMyList();
                UpdateFavoriteChannels();
            }

            UpdateHistoryChannels();
            if (ActiveView == AppView.Downloads)
            {
                UpdateDownloadedItems();
            }
            UpdateSearchBuckets();

            QueueVisibleChannelVisualEnrichment(page);
        }
        finally
        {
            _isLoadingMoreChannels = false;
        }
    }

    public async Task LoadMoreChannelsIfNeededAsync(double verticalOffset, double scrollableHeight)
    {
        if (ActiveView is not (AppView.Home or AppView.Live or AppView.Movies or AppView.Search))
        {
            _perfTrace?.Event("LOAD", "LoadMoreChannelsIfNeeded skipped by view", $"view={ActiveView}");
            return;
        }

        if (scrollableHeight <= 0)
        {
            return;
        }

        if (SelectedPlaylist == null || !_hasMoreChannels || _isLoadingMoreChannels)
        {
            return;
        }

        if ((verticalOffset / scrollableHeight) >= LoadMoreThreshold)
        {
            await LoadMoreChannelsAsync();
        }
    }

    public async Task LoadMoreSeriesIfNeededAsync(double verticalOffset, double scrollableHeight)
    {
        if (ActiveView is not (AppView.Home or AppView.Series or AppView.Search))
        {
            _perfTrace?.Event("LOAD", "LoadMoreSeriesIfNeeded skipped by view", $"view={ActiveView}");
            return;
        }

        if (scrollableHeight <= 0)
        {
            return;
        }

        if (!_hasMoreSeriesItems || _isLoadingMoreSeriesItems || _seriesFilteredSource.Count == 0)
        {
            return;
        }

        if ((verticalOffset / scrollableHeight) >= LoadMoreThreshold)
        {
            await LoadMoreSeriesAsync();
        }
    }

    public Task LoadMoreSeriesAsync()
    {
        using var trace = _perfTrace?.BeginOperation(
            "LOAD",
            "LoadMoreSeriesAsync",
            $"nav={Volatile.Read(ref _navigationTraceId)} source={_seriesFilteredSource.Count} page={_currentSeriesPage}");
        if (!_hasMoreSeriesItems || _isLoadingMoreSeriesItems || _seriesFilteredSource.Count == 0)
        {
            return Task.CompletedTask;
        }

        _isLoadingMoreSeriesItems = true;

        try
        {
            var start = _currentSeriesPage * IncrementalPageSize;
            var page = _seriesFilteredSource.Skip(start).Take(IncrementalPageSize).ToList();
            if (page.Count == 0)
            {
                _hasMoreSeriesItems = false;
                _perfTrace?.Event("LOAD", "LoadMoreSeriesAsync empty-page", $"page={_currentSeriesPage}");
                return Task.CompletedTask;
            }

            _currentSeriesPage++;
            _hasMoreSeriesItems = page.Count == IncrementalPageSize;

            SeriesViewItems.AddRange(page);
            _perfTrace?.Counter("LOAD", "LoadMoreSeriesAsync added", page.Count, $"visible={SeriesViewItems.Count}");
            OnPropertyChanged(nameof(SeriesViewItems));
            NotifyContentStateChanged();

            // On-demand TMDB enrichment for newly visible series
            var enrichPage = page.Where(s => (s.TmdbId == null && s.LastTmdbSync == null) || s.MetadataFetchedAt == null).ToList();
            if (enrichPage.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _tmdbSyncService.EnrichSeriesBatchAsync(enrichPage);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug($"TMDB enrichment for page failed: {ex.Message}");
                    }
                });
            }

            QueueVisibleSeriesVisualEnrichment(page);
        }
        finally
        {
            _isLoadingMoreSeriesItems = false;
        }

        return Task.CompletedTask;
    }

    private bool ShouldUseLazyVisualEnrichment()
        => IsM3UProfile();

    private void QueueVisibleChannelVisualEnrichment(IReadOnlyCollection<Channel> page)
    {
        if (!ShouldUseLazyVisualEnrichment())
        {
            return;
        }

        var candidates = page
            .Where(c => c.Type == ChannelType.VOD && c.Id > 0 &&
                        (!HasDisplayImage(c) || ShouldRefreshProviderImageUrl(c.CoverUrl ?? c.LogoUrl)))
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        _perfTrace?.Event(
            "IMAGE",
            "QueueVisibleChannelVisualEnrichment",
            $"candidates={candidates.Count} page={page.Count}");

        _ = Task.Run(async () =>
        {
            foreach (var channel in candidates)
            {
                try
                {
                    await _channelVisualEnrichmentSemaphore.WaitAsync();
                    await EnrichChannelVisualAsync(channel);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Lazy visual enrichment failed for VOD {Name}", channel.Name);
                }
                finally
                {
                    _channelVisualEnrichmentSemaphore.Release();
                }
            }
        });
    }

    private void QueueVisibleSeriesVisualEnrichment(IReadOnlyCollection<Series> page)
    {
        if (!ShouldUseLazyVisualEnrichment())
        {
            return;
        }

        var candidates = page
            .Where(s => s.Id > 0 &&
                        (!HasDisplayImage(s) || ShouldRefreshProviderImageUrl(s.CoverUrl)))
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        _perfTrace?.Event(
            "IMAGE",
            "QueueVisibleSeriesVisualEnrichment",
            $"candidates={candidates.Count} page={page.Count}");

        _ = Task.Run(async () =>
        {
            foreach (var series in candidates)
            {
                try
                {
                    await _seriesVisualEnrichmentSemaphore.WaitAsync();
                    await EnrichSeriesVisualAsync(series);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Lazy visual enrichment failed for Series {Name}", series.Name);
                }
                finally
                {
                    _seriesVisualEnrichmentSemaphore.Release();
                }
            }
        });
    }

    private async Task EnrichChannelVisualAsync(Channel channel)
    {
        var key = $"vod:{channel.Id}";
        if (!_pendingVisualEnrichmentKeys.TryAdd(key, 1))
        {
            return;
        }

        try
        {
            var currentImageUrl = channel.CoverUrl ?? channel.LogoUrl;
            if (HasDisplayImage(channel) &&
                !await ShouldUseTmdbImageFallbackAsync(currentImageUrl).ConfigureAwait(false))
            {
                return;
            }

            var languageCode = SeriesInfoParser.ExtractLanguageCode(channel.GroupTitle ?? channel.Name);
            var metadata = await _metadataService.FetchMetadataAsync(channel.Name, ChannelType.VOD, languageCode);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.PosterUrl))
            {
                _perfTrace?.Event(
                    "IMAGE",
                    "EnrichChannelVisualAsync no-poster",
                    $"id={channel.Id} name={channel.Name} language={languageCode ?? "<none>"}");
                return;
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            var dbChannel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channel.Id);
            if (dbChannel == null)
            {
                return;
            }

            var changed = false;

            if (!HasDisplayImage(dbChannel) || ShouldRefreshProviderImageUrl(currentImageUrl))
            {
                dbChannel.LogoUrl = metadata.PosterUrl;
                changed = true;
                _perfTrace?.Event(
                    "IMAGE",
                    "EnrichChannelVisualAsync poster",
                    $"id={channel.Id} oldHost={DescribeImageHost(currentImageUrl)} newHost={DescribeImageHost(metadata.PosterUrl)}");
            }

            if (dbChannel.TmdbId == null && metadata.TmdbId.HasValue)
            {
                dbChannel.TmdbId = metadata.TmdbId.Value;
                changed = true;
            }

            dbChannel.LastTmdbSync = DateTime.UtcNow;
            changed = true;

            if (changed)
            {
                await db.SaveChangesAsync();

                await _dispatcherService.InvokeAsync(() =>
                {
                    channel.LogoUrl = dbChannel.LogoUrl;
                    if (string.IsNullOrWhiteSpace(channel.BackdropUrl))
                    {
                        channel.BackdropUrl = dbChannel.BackdropUrl;
                    }
                    channel.TmdbId = dbChannel.TmdbId;
                    channel.LastTmdbSync = dbChannel.LastTmdbSync;
                    channel.NotifyVisualsChanged();
                    return Task.CompletedTask;
                });
            }
        }
        finally
        {
            _pendingVisualEnrichmentKeys.TryRemove(key, out _);
        }
    }

    private async Task EnrichSeriesVisualAsync(Series series)
    {
        var key = $"series:{series.Id}";
        if (!_pendingVisualEnrichmentKeys.TryAdd(key, 1))
        {
            return;
        }

        try
        {
            if (HasDisplayImage(series) &&
                !await ShouldUseTmdbImageFallbackAsync(series.CoverUrl).ConfigureAwait(false))
            {
                return;
            }

            var languageCode = SeriesInfoParser.ExtractLanguageCode(series.GroupTitle ?? series.Genre ?? series.Name);
            var metadata = await _metadataService.SearchSeriesAsync(series.Name, languageCode);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.PosterUrl))
            {
                _perfTrace?.Event(
                    "IMAGE",
                    "EnrichSeriesVisualAsync no-poster",
                    $"id={series.Id} name={series.Name} language={languageCode ?? "<none>"}");
                return;
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            var dbSeries = await db.Series.FirstOrDefaultAsync(s => s.Id == series.Id);
            if (dbSeries == null)
            {
                return;
            }

            var changed = false;

            if (!HasDisplayImage(dbSeries) || ShouldRefreshProviderImageUrl(dbSeries.CoverUrl))
            {
                var oldHost = DescribeImageHost(dbSeries.CoverUrl);
                dbSeries.CoverUrl = metadata.PosterUrl;
                changed = true;
                _perfTrace?.Event(
                    "IMAGE",
                    "EnrichSeriesVisualAsync poster",
                    $"id={series.Id} oldHost={oldHost} newHost={DescribeImageHost(metadata.PosterUrl)}");
            }

            if (string.IsNullOrWhiteSpace(dbSeries.BackdropUrl) && !string.IsNullOrWhiteSpace(metadata.BackdropUrl))
            {
                dbSeries.BackdropUrl = metadata.BackdropUrl;
                changed = true;
            }

            if (dbSeries.TmdbId == null && metadata.TmdbId.HasValue)
            {
                dbSeries.TmdbId = metadata.TmdbId.Value;
                changed = true;
            }

            dbSeries.LastTmdbSync = DateTime.UtcNow;
            changed = true;

            if (changed)
            {
                await db.SaveChangesAsync();

                await _dispatcherService.InvokeAsync(() =>
                {
                    series.CoverUrl = dbSeries.CoverUrl;
                    series.BackdropUrl = dbSeries.BackdropUrl;
                    series.TmdbId = dbSeries.TmdbId;
                    series.LastTmdbSync = dbSeries.LastTmdbSync;
                    return Task.CompletedTask;
                });
            }
        }
        finally
        {
            _pendingVisualEnrichmentKeys.TryRemove(key, out _);
        }
    }

    private async Task<bool> ShouldUseTmdbImageFallbackAsync(string? providerImageUrl)
    {
        if (!IsDisplayImageUrl(providerImageUrl))
        {
            return true;
        }

        if (!ShouldRefreshProviderImageUrl(providerImageUrl))
        {
            return false;
        }

        return !await IsProviderImageReachableAsync(providerImageUrl).ConfigureAwait(false);
    }

    private async Task<bool> IsProviderImageReachableAsync(string? providerImageUrl)
    {
        if (string.IsNullOrWhiteSpace(providerImageUrl))
        {
            return false;
        }

        var normalized = providerImageUrl.Trim().Trim('"', '\'');
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            normalized = "https:" + normalized;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var attempt = 1; attempt <= ProviderImageFallbackAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode && !IsHtmlImageResponse(response))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Provider image probe failed for {Host} attempt {Attempt}", uri.Host, attempt);
            }

            if (attempt < ProviderImageFallbackAttempts)
            {
                await Task.Delay(ProviderImageFallbackDelayMs).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static bool IsHtmlImageResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType != null &&
               (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("application/xhtml", StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressNavigationFilterRefresh)
        {
            _perfTrace?.Event("SEARCH", "OnSearchTextChanged suppressed", $"len={value?.Length ?? 0} view={ActiveView}");
            return;
        }

        _perfTrace?.Event("SEARCH", "OnSearchTextChanged", $"len={value?.Length ?? 0} view={ActiveView}");

        // Debounce logic
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;
        var version = Interlocked.Increment(ref _filterRequestVersion);

        _perfTrace?.Event(
            "FILTER",
            "ScheduleDelayedFilter",
            $"version={version} caller={nameof(OnSearchTextChanged)} delayMs={_filterDelayMs} nav={Volatile.Read(ref _navigationTraceId)} view={ActiveView} type={SelectedChannelType} group={SelectedGroup ?? "<all>"} search={(string.IsNullOrWhiteSpace(SearchText) ? "<empty>" : SearchText)}");

        _ = ApplyFiltersWithDelayAsync(token, version, "search", nameof(OnSearchTextChanged));
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        _perfTrace?.Event(
            "FILTER",
            "OnSelectedGroupChanged",
            $"value={value ?? "<all>"} suppress={_suppressFilterRefresh} navSuppress={_suppressNavigationFilterRefresh} view={ActiveView} type={SelectedChannelType}");

        if (_suppressFilterRefresh || _suppressNavigationFilterRefresh)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            _prioritizeCategoryAction?.Invoke(value);
        }

        ScheduleImmediateFilter();
    }

    partial void OnSelectedChannelTypeChanged(ChannelType? value)
    {
        _perfTrace?.Event(
            "FILTER",
            "OnSelectedChannelTypeChanged",
            $"value={value?.ToString() ?? "<all>"} suppress={_suppressFilterRefresh} navSuppress={_suppressNavigationFilterRefresh} view={ActiveView} group={SelectedGroup ?? "<all>"}");

        UpdateGroupsForSelectedType();
        if (_suppressNavigationFilterRefresh)
        {
            return;
        }

        ScheduleImmediateFilter();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        if (value)
        {
            _slowLoadingWarnCts?.Cancel();
            _slowLoadingWarnCts?.Dispose();
            _slowLoadingWarnCts = new CancellationTokenSource();
            _ = StartSlowLoadingWarningAsync(_slowLoadingWarnCts.Token);
        }
        else
        {
            _slowLoadingWarnCts?.Cancel();
            _slowLoadingWarnCts?.Dispose();
            _slowLoadingWarnCts = null;
            LoadingWarningMessage = string.Empty;
        }

        NotifyContentStateChanged();
    }

    private void NotifyContentStateChanged()
    {
        OnPropertyChanged(nameof(IsContentLoading));
        OnPropertyChanged(nameof(ShowEmptyChannels));
        OnPropertyChanged(nameof(ShowContentFilters));
        OnPropertyChanged(nameof(ShowGroupFilter));
    }

    partial void OnShowOnlyFavoritesChanged(bool value)
    {
        if (_suppressNavigationFilterRefresh)
        {
            return;
        }

        ScheduleImmediateFilter();
    }

    private List<string> OrderGroupsByLanguagePreference(List<string> groups)
    {
        if (groups.Count == 0)
        {
            return groups;
        }

        var preferredCountry = GetPreferredCountryCodeFromLanguage(_settingsService.Settings.Language);
        if (string.IsNullOrWhiteSpace(preferredCountry))
        {
            return groups;
        }

        var preferred = new List<string>();
        var others = new List<string>();
        var adult = new List<string>();

        foreach (var group in groups)
        {
            if (IsAdultGroup(group))
            {
                adult.Add(group);
            }
            else if (IsCountryPreferredGroup(group, preferredCountry))
            {
                preferred.Add(group);
            }
            else
            {
                others.Add(group);
            }
        }

        // Sort each segment alphabetically for better UX
        preferred.Sort(StringComparer.OrdinalIgnoreCase);
        others.Sort(StringComparer.OrdinalIgnoreCase);
        adult.Sort(StringComparer.OrdinalIgnoreCase);

        var result = new List<string>();
        result.AddRange(preferred);
        result.AddRange(others);
        result.AddRange(adult);
        return result;
    }

    private static bool IsAdultGroup(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return AdultContentRegex().IsMatch(name);
    }

    private void EnsurePreferredDefaultGroupSelected()
    {
        if (Groups.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedGroup) && Groups.Contains(SelectedGroup))
        {
            return;
        }

        var preferredCountry = GetPreferredCountryCodeFromLanguage(_settingsService.Settings.Language);
        var preferred = Groups.FirstOrDefault(g => IsCountryPreferredGroup(g, preferredCountry));

        _suppressFilterRefresh = true;
        try
        {
            // Eğer dil koduyla eşleşen grup yoksa, listenin en başındaki (Adult olmayan) grubu seç
            SelectedGroup = preferred ?? Groups.FirstOrDefault();
            _perfTrace?.Event("FILTER", "EnsurePreferredDefaultGroupSelected", $"selected={SelectedGroup ?? "<all>"} preferred={preferred ?? "<none>"} count={Groups.Count}");
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private void UpdateGroupsForSelectedType()
    {
        var s = _settingsService.Settings;

        // Eğer SelectedChannelType null ise ActiveView'den türetmeyi dene
        var effectiveType = SelectedChannelType ?? ActiveView switch
        {
            AppView.Live => ChannelType.Live,
            AppView.Movies => ChannelType.VOD,
            AppView.Series => ChannelType.Series,
            _ => (ChannelType?)null
        };

        var nextGroups = effectiveType switch
        {
            ChannelType.Live => _liveGroupsCache.Where(g => !s.HiddenLiveGroups.Contains(g)).ToList(),
            ChannelType.VOD => _vodGroupsCache.Where(g => !s.HiddenMovieGroups.Contains(g)).ToList(),
            ChannelType.Series => _seriesGroupsCache.Where(g => !s.HiddenSeriesGroups.Contains(g)).ToList(),
            _ => _allGroupsCache.Where(g => !s.HiddenLiveGroups.Contains(g) && !s.HiddenMovieGroups.Contains(g) && !s.HiddenSeriesGroups.Contains(g)).ToList()
        };

        _dispatcherService.Invoke(() => Groups = new BatchObservableCollection<string>(nextGroups));
        _perfTrace?.Event(
            "FILTER",
            "UpdateGroupsForSelectedType",
            $"effectiveType={effectiveType?.ToString() ?? "<all>"} groups={nextGroups.Count} selected={SelectedGroup ?? "<all>"} view={ActiveView}");

        if (!string.IsNullOrWhiteSpace(SelectedGroup) && !Groups.Contains(SelectedGroup))
        {
            _suppressFilterRefresh = true;
            try
            {
                _perfTrace?.Event("FILTER", "SelectedGroup invalidated", $"old={SelectedGroup} effectiveType={effectiveType?.ToString() ?? "<all>"}");
                SelectedGroup = null;
            }
            finally
            {
                _suppressFilterRefresh = false;
            }
        }

        if (SelectedChannelType.HasValue)
        {
            EnsurePreferredDefaultGroupSelected();
        }
    }

    private void ReorderGroupCachesFromLanguagePreference()
    {
        _allGroupsCache = OrderGroupsByLanguagePreference(_allGroupsCache);
        _liveGroupsCache = OrderGroupsByLanguagePreference(_liveGroupsCache);
        _vodGroupsCache = OrderGroupsByLanguagePreference(_vodGroupsCache);
        _seriesGroupsCache = OrderGroupsByLanguagePreference(_seriesGroupsCache);
    }

    private static bool IsCountryPreferredGroup(string group, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        var normalized = group.Trim();
        return normalized.StartsWith(countryCode + "/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(countryCode + "|", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(countryCode + " |", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(countryCode + ":", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(countryCode + "-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(countryCode + "_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(countryCode + " ", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(countryCode, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith($"|{countryCode}|", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith($"[{countryCode}]", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith($"({countryCode})", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPreferredCountryCodeFromLanguage(string? language)
    {
        var lang = (language ?? string.Empty).Trim().ToLowerInvariant();
        return lang switch
        {
            "tr" => "TR",
            "en" => "US",
            "de" => "DE",
            "fr" => "FR",
            "es" => "ES",
            "it" => "IT",
            "ar" => "SA",
            _ => "TR"
        };
    }

    partial void OnSelectedSortOrderChanged(ChannelSortOrder value)
    {
        ScheduleImmediateFilter();
    }

    public void ScheduleImmediateFilter(string reason = "immediate", [CallerMemberName] string caller = "")
    {
        var version = Interlocked.Increment(ref _filterRequestVersion);
        _perfTrace?.Event(
            "FILTER",
            "ScheduleImmediateFilter",
            $"version={version} caller={caller} reason={reason} running={Volatile.Read(ref _isFilterApplyRunning)} pending={Volatile.Read(ref _pendingFilterRequest)} nav={Volatile.Read(ref _navigationTraceId)} view={ActiveView} type={SelectedChannelType} group={SelectedGroup ?? "<all>"} search={(string.IsNullOrWhiteSpace(SearchText) ? "<empty>" : SearchText)}");
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        _ = ApplyImmediateFilterAsync(token, version, reason, caller);
    }

    private async Task ApplyImmediateFilterAsync(CancellationToken token, int version, string reason, string caller)
    {
        try
        {
            await Task.Delay(ImmediateFilterCoalesceDelayMs, token);
            await RunCoalescedFilterAsync(token, version, reason, caller);
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer filter request supersedes this one.
        }
        catch (ObjectDisposedException)
        {
            // Ignore races from rapid filter token replacement.
        }
    }

    private async Task ApplyFiltersWithDelayAsync(CancellationToken token, int version, string reason, string caller)
    {
        try
        {
            await Task.Delay(_filterDelayMs, token);
            await RunCoalescedFilterAsync(token, version, reason, caller);
        }
        catch (OperationCanceledException)
        {
            // Expected for debounce scenarios.
        }
        catch (ObjectDisposedException)
        {
            // Ignore races from rapid filter token replacement.
        }
    }

    private async Task RunCoalescedFilterAsync(CancellationToken token, int version, string reason, string caller)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isFilterApplyRunning, 1) == 1)
        {
            Interlocked.Exchange(ref _pendingFilterRequest, 1);
            _pendingFilterReason = reason;
            _pendingFilterCaller = caller;
            _perfTrace?.Event(
                "FILTER",
                "Filter request coalesced",
                $"version={version} latest={Volatile.Read(ref _filterRequestVersion)} caller={caller} reason={reason} nav={Volatile.Read(ref _navigationTraceId)} view={ActiveView} type={SelectedChannelType} group={SelectedGroup ?? "<all>"}");
            return;
        }

        try
        {
            var currentToken = token;
            var currentReason = reason;
            var currentCaller = caller;

            while (true)
            {
                Interlocked.Exchange(ref _pendingFilterRequest, 0);
                currentToken = _filterCts?.Token ?? currentToken;
                var currentVersion = Volatile.Read(ref _filterRequestVersion);
                if (currentToken.IsCancellationRequested)
                {
                    _perfTrace?.Event("FILTER", "Filter request canceled-before-run", $"version={currentVersion} caller={currentCaller} reason={currentReason}");
                    break;
                }

                var signature = BuildFilterSignature();
                if (ShouldSkipDuplicateFilter(signature))
                {
                    _perfTrace?.Event("FILTER", "Filter duplicate skipped", $"version={currentVersion} caller={currentCaller} reason={currentReason} signature={signature}");
                }
                else
                {
                    _perfTrace?.Event("FILTER", "Filter request run", $"version={currentVersion} caller={currentCaller} reason={currentReason} signature={signature}");
                    var applied = await ApplyFiltersAsync(currentToken);
                    if (applied)
                    {
                        MarkCompletedFilter(signature);
                    }
                }

                if (Interlocked.Exchange(ref _pendingFilterRequest, 0) == 0)
                {
                    break;
                }

                currentReason = _pendingFilterReason ?? "pending";
                currentCaller = _pendingFilterCaller ?? currentCaller;
                _pendingFilterReason = null;
                _pendingFilterCaller = null;
                _perfTrace?.Event("FILTER", "Filter pending replay", $"latest={Volatile.Read(ref _filterRequestVersion)} caller={currentCaller} reason={currentReason}");

                currentToken = _filterCts?.Token ?? currentToken;
                await Task.Delay(ImmediateFilterCoalesceDelayMs, currentToken);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isFilterApplyRunning, 0);
            if (Interlocked.Exchange(ref _pendingFilterRequest, 0) == 1)
            {
                ScheduleImmediateFilter("pending-after-release", nameof(RunCoalescedFilterAsync));
            }
        }
    }

    private string BuildFilterSignature()
    {
        var playlistId = SelectedPlaylist?.Id ?? 0;
        var group = string.IsNullOrWhiteSpace(SelectedGroup) ? string.Empty : SelectedGroup.Trim();
        var search = string.IsNullOrWhiteSpace(SearchText) ? string.Empty : SearchText.Trim();
        return string.Join(
            "|",
            playlistId.ToString(CultureInfo.InvariantCulture),
            ActiveView,
            SelectedChannelType?.ToString() ?? string.Empty,
            group,
            ShowOnlyFavorites.ToString(CultureInfo.InvariantCulture),
            SelectedSortOrder,
            search);
    }

    private bool ShouldSkipDuplicateFilter(string signature)
    {
        return string.Equals(_lastCompletedFilterSignature, signature, StringComparison.Ordinal) &&
               (DateTime.UtcNow - _lastCompletedFilterUtc).TotalMilliseconds <= DuplicateFilterSuppressWindowMs;
    }

    private void MarkCompletedFilter(string signature)
    {
        _lastCompletedFilterSignature = signature;
        _lastCompletedFilterUtc = DateTime.UtcNow;
    }

    private async Task<bool> ApplyFiltersAsync(CancellationToken token)
    {
        using var trace = _perfTrace?.BeginOperation(
            "FILTER",
            "ApplyFiltersAsync",
            $"nav={Volatile.Read(ref _navigationTraceId)} view={ActiveView} type={SelectedChannelType} group={SelectedGroup ?? "<all>"} search={(string.IsNullOrWhiteSpace(SearchText) ? "<empty>" : SearchText)}");
        if (SelectedPlaylist == null || token.IsCancellationRequested) return false;

        BeginLoading();

        try
        {
            if (token.IsCancellationRequested) return false;
            var view = ActiveView;
            var needsChannels = view is AppView.Home or AppView.Live or AppView.Movies or AppView.Search;
            var needsSeries = view is AppView.Home or AppView.Series or AppView.Search;

            _perfTrace?.Event("FILTER", "ApplyFilters view-plan", $"view={view} channels={needsChannels} series={needsSeries}");

            if (needsChannels)
            {
                ResetIncrementalState();
            }
            else
            {
                _hasMoreChannels = false;
                _isLoadingMoreChannels = false;
                FilteredChannels = new BatchObservableCollection<Channel>(c => !IsDummyChannel(c));
                _perfTrace?.Event("FILTER", "Preserve channel cache for non-grid view", $"view={view} channels={Channels?.Count ?? 0}");
                NotifyContentStateChanged();
            }

            if (needsSeries)
            {
                ResetSeriesIncrementalState();
            }
            else
            {
                _hasMoreSeriesItems = false;
                _isLoadingMoreSeriesItems = false;
                _seriesFilteredSource.Clear();
                SeriesViewItems = new BatchObservableCollection<Series>();
                NotifyContentStateChanged();
            }

            if (token.IsCancellationRequested) return false;

            if (needsChannels)
            {
                await LoadMoreChannelsAsync(token);
            }
            else
            {
                _perfTrace?.Event("FILTER", "LoadMoreChannels skipped by view", $"view={view}");
            }

            if (token.IsCancellationRequested) return false;

            if (needsSeries)
            {
                UpdateSeriesViewItems();
            }
            else
            {
                _perfTrace?.Event("FILTER", "UpdateSeriesViewItems skipped by view", $"view={view}");
            }

            return !token.IsCancellationRequested;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException) return false;

            _logger?.LogDebug($"ApplyFilters error: {ex}");
            StatusMessage = _localizationService.GetString("Main.Status.FilterError");
            return false;
        }
        finally
        {
            EndLoading();
        }
    }

    /// <summary>
    /// EPG panelinden bir kanala tıklandığında o kanalı doğrudan oynatır.
    /// </summary>
    [RelayCommand]
    private void SelectChannelFromEpg(Channel channel)
    {
        if (channel == null) return;
        SelectChannel(channel);
    }

    [RelayCommand]
    private void PlayNextLiveChannel()
    {
        if (_livePlaybackContext == null || _livePlaybackContext.Count == 0 || SelectedChannel == null)
        {
            return;
        }

        var currentIndex = _livePlaybackContext.FindIndex(c => c.Id == SelectedChannel.Id);
        if (currentIndex == -1) 
        {
            return;
        }

        var nextIndex = (currentIndex + 1) % _livePlaybackContext.Count;
        SelectChannel(_livePlaybackContext[nextIndex]);
    }

    [RelayCommand]
    private void PlayPreviousLiveChannel()
    {
        if (_livePlaybackContext == null || _livePlaybackContext.Count == 0 || SelectedChannel == null)
        {
            return;
        }

        var currentIndex = _livePlaybackContext.FindIndex(c => c.Id == SelectedChannel.Id);
        if (currentIndex == -1)
        {
            return;
        }

        var prevIndex = currentIndex - 1;
        if (prevIndex < 0) prevIndex = _livePlaybackContext.Count - 1;

        SelectChannel(_livePlaybackContext[prevIndex]);
    }

    [RelayCommand]
    private void SelectChannel(Channel channel)
    {
        var selectionVersion = BeginMediaSelectionIntent();

        if (channel.Type == ChannelType.Series)
        {
            TryPrepareEpisodePlaybackContext(channel);
        }
        else
        {
            CurrentEpisodePlaybackContext = null;
            NextEpisodePlaybackContext = null;
            CurrentSeriesPlaybackContext = null;
            
            if (channel.Type == ChannelType.Live)
            {
                // Is this channel in our currently displayed FilteredChannels?
                if (ActiveView == AppView.Live && FilteredChannels.Any(c => c.Id == channel.Id))
                {
                    _livePlaybackContext = FilteredChannels.ToList();
                }
                else
                {
                    // Fire and forget deep DB fallback. Keep the selection token so
                    // a slow DB lookup cannot overwrite the live zapping context
                    // after the user has already moved to another channel.
                    _ = RefreshLivePlaybackContextAsync(channel, selectionVersion);
                }
            }
            else
            {
                _livePlaybackContext = null;
            }
        }
        
        if (!IsMediaSelectionIntentCurrent(selectionVersion))
        {
            return;
        }

        SelectedChannel = channel;
        
        // Update last watched
        channel.LastWatched = DateTime.UtcNow;
        _ = _channelService.UpdateChannelAsync(channel);
        UpdateHistoryChannels();

        // Notify UI to play
        OnMediaSelected?.Invoke(channel);
    }

    private async Task RefreshLivePlaybackContextAsync(Channel targetChannel, int selectionVersion)
    {
        try
        {
            var profileId = CurrentProfileId;
            if (profileId == null) return;

            using var db = await _contextFactory.CreateDbContextAsync();
            
            // Validate playlist is still active
            var playlistExists = await db.Playlists
                .AsNoTracking()
                .AnyAsync(p => p.Id == targetChannel.PlaylistId && p.ProfileId == profileId && p.IsActive);
                
            if (!playlistExists)
            {
                if (IsMediaSelectionIntentCurrent(selectionVersion) && SelectedChannel?.Id == targetChannel.Id)
                {
                    _livePlaybackContext = null;
                }
                return;
            }

            var groupChannels = await db.Channels
                .AsNoTracking()
                .Where(c => c.PlaylistId == targetChannel.PlaylistId 
                         && c.GroupTitle == targetChannel.GroupTitle 
                         && c.Type == ChannelType.Live)
                .ToListAsync();

            if (groupChannels.Count == 0)
            {
                if (IsMediaSelectionIntentCurrent(selectionVersion) && SelectedChannel?.Id == targetChannel.Id)
                {
                    _livePlaybackContext = null;
                }
                return;
            }

            var liveContext = SelectedSortOrder switch
            {
                ChannelSortOrder.NameAsc => groupChannels.OrderBy(c => c.Name).ToList(),
                ChannelSortOrder.NameDesc => groupChannels.OrderByDescending(c => c.Name).ToList(),
                ChannelSortOrder.OldestFirst => groupChannels.OrderBy(c => c.Id).ToList(),
                _ => groupChannels.OrderByDescending(c => c.Id).ToList()
            };

            if (!IsMediaSelectionIntentCurrent(selectionVersion) || SelectedChannel?.Id != targetChannel.Id)
            {
                return;
            }

            _livePlaybackContext = liveContext;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"RefreshLivePlaybackContextAsync failed: {ex.Message}");
            if (IsMediaSelectionIntentCurrent(selectionVersion) && SelectedChannel?.Id == targetChannel.Id)
            {
                _livePlaybackContext = null;
            }
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(object media)
    {
        if (!CurrentProfileId.HasValue)
        {
            StatusMessage = _localizationService.GetString("Main.Status.SelectProfileFirst");
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync();

        if (media is Channel channel)
        {
            if (channel.PlaylistId <= 0)
            {
                channel.PlaylistId = SelectedPlaylist?.Id
                    ?? await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
            }

            channel.IsFavorite = !channel.IsFavorite;
            await _channelService.UpdateChannelAsync(channel);
            StatusMessage = channel.IsFavorite
                ? _localizationService.GetString("Main.Status.FavoriteAdded")
                : _localizationService.GetString("Main.Status.FavoriteRemoved");
        }
        else if (media is Series series)
        {
            if (series.PlaylistId <= 0)
            {
                series.PlaylistId = SelectedPlaylist?.Id
                    ?? await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
            }

            var seriesFromDb = await db.Series
                .Include(s => s.Seasons)
                .ThenInclude(sn => sn.Episodes)
                .FirstOrDefaultAsync(s =>
                    s.Id == series.Id ||
                    (s.PlaylistId == series.PlaylistId && s.Name == series.Name));

            if (seriesFromDb == null)
            {
                return;
            }

            var shouldFavorite = !seriesFromDb.IsFavorite;
            seriesFromDb.IsFavorite = shouldFavorite;
            series.IsFavorite = shouldFavorite;

            var episodeUrls = seriesFromDb.Seasons
                .SelectMany(sn => sn.Episodes)
                .Select(ep => ep.StreamUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();

            if (episodeUrls.Count > 0)
            {
                var relatedChannels = await db.Channels
                    .Where(c => episodeUrls.Contains(c.StreamUrl))
                    .ToListAsync();

                foreach (var relatedChannel in relatedChannels)
                {
                    relatedChannel.IsFavorite = shouldFavorite;
                }
            }

            await db.SaveChangesAsync();
            StatusMessage = shouldFavorite
                ? _localizationService.GetString("Main.Status.FavoriteAdded")
                : _localizationService.GetString("Main.Status.FavoriteRemoved");
        }
        else
        {
            return;
        }

        UpdateMyList();
        UpdateFavoriteChannels();
        UpdateHistoryChannels();
        await RefreshPersonalListsFromDatabaseAsync();
        ScheduleImmediateFilter();
    }

    [RelayCommand]
    private async Task AddPlaylistFromUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName) || string.IsNullOrWhiteSpace(NewPlaylistUrl))
        {
            StatusMessage = _localizationService.GetString("Main.Status.EnterPlaylistInfo");
            return;
        }

        if (Interlocked.Exchange(ref _isAddingPlaylist, 1) == 1)
        {
            StatusMessage = _localizationService.GetString("Main.Status.AddInProgress");
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = _localizationService.GetString("Main.Status.AddingPlaylist");
            
            var playlist = await _playlistService.AddFromUrlAsync(NewPlaylistName, NewPlaylistUrl, CurrentProfileId);
            await LoadPlaylistsAsync();
            SelectedPlaylist = playlist;
            
            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Main.Status.PlaylistAddedFormat"),
                NewPlaylistName,
                playlist.ChannelCount);
            NewPlaylistName = string.Empty;
            NewPlaylistUrl = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.OperationFailed"), ex);
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref _isAddingPlaylist, 0);
        }
    }

    [RelayCommand]
    private async Task RefreshPlaylistAsync()
    {
        await RefreshSelectedPlaylistAsync();
    }

    public async Task RefreshSelectedPlaylistAsync(bool isBackground = false)
    {
        using var trace = _perfTrace?.BeginOperation(
            "REFRESH",
            "RefreshSelectedPlaylistAsync",
            $"background={isBackground} profile={CurrentProfileId?.ToString() ?? "<none>"} playlist={SelectedPlaylist?.Id.ToString() ?? "<none>"}");
        if (SelectedPlaylist == null)
        {
            _perfTrace?.Event("REFRESH", "RefreshSelectedPlaylist no selected playlist", $"background={isBackground}");
            if (CurrentProfile != null && !isBackground)
            {
                // If profile has no playlist record, try to load/create it
                await LoadProfileAsync(CurrentProfile);
            }
            return;
        }

        var playlistId = SelectedPlaylist.Id;

        if (!isBackground &&
            _playlistNoChangeUntilUtc.TryGetValue(playlistId, out var noChangeUntil) &&
            noChangeUntil > DateTime.UtcNow)
        {
            StatusMessage = _localizationService.GetString("Main.Status.AlreadyUpToDate");
            _perfTrace?.Event("REFRESH", "RefreshSelectedPlaylist no-change-cache", $"playlist={playlistId}");
            await TouchPlaylistLastUpdatedAsync(playlistId);
            return;
        }

        if (Interlocked.Exchange(ref _isRefreshingPlaylist, 1) == 1)
        {
            if (!isBackground)
            {
                StatusMessage = _localizationService.GetString("Main.Status.PlaylistRefreshAlreadyInProgress");
            }
            _perfTrace?.Event("REFRESH", "RefreshSelectedPlaylist skipped busy", $"playlist={playlistId}");
            return;
        }

        if (!isBackground)
        {
            if (Interlocked.Exchange(ref _isManualRefreshRunning, 1) == 1)
            {
                StatusMessage = _localizationService.GetString("Main.Status.OtherRefreshInProgress");
                Interlocked.Exchange(ref _isRefreshingPlaylist, 0);
                _perfTrace?.Event("REFRESH", "RefreshSelectedPlaylist skipped manual-refresh-busy", $"playlist={playlistId}");
                return;
            }
        }

        try
        {
            if (!isBackground)
            {
                BeginLoading();
                StatusMessage = _localizationService.GetString("Main.Status.RefreshingChannels");
            }

            var profile = CurrentProfile;

            if (profile != null && profile.ProviderAccount != null)
            {
                if (profile.ProviderAccount.Type == ProfileType.StalkerPortal)
                {
                    _perfTrace?.Event("REFRESH", "RefreshSelectedPlaylist provider", $"type=Stalker playlist={playlistId}");
                    await ResumeStalkerProgressiveLoadingAsync(profile, SelectedPlaylist, isFullRefresh: true);
                    if (!isBackground)
                    {
                        _playlistNoChangeUntilUtc[playlistId] = DateTime.UtcNow.AddMinutes(5);
                    }
                    return;
                }
                else if (profile.ProviderAccount.Type == ProfileType.XtreamCodes)
                {
                    _perfTrace?.Event("REFRESH", "RefreshSelectedPlaylist provider", $"type=Xtream playlist={playlistId}");
                    await ResumeXtreamProgressiveLoadingAsync(profile, SelectedPlaylist, isFullRefresh: true);
                    if (!isBackground)
                    {
                        _playlistNoChangeUntilUtc[playlistId] = DateTime.UtcNow.AddMinutes(5);
                    }
                    return;
                }
            }

            // Varsayılan M3U mantığı
            int beforeCount;
            int afterCount;
            using (_perfTrace?.BeginOperation("REFRESH", "GetChannelCount before", $"playlist={playlistId}"))
            {
                beforeCount = await _playlistService.GetChannelCountAsync(playlistId);
            }

            using (_perfTrace?.BeginOperation("REFRESH", "PlaylistService.RefreshAsync", $"playlist={playlistId}"))
            {
                await _playlistService.RefreshAsync(playlistId);
            }

            using (_perfTrace?.BeginOperation("REFRESH", "GetChannelCount after", $"playlist={playlistId}"))
            {
                afterCount = await _playlistService.GetChannelCountAsync(playlistId);
            }
            _perfTrace?.Counter("REFRESH", "ChannelCountDelta", afterCount - beforeCount, $"before={beforeCount} after={afterCount}");
            
            // "Güvenli Sıfırlama" sonrası tüm kanallar silinip baştan eklendiği için
            // eklenen/silinen farkı 0 olsa dahi kategoriler değişmiş olabilir. 
            // Bu yüzden LoadChannelsAsync her zaman çağrılmalı.
            if (!isBackground)
            {
                ResetUIForRefresh();
            }
            await LoadChannelsAsync(playlistId);

            if (!isBackground)
            {
                var delta = afterCount - beforeCount;
                StatusMessage = delta == 0
                    ? _localizationService.GetString("Main.Status.PlaylistUpdated")
                    : string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("Main.Status.PlaylistRefreshedWithDelta"),
                        Math.Abs(delta));
                    
                ChannelListLastError = null;
                _playlistNoChangeUntilUtc[playlistId] = DateTime.UtcNow.AddMinutes(5);
            }
        }
        catch (Exception ex)
        {
            if (!isBackground)
            {
                var errorMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Settings.Refresh.Channel.Error"), ex);
                StatusMessage = errorMessage;
                ChannelListLastError = UserFriendlyErrorMessage.FromException(ex);

                // Refresh başarısızsa: UI'yi boş bırakma, mevcut DB içeriğini geri yükle.
                // LoadChannelsAsync kendi status mesajlarını yazacağı için, hata mesajını en sonda tekrar basıyoruz.
                try
                {
                    await LoadChannelsAsync(playlistId);
                }
                catch
                {
                    // Restore da başarısız olursa, orijinal hata mesajı zaten gösteriliyor.
                }

                StatusMessage = errorMessage;
            }
        }
        finally
        {
            if (!isBackground)
            {
                EndLoading();
                Interlocked.Exchange(ref _isManualRefreshRunning, 0);
            }

            Interlocked.Exchange(ref _isRefreshingPlaylist, 0);
        }
    }

    private async Task TouchPlaylistLastUpdatedAsync(int playlistId)
    {
        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
            if (playlist == null)
            {
                return;
            }

            playlist.LastUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (SelectedPlaylist?.Id == playlistId)
            {
                SelectedPlaylist.LastUpdated = playlist.LastUpdated;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"TouchPlaylistLastUpdatedAsync failed: {ex}");
        }
    }

    [RelayCommand]
    private void HideGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        var s = _settingsService.Settings;
        var list = SelectedChannelType switch
        {
            ChannelType.Live => s.HiddenLiveGroups,
            ChannelType.VOD => s.HiddenMovieGroups,
            ChannelType.Series => s.HiddenSeriesGroups,
            _ => null
        };

        if (list == null) return;

        if (!list.Contains(groupName))
        {
            list.Add(groupName);
            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Main.Status.CategoryHiddenFormat"), groupName);
            
            // Arayüzden anında kaldır (akıcılık için)
            if (Groups.Contains(groupName))
            {
                Groups.Remove(groupName);
            }
            
            if (SelectedGroup == groupName)
            {
                SelectedGroup = null; // Bu işlem otomatik filtrelemeyi tetikler
            }
            else
            {
                ScheduleImmediateFilter();
            }

            // Diske yazma işlemini arayüzü dondurmamak için arka planda yap
            _ = Task.Run(async () => 
            {
                try { await _settingsService.SaveAsync(); } catch { }
            });
        }
    }

    [RelayCommand]
    private async Task UnhideGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        var s = _settingsService.Settings;
        // Search in all lists
        bool removed = false;
        if (s.HiddenLiveGroups.Remove(groupName)) removed = true;
        if (s.HiddenMovieGroups.Remove(groupName)) removed = true;
        if (s.HiddenSeriesGroups.Remove(groupName)) removed = true;

        if (removed)
        {
            await _settingsService.SaveAsync();
            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Main.Status.CategoryUnhidden"), groupName);
            ScheduleImmediateFilter();
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedGroup = null;
        SelectedChannelType = null;
        ShowOnlyFavorites = false;
    }

    [RelayCommand]
    public async Task LoadEpgAsync(bool isBackgroundSync = false)
    {
        await LoadEpgInternalAsync(isBackgroundSync, forceRefresh: false, setBusyState: true);
    }

    public async Task ForceRefreshEpgAsync()
    {
        // Force refresh should not lock the whole UI.
        await LoadEpgInternalAsync(isBackgroundSync: false, forceRefresh: true, setBusyState: false);
    }

    public bool ForceRefreshEpgInBackground()
    {
        if (Volatile.Read(ref _isManualRefreshRunning) == 1 ||
            Volatile.Read(ref _isManualEpgRefreshRunning) == 1)
        {
            StatusMessage = _localizationService.GetString("Main.Status.OtherRefreshInProgress");
            _perfTrace?.Event("EPG", "ForceRefreshEpgInBackground skipped", "busy");
            return false;
        }

        _ = RunManualEpgRefreshBackgroundAsync();
        return true;
    }

    private async Task RunManualEpgRefreshBackgroundAsync()
    {
        try
        {
            StatusMessage = _localizationService.GetString("Main.Status.EpgRefreshStarted");
            await LoadEpgInternalAsync(isBackgroundSync: false, forceRefresh: true, setBusyState: false);
        }
        catch (Exception ex)
        {
            await PersistSelectedPlaylistEpgErrorAsync($"Manual: {UserFriendlyErrorMessage.FromException(ex)}");
            _logger?.LogDebug($"Manual EPG refresh failed: {ex}");
        }
    }

    private async Task LoadEpgInternalAsync(bool isBackgroundSync, bool forceRefresh = false, bool setBusyState = true)
    {
        using var trace = _perfTrace?.BeginOperation(
            "EPG",
            "LoadEpgInternalAsync",
            $"background={isBackgroundSync} force={forceRefresh} busy={setBusyState} profile={CurrentProfileId?.ToString() ?? "<none>"} playlist={SelectedPlaylist?.Id.ToString() ?? "<none>"}");
        if (CurrentProfile == null) return;

        if (!isBackgroundSync)
        {
            if (Interlocked.Exchange(ref _isManualEpgRefreshRunning, 1) == 1)
            {
                StatusMessage = _localizationService.GetString("Main.Status.EpgRefreshInProgress");
                return;
            }

            if (Interlocked.Exchange(ref _isManualRefreshRunning, 1) == 1)
            {
                StatusMessage = _localizationService.GetString("Main.Status.OtherRefreshInProgress");
                Interlocked.Exchange(ref _isManualEpgRefreshRunning, 0);
                return;
            }
        }

        try
        {
            _epgService.ClearLastError();
            if (!isBackgroundSync && setBusyState)
            {
                IsLoading = true;
                StatusMessage = _localizationService.GetString("Main.Status.UpdatingEpg");
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            _perfTrace?.Event("EPG", "DbContext created");

            // Clear old errors from the database before starting the long-running download
            if (SelectedPlaylist != null)
            {
                var playlistToUpdate = await db.Playlists.FirstOrDefaultAsync(p => p.Id == SelectedPlaylist.Id);
                if (playlistToUpdate != null && !string.IsNullOrWhiteSpace(playlistToUpdate.EpgLastError))
                {
                    playlistToUpdate.EpgLastError = null;
                    await db.SaveChangesAsync();
                    SelectedPlaylist.EpgLastError = null;
                }
            }

            IEnumerable<Channel> channelsForMapping = Channels;
            if (SelectedPlaylist != null)
            {
                using (_perfTrace?.BeginOperation("EPG", "GetChannelsForMapping", $"playlist={SelectedPlaylist.Id}"))
                {
                    channelsForMapping = await _playlistService.GetChannelsAsync(SelectedPlaylist.Id);
                }
            }

            // EPG is relevant for live channels only.
            var liveChannels = channelsForMapping
                .Where(c => c.Type == ChannelType.Live)
                .ToList();
            _perfTrace?.Counter("EPG", "LiveChannelsForMapping", liveChannels.Count, $"playlist={SelectedPlaylist?.Id.ToString() ?? "<none>"}");

            if (liveChannels.Count == 0)
            {
                if (!isBackgroundSync)
                {
                    StatusMessage = _localizationService.GetString("Main.Status.NoLiveChannelsSkipEpg");
                }
                _perfTrace?.Event("EPG", "LoadEpgInternal no-live-channels");
                return;
            }

            // Initial EPG visibility check: ensure we have programs in DB if not refreshing
            if (!forceRefresh && SelectedPlaylist != null)
            {
                bool hasCachedPrograms;
                using (_perfTrace?.BeginOperation("EPG", "HasEpgForChannelsAsync", $"channels={liveChannels.Count}"))
                {
                    hasCachedPrograms = await HasEpgForChannelsAsync(db, liveChannels);
                }
                var refreshThresholdHours = _settingsService.Settings.EpgRefreshFrequencyHours;
                _perfTrace?.Event("EPG", "CacheCheck", $"hasCached={hasCachedPrograms} thresholdHours={refreshThresholdHours}");

                if (hasCachedPrograms)
                {
                    // If refresh frequency is 0 (Manual) and we have data, skip.
                    if (refreshThresholdHours <= 0)
                    {
                        if (isBackgroundSync) return;
                    }
                    else
                    {
                        // If EPG was updated recently (within the threshold), skip.
                        var lastUpdated = SelectedPlaylist.EpgLastUpdated ?? DateTime.MinValue;
                        if ((DateTime.UtcNow - lastUpdated).TotalHours < refreshThresholdHours)
                        {
                            _perfTrace?.Event("EPG", "LoadEpgInternal cached-skip", $"lastUpdated={lastUpdated:o}");
                            if (isBackgroundSync) return;
                        }
                    }
                }
            }

            // 1. Provider EPG URL & Headers (Xtream / Stalker / Inferred)
            string? providerEpgUrl = null;
            Dictionary<string, string>? providerHeaders = null;

            if (CurrentProfile.ProviderAccount?.Type == ProfileType.XtreamCodes)
            {
                var baseUrl = CurrentProfile.ProviderAccount.Url.TrimEnd('/');
                if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
                var decryptedPassword = _securityService.Decrypt(CurrentProfile.ProviderAccount.Password) ?? "";
                providerEpgUrl = $"{baseUrl}/xmltv.php?username={Uri.EscapeDataString(CurrentProfile.ProviderAccount.Username ?? "")}&password={Uri.EscapeDataString(decryptedPassword)}";
            }
            else if (CurrentProfile.ProviderAccount?.Type == ProfileType.StalkerPortal)
            {
                var baseUrl = CurrentProfile.ProviderAccount.Url.TrimEnd('/');
                var macAddress = CurrentProfile.ProviderAccount.Username ?? "";
                providerEpgUrl = _stalkerPortalService.GetEpgUrl(baseUrl);
                
                providerHeaders = new Dictionary<string, string>
                {
                    ["Cookie"] = $"mac={macAddress}; stb_lang=en; timezone=Europe/Istanbul"
                };
            }
            else if (CurrentProfile.ProviderAccount?.Type == ProfileType.M3U && !string.IsNullOrWhiteSpace(SelectedPlaylist?.Url))
            {
                // M3U olarak eklenmiş ama Xtream formatındaysa EPG URL'ini tahmin et
                providerEpgUrl = _epgSourceResolver.TryInferXtreamEpgUrl(SelectedPlaylist.Url);
            }

            // 2. EPG kaynaklarını topla
            var appLanguage = (_settingsService.Settings.Language ?? "tr").ToUpperInvariant();
            var playlistEpgUrl = (SelectedPlaylist?.EpgUrl ?? string.Empty).Trim();
            var customEpgUrls = _settingsService.Settings.CustomEpgUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList() ?? new List<string>();
            var hasUsableTvgIds = channelsForMapping.Any(c => !string.IsNullOrWhiteSpace(c.TvgId));
            
            var epgSources = _epgSourceResolver.ResolveEpgSources(
                new List<string>(), // Country based detection removed with iptv-epg.org
                providerEpgUrl, 
                playlistEpgUrl, 
                customEpgUrls,
                hasUsableTvgIds,
                preferredLanguageCode: appLanguage,
                providerHeaders: providerHeaders);
            _perfTrace?.Counter("EPG", "ResolvedSources", epgSources.Count, $"providerUrl={!string.IsNullOrWhiteSpace(providerEpgUrl)} custom={customEpgUrls.Count}");

            if (!isBackgroundSync)
            {
                StatusMessage = _localizationService.GetString("Main.Status.TryingEpgSources");
            }

            // 4. Her kaynağı indirmeyi dene
            bool anySuccess = false;
            string? lastSourceError = null;
            string? successfulSourceUrl = null;
            
            var epgProgressReporter = new Progress<EpgProgressInfo>(p =>
            {
                EpgProgress = p;
                if (!isBackgroundSync)
                {
                    StatusMessage = $"EPG: {p.Message}";
                }
            });

            foreach (var source in epgSources)
            {
                if (anySuccess && source.Type != EpgSourceType.CustomUrl)
                {
                    _logger?.LogDebug("[MainViewModel] Skipping lower-priority EPG source {SourceType}; custom EPG already loaded.", source.Type);
                    break;
                }

                try
                {
                    using var sourceTrace = _perfTrace?.BeginOperation("EPG", "LoadEpgSource", $"type={source.Type} primary={source.IsPrimary}");
                    if (!isBackgroundSync)
                    {
                        StatusMessage = string.Format(CultureInfo.CurrentCulture,
                            _localizationService.GetString("Main.Status.EpgConnecting"), source.Type);
                    }

                    // Always process all live channels for every source
                    List<Channel> targetChannels = liveChannels;
                    if (targetChannels.Count == 0) continue;

                    var loadedPrograms = await _epgService.LoadEpgAsync(
                        source.Url, 
                        source.IsPrimary, 
                        targetChannels, 
                        daysAhead: 7, 
                        progress: epgProgressReporter,
                        clearBeforeSave: source.ClearBeforeLoad,
                        headers: source.Headers); // ATOMIC CLEAR: Only clear if we actually start saving programs
                    _perfTrace?.Counter("EPG", "LoadedPrograms", loadedPrograms, $"source={source.Type}");

                    if (loadedPrograms > 0)
                    {
                        anySuccess = true;
                        successfulSourceUrl = source.Url;
                        _logger?.LogDebug($"[MainViewModel] EPG loaded from {source.Type} ({loadedPrograms} programs) - URL: {source.Url}");
                        lastSourceError = null;

                        if (source.Type != EpgSourceType.CustomUrl)
                        {
                            break;
                        }
                    }
                    else
                    {
                        if (!anySuccess) lastSourceError = $"{source.Type}: 0 program";
                    }
                }
                catch (Exception ex)
                {
                    lastSourceError = $"{source.Type}: {UserFriendlyErrorMessage.FromException(ex)}";
                    _perfTrace?.Event("EPG", "LoadEpgSource failed", $"source={source.Type} error={ex.GetType().Name}:{ex.Message}");
                    _logger?.LogDebug($"[MainViewModel] EPG source failed: {source.Type} - {ex.Message}");
                }
            }

            EpgProgress = null; // Clear progress info when done

            EnsureEpgBackgroundSync();

            if (!isBackgroundSync)
            {
                StatusMessage = anySuccess
                    ? _localizationService.GetString("Main.Status.EpgReady")
                    : string.IsNullOrWhiteSpace(lastSourceError)
                        ? _localizationService.GetString("Main.Error.EpgLoadFailed")
                        : $"{_localizationService.GetString("Main.Error.EpgLoadFailed")} ({lastSourceError})";
            }

            if (SelectedPlaylist != null)
            {
                _perfTrace?.Event("EPG", "Persist EPG result", $"success={anySuccess} playlist={SelectedPlaylist.Id}");
                var playlistToUpdate = await db.Playlists.FirstOrDefaultAsync(p => p.Id == SelectedPlaylist.Id);
                if (playlistToUpdate != null)
                {
                    if (anySuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(successfulSourceUrl))
                        {
                            // Xtream ve Stalker için dinamik üretilen linkleri DB'ye (açık şifre/mac ile) kaydetmiyoruz.
                            // Çünkü bunlar her seferinde ProviderAccount üzerinden güvenlice oluşturuluyor.
                            // M3U Header veya Custom URL söz konusuysa kaydediyoruz.
                            bool isDynamicProvider = CurrentProfile.ProviderAccount?.Type == ProfileType.XtreamCodes || 
                                                    CurrentProfile.ProviderAccount?.Type == ProfileType.StalkerPortal;
                            
                            if (!isDynamicProvider)
                            {
                                playlistToUpdate.EpgUrl = successfulSourceUrl;
                                SelectedPlaylist.EpgUrl = successfulSourceUrl;
                            }
                        }

                        playlistToUpdate.EpgLastUpdated = DateTime.UtcNow;
                        playlistToUpdate.EpgLastError = null;
                        await db.SaveChangesAsync();
                        SelectedPlaylist.EpgLastUpdated = playlistToUpdate.EpgLastUpdated;
                        SelectedPlaylist.EpgLastError = null;
                        
                        // Sync completed, refresh UI immediately
                        _ = EnrichVisibleChannelsWithEpgAsync();
                    }
                    else
                    {
                        var errorText = string.IsNullOrWhiteSpace(lastSourceError)
                            ? "EPG kaynaklarindan veri alinamadi."
                            : lastSourceError;
                        playlistToUpdate.EpgLastError = errorText;
                        await db.SaveChangesAsync();
                        SelectedPlaylist.EpgLastError = errorText;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await PersistSelectedPlaylistEpgErrorAsync($"LoadEpgInternal: {UserFriendlyErrorMessage.FromException(ex)}");
            if (!isBackgroundSync)
            {
                StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Main.Error.Epg"), ex);
            }
            else
            {
                _logger?.LogDebug($"Background EPG sync failed: {ex.Message}");
            }
        }
        finally
        {
            if (!isBackgroundSync && setBusyState)
            {
                IsLoading = false;
            }

            if (!isBackgroundSync)
            {
                Interlocked.Exchange(ref _isManualEpgRefreshRunning, 0);
                Interlocked.Exchange(ref _isManualRefreshRunning, 0);
            }
        }
    }


    private async Task PersistSelectedPlaylistEpgErrorAsync(string error)
    {
        if (SelectedPlaylist == null || string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == SelectedPlaylist.Id);
            if (playlist == null)
            {
                return;
            }

            playlist.EpgLastError = error;
            await db.SaveChangesAsync();
            SelectedPlaylist.EpgLastError = error;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"PersistSelectedPlaylistEpgErrorAsync failed: {ex}");
        }
    }

    private static async Task<bool> HasEpgForChannelsAsync(AppDbContext db, List<Channel> channels)
    {
        if (channels.Count == 0) return false;

        var channelIds = new HashSet<string>(channels.Select(c => c.Id.ToString()), StringComparer.Ordinal);
        foreach (var tvgId in channels.Select(c => c.TvgId).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            channelIds.Add(tvgId!);
        }

        var from = DateTime.UtcNow.Date;
        var to = from.AddDays(2);
        return await db.EpgPrograms.AnyAsync(p =>
            channelIds.Contains(p.ChannelId) &&
            p.StartTime < to &&
            p.EndTime > from);
    }

    private void EnsureEpgBackgroundSync()
    {
        var hours = _settingsService.Settings.EpgRefreshFrequencyHours;
        var epgEnabled = _settingsService.Settings.EpgEnabled;

        if (!epgEnabled)
        {
            _epgSyncTimer?.Dispose();
            _epgSyncTimer = null;
            _uiEpgRefreshTimer?.Dispose();
            _uiEpgRefreshTimer = null;
            return;
        }

        if (hours <= 0)
        {
            _epgSyncTimer?.Dispose();
            _epgSyncTimer = null;
        }
        else
        {
            var interval = TimeSpan.FromHours(hours);
            if (_epgSyncTimer != null)
            {
                _epgSyncTimer.Change(interval, interval);
            }
            else
            {
                _epgSyncTimer = new Timer(async _ =>
                {
                    if (Interlocked.Exchange(ref _isBackgroundEpgSyncRunning, 1) == 1) return;
                    try { await LoadEpgInternalAsync(isBackgroundSync: true, forceRefresh: false); }
                    finally { Interlocked.Exchange(ref _isBackgroundEpgSyncRunning, 0); }
                }, null, interval, interval);
            }
        }

        // Start UI EPG refresh timer (refresh visible program titles every 5 minutes)
        if (_uiEpgRefreshTimer == null)
        {
            var uiInterval = TimeSpan.FromMinutes(5);
            _uiEpgRefreshTimer = new Timer(_ =>
            {
                _ = EnrichVisibleChannelsWithEpgAsync();
            }, null, uiInterval, uiInterval);
        }
    }

    private async Task EnrichVisibleChannelsWithEpgAsync()
    {
        using var trace = _perfTrace?.BeginOperation("BG", "EnrichVisibleChannelsWithEpgAsync", $"visible={FilteredChannels.CountedItemCount}");
        // Check for EPG expiration (All programs in database have ended)
        if (_settingsService.Settings.EpgEnabled)
        {
            try
            {
                var maxEndTime = await _epgService.GetMaxProgramEndTimeAsync();
                if (maxEndTime.HasValue && DateTime.UtcNow > maxEndTime.Value)
                {
                    _logger?.LogInformation("EPG data has expired (Latest program ended at {MaxEndTime}). Clearing database.", maxEndTime.Value);
                    await _epgService.ClearEpgAsync();
                    
                    // Clear current program titles from memory to reflect "No Information" immediately
                    foreach (var channel in Channels)
                    {
                        channel.CurrentProgramTitle = null;
                        channel.EpgProgress = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"EPG expiration check failed: {ex.Message}");
            }
        }

        // Enrich main list
        if (Channels.Count > 0)
            await EnrichChannelsWithEpgAsync(Channels);

        // Enrich favorites (only live)
        var favoriteLive = FavoriteChannels.OfType<Channel>().Where(c => c.Type == ChannelType.Live).ToList();
        if (favoriteLive.Count > 0)
            await EnrichChannelsWithEpgAsync(favoriteLive);

        // Enrich history (only live)
        if (HistoryLiveChannels.Count > 0)
            await EnrichChannelsWithEpgAsync(HistoryLiveChannels);
    }

    private void EnsureChannelBackgroundRefresh()
    {
        var hours = _settingsService.Settings.ChannelListRefreshFrequencyHours;
        if (hours <= 0)
        {
            _channelSyncTimer?.Dispose();
            _channelSyncTimer = null;
            return;
        }

        var interval = TimeSpan.FromHours(hours);
        if (_channelSyncTimer != null)
        {
            _channelSyncTimer.Change(interval, interval);
            return;
        }

        _channelSyncTimer = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref _isBackgroundChannelSyncRunning, 1) == 1)
            {
                return;
            }

            try
            {
                await RefreshSelectedPlaylistAsync(isBackground: true);
            }
            finally
            {
                Interlocked.Exchange(ref _isBackgroundChannelSyncRunning, 0);
            }
        }, null, interval, interval);
    }

    private void ApplyRefreshSchedulesFromSettings()
    {
        EnsureEpgBackgroundSync();
        EnsureChannelBackgroundRefresh();
        ReorderGroupCachesFromLanguagePreference();
        UpdateGroupsForSelectedType();
        ScheduleImmediateFilter();
    }

    [ObservableProperty]
    private BatchObservableCollection<object> _myList = new();

    [ObservableProperty]
    private BatchObservableCollection<object> _favoriteChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _historyChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _historyLiveChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Series> _historySeriesItems = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _historyVodChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Series> _downloadedSeriesItems = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _downloadedVodChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<DownloadItem> _activeDownloadItems = new();

    [ObservableProperty]
    private BatchObservableCollection<DownloadItem> _activeDownloadingItems = new();

    [ObservableProperty]
    private BatchObservableCollection<DownloadItem> _queuedDownloadItems = new();

    [ObservableProperty]
    private BatchObservableCollection<DownloadItem> _completedDownloadItems = new();

    [ObservableProperty]
    private int _activeDownloadCount;

    [ObservableProperty]
    private int _totalDownloadedCount;

    [ObservableProperty]
    private string _totalDownloadsInfoText = "";

    [ObservableProperty]
    private double _storageOtherPercent;

    [ObservableProperty]
    private double _storageNoctraPercent;

    [ObservableProperty]
    private double _storagePendingPercent;

    [ObservableProperty]
    private double _storageFreePercent = 100;

    [ObservableProperty]
    private string _storageUsageDetailText = "0 B / 0 B";

    [ObservableProperty]
    private string _activeDownloadsTotalSpeedText = "0 B/sn";

    [ObservableProperty]
    private string _downloadFreeDiskSpaceText = "-";

    [ObservableProperty]
    private BatchObservableCollection<Channel> _searchLiveChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Series> _searchSeriesChannels = new();


    [ObservableProperty]
    private BatchObservableCollection<Channel> _searchVodChannels = new();

    [ObservableProperty]
    private string _searchSuggestion = string.Empty;

    [ObservableProperty]
    private BatchObservableCollection<Channel> _searchSimilarLiveChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Series> _searchSimilarSeriesChannels = new();

    [ObservableProperty]
    private BatchObservableCollection<Channel> _searchSimilarVodChannels = new();

    [ObservableProperty]
    private bool _showSearchSimilarSection;

    [ObservableProperty]
    private bool _showSearchEmptyState;

    [ObservableProperty]
    private bool _showMyListEmptyState = true;

    [ObservableProperty]
    private bool _showFavoritesEmptyState = true;

    [ObservableProperty]
    private bool _showHistoryEmptyState = true;

    [ObservableProperty]
    private bool _showDownloadsEmptyState = true;

    [ObservableProperty]
    private bool _isDownloadCenterVisible;

    public int DownloadTabIndex
    {
        get => IsDownloadCenterVisible ? 1 : 0;
        set
        {
            IsDownloadCenterVisible = value == 1;
            OnPropertyChanged(nameof(DownloadTabIndex));
        }
    }

    [ObservableProperty]
    private DownloadSortOrder _selectedDownloadSortOrder = DownloadSortOrder.Latest;

    partial void OnSelectedDownloadSortOrderChanged(DownloadSortOrder value) => _ = RefreshDownloadedItemsFromDatabaseAsync();

    public bool ShowDownloadsLandingEmptyState => !IsDownloadCenterVisible && ShowDownloadsEmptyState;

    [RelayCommand]
    private void Navigate(AppView view)
    {
        var previousView = ActiveView;
        var traceId = Interlocked.Increment(ref _navigationTraceId);
        using var trace = _perfTrace?.BeginOperation(
            "NAV",
            $"NAV#{traceId} {previousView}->{view}",
            $"currentItems={CurrentViewItemCount} type={SelectedChannelType} group={SelectedGroup ?? "<all>"} search={(string.IsNullOrWhiteSpace(SearchText) ? "<empty>" : SearchText)}");
        _suppressNavigationFilterRefresh = true;
        try
        {
            if (view != AppView.Search && !string.IsNullOrWhiteSpace(SearchText))
            {
                SearchText = string.Empty;
                SearchQuery = string.Empty;
            }

            ActiveView = view;
            if (view != AppView.Downloads)
            {
                _seriesDetailDownloadedOnlyMode = false;
                OnPropertyChanged(nameof(IsDownloadedSeriesDetailMode));
            }

            if (view == AppView.Live) SelectedChannelType = ChannelType.Live;
            else if (view == AppView.Movies) SelectedChannelType = ChannelType.VOD;
            else if (view == AppView.Series) SelectedChannelType = ChannelType.Series;
            else if (view == AppView.MyList)
            {
                SelectedChannelType = null;
                SelectedGroup = null;
                try
                {
                    UpdateMyList();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"Navigate->UpdateMyList failed: {ex}");
                    MyList.Clear();
                    ShowMyListEmptyState = true;
                }
                _ = RefreshPersonalListsFromDatabaseAsync();
            }
            else if (view == AppView.Favorites)
            {
                SelectedChannelType = null;
                SelectedGroup = null;
                ShowOnlyFavorites = false;
                try
                {
                    UpdateFavoriteChannels();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"Navigate->UpdateFavoriteChannels failed: {ex}");
                    FavoriteChannels.Clear();
                    ShowFavoritesEmptyState = true;
                }
                _ = RefreshPersonalListsFromDatabaseAsync();
            }
            else if (view == AppView.History)
            {
                SelectedChannelType = null;
                SelectedGroup = null;
                ShowOnlyFavorites = false;
                UpdateHistoryChannels();
                _ = RefreshPersonalListsFromDatabaseAsync();
            }
            else if (view == AppView.Downloads)
            {
                SelectedChannelType = null;
                SelectedGroup = null;
                ShowOnlyFavorites = false;
                IsDownloadCenterVisible = false;
                UpdateDownloadedItems();
                _ = RefreshDownloadedItemsFromDatabaseAsync();
            }
            else SelectedChannelType = null;
        }
        finally
        {
            _suppressNavigationFilterRefresh = false;
        }

        if (view is AppView.Live or AppView.Movies or AppView.Series &&
            (previousView != view || CurrentViewItemCount == 0))
        {
            ScheduleImmediateFilter();
        }
        else
        {
            _perfTrace?.Event("NAV", $"NAV#{traceId} filter-skipped", $"view={view} previous={previousView} currentItems={CurrentViewItemCount}");
        }
    }

    private void UpdateMyList()
    {
        using var trace = _perfTrace?.BeginOperation("LIST", "UpdateMyList", $"channels={Channels?.Count ?? 0} series={_allSeriesCache.Count}");
        try
        {
            var channelsSnapshot = Channels?.ToList() ?? new List<Channel>();
            var seriesSnapshot = _allSeriesCache;

            var seriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            var episodeUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (seriesSnapshot != null)
            {
                foreach (var series in seriesSnapshot)
                {
                    if (series == null)
                    {
                        continue;
                    }

                    var key = series.Id > 0 ? $"id:{series.Id}" : $"p:{series.PlaylistId}|n:{series.Name}";
                    if (!seriesMap.ContainsKey(key))
                    {
                        seriesMap[key] = series;
                    }

                    if (series.Seasons != null)
                    {
                        foreach (var s in series.Seasons)
                        {
                            if (s?.Episodes == null) continue;
                            foreach (var ep in s.Episodes)
                            {
                                if (!string.IsNullOrWhiteSpace(ep.StreamUrl))
                                {
                                    episodeUrls.Add(ep.StreamUrl);
                                }
                            }
                        }
                    }
                }
            }

            var list = new List<object>();
            list.AddRange(channelsSnapshot
                .Where(c => c.Type != ChannelType.Series && c.IsInMyList && (string.IsNullOrWhiteSpace(c.StreamUrl) || !episodeUrls.Contains(c.StreamUrl)))
                .Cast<object>());

            list.AddRange(seriesMap.Values.Where(s => s.IsInMyList).Cast<object>());
            SetItems(MyList, list
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty),
                () => ShowMyListEmptyState = MyList.Count == 0);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"UpdateMyList failed: {ex}");
            MyList.Clear();
            ShowMyListEmptyState = true;
        }
    }

    private void UpdateFavoriteChannels()
    {
        using var trace = _perfTrace?.BeginOperation("LIST", "UpdateFavoriteChannels", $"channels={Channels?.Count ?? 0} series={_allSeriesCache.Count}");
        try
        {
            var channelsSnapshot = Channels?.ToList() ?? new List<Channel>();
            var seriesSnapshot = _allSeriesCache;

            var seriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            var episodeUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (seriesSnapshot != null)
            {
                foreach (var series in seriesSnapshot)
                {
                    if (series == null)
                    {
                        continue;
                    }

                    var key = series.Id > 0 ? $"id:{series.Id}" : $"p:{series.PlaylistId}|n:{series.Name}";
                    if (!seriesMap.ContainsKey(key))
                    {
                        seriesMap[key] = series;
                    }

                    if (series.Seasons != null)
                    {
                        foreach (var s in series.Seasons)
                        {
                            if (s?.Episodes == null) continue;
                            foreach (var ep in s.Episodes)
                            {
                                if (!string.IsNullOrWhiteSpace(ep.StreamUrl))
                                {
                                    episodeUrls.Add(ep.StreamUrl);
                                }
                            }
                        }
                    }
                }
            }

            var list = new List<object>();
            list.AddRange(channelsSnapshot
                .Where(c => c.Type != ChannelType.Series && c.IsFavorite && (string.IsNullOrWhiteSpace(c.StreamUrl) || !episodeUrls.Contains(c.StreamUrl)))
                .Cast<object>());

            list.AddRange(seriesMap.Values.Where(s => s.IsFavorite).Cast<object>());
            SetItems(FavoriteChannels, list
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty),
                () => {
                    ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
                    _ = EnrichChannelsWithEpgAsync(FavoriteChannels.OfType<Channel>());
                });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"UpdateFavoriteChannels failed: {ex}");
            FavoriteChannels.Clear();
            ShowFavoritesEmptyState = true;
        }
    }

    public void ResetWatchHistoryUI()
    {
        // 1. Reset Channels in memory
        if (Channels != null)
        {
            foreach (var channel in Channels)
            {
                channel.LastWatched = null;
                channel.WatchedPosition = TimeSpan.Zero;
                channel.IsCompleted = false;
            }
        }

        // 2. Episodes are no longer cached in _allSeriesCache (lightweight cache).
        // Progress is queried directly from DB when needed.
        // Just update dependent UI collections
        UpdateHistoryChannels();
        _ = RefreshPersonalListsFromDatabaseAsync();
    }

    public void UpdateHistoryChannels()
    {
        _ = RefreshHistoryChannelsOnlyAsync();
    }

    private async Task UpdateHistoryBucketsAsync(IEnumerable<Channel>? sourceChannels = null)
    {
        var historySnapshot = (sourceChannels ?? HistoryChannels).ToList();
        SetItems(HistoryLiveChannels, historySnapshot.Where(c => c.Type == ChannelType.Live));
        SetItems(HistoryVodChannels, historySnapshot.Where(c => c.Type == ChannelType.VOD));

        // Use cached LastWatchedEpisodeAt when available (populated on episode watch save)
        var cachedSeries = _allSeriesCache
            .Where(s => s.LastWatchedEpisodeAt.HasValue)
            .OrderByDescending(s => s.LastWatchedEpisodeAt)
            .ToList();

        if (cachedSeries.Count > 0)
        {
            SetItems(HistorySeriesItems, cachedSeries, () => {
                ShowHistoryEmptyState = HistoryLiveChannels.Count == 0
                                     && HistoryVodChannels.Count == 0
                                     && HistorySeriesItems.Count == 0;
            });
            return;
        }

        // Fallback: query DB for series with watched episodes
        var playlistId = SelectedPlaylist?.Id ?? 0;
        var watchedSeries = new List<Series>();
        if (playlistId > 0)
        {
            try
            {
                using var db = await _contextFactory.CreateDbContextAsync();
                var watchedSeriesIds = await db.Episodes
                    .AsNoTracking()
                    .Where(e => e.LastWatched.HasValue
                             && e.Season != null
                             && e.Season.Series != null
                             && e.Season.Series.PlaylistId == playlistId)
                    .GroupBy(e => e.Season.SeriesId)
                    .Select(g => new { SeriesId = g.Key, LastWatched = g.Max(e => e.LastWatched) })
                    .OrderByDescending(x => x.LastWatched)
                    .ToListAsync();

                // Match against lightweight cache and populate LastWatchedEpisodeAt
                var cacheById = _allSeriesCache.ToDictionary(s => s.Id, s => s);
                foreach (var ws in watchedSeriesIds)
                {
                    if (cacheById.TryGetValue(ws.SeriesId, out var series))
                    {
                        series.LastWatchedEpisodeAt = ws.LastWatched;
                        watchedSeries.Add(series);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in UpdateHistoryBucketsAsync DB query");
            }
        }

        SetItems(HistorySeriesItems, watchedSeries, () => {
            ShowHistoryEmptyState = HistoryLiveChannels.Count == 0
                                 && HistoryVodChannels.Count == 0
                                 && HistorySeriesItems.Count == 0;
        });
    }

    private void UpdateDownloadedItems()
    {
        // Phase 27: Do not rebuild the downloads list from local memory channels.
        // Instead, always refresh from the global database so we don't accidentally wipe what we just read.
        _ = RefreshDownloadedItemsFromDatabaseAsync();
    }

    [ObservableProperty]
    private bool _showStorageWarning;

    private async Task RefreshDownloadedItemsFromDatabaseAsync()
    {
        // Phase 27: Completely global — no profile guards, no early returns.
        // This method always runs the full pipeline regardless of current profile state.

        using var db = await _contextFactory.CreateDbContextAsync();

        // ── 1. Collect all tracked file paths from DB ──
        var allCompletedDownloads = await db.DownloadItems
            .AsNoTracking()
            .Where(d => d.Status == DownloadStatus.Completed &&
                        !string.IsNullOrWhiteSpace(d.LocalFilePath))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();

        var trackedPaths = new HashSet<string>(
            allCompletedDownloads
                .Select(d => d.LocalFilePath!)
                .Where(p => !string.IsNullOrWhiteSpace(p)),
            StringComparer.OrdinalIgnoreCase);

        // ── 2. Build VOD list from DB records whose files still exist ──
        var vodList = new List<Channel>();
        foreach (var item in allCompletedDownloads.Where(d => d.ChannelType == ChannelType.VOD))
        {
            if (string.IsNullOrWhiteSpace(item.LocalFilePath) || !File.Exists(item.LocalFilePath))
                continue;

            var size = new FileInfo(item.LocalFilePath).Length;
            vodList.Add(new Channel
            {
                Name = string.IsNullOrWhiteSpace(item.DisplayName) ? "VOD" : item.DisplayName,
                StreamUrl = item.LocalFilePath,
                LogoUrl = item.PosterUrl,
                BackdropUrl = item.PosterUrl,
                Type = ChannelType.VOD,
                PlaylistId = item.PlaylistId,
                LocalSizeText = FormatDownloadBytes(size)
            });
        }

        // ── 3. Build Series map from DB records whose files still exist ──
        var seriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allCompletedDownloads.Where(d => d.ChannelType == ChannelType.Series))
        {
            if (string.IsNullOrWhiteSpace(item.LocalFilePath) || !File.Exists(item.LocalFilePath))
                continue;

            AddSeriesEpisodeFromDownloadItem(seriesMap, item);
        }

        // ── 4. Filesystem discovery: find orphan video files not tracked in DB ──
        try
        {
            var downloadRoot = ResolveGlobalDownloadRoot();
            if (Directory.Exists(downloadRoot))
            {
                var videoExtensions = GetDownloadVideoExtensions();

                var allFiles = Directory.EnumerateFiles(downloadRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f)))
                    .Where(f => !trackedPaths.Contains(f));

                foreach (var filePath in allFiles)
                {
                    // Determine if this is a Series or a Movie based on directory structure
                    var relativePath = Path.GetRelativePath(downloadRoot, filePath);
                    var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Series pattern: Series/<SeriesName>/<Season>/<file> or Profile_X/Diziler/<SeriesName>/<Season>/<file>
                    // or Profile_X/Series/<SeriesName>/<Season>/<file>
                    if (IsSeriesFilePath(parts))
                    {
                        var seriesName = ExtractSeriesNameFromPath(parts);
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        var displayName = fileName;
                        var syntheticItem = new DownloadItem
                        {
                            DisplayName = displayName,
                            LocalFilePath = filePath,
                            ChannelType = ChannelType.Series
                        };
                        AddSeriesEpisodeFromDownloadItem(seriesMap, syntheticItem);
                    }
                    else
                    {
                        // Treat as VOD / Movie
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        var size = new FileInfo(filePath).Length;
                        vodList.Add(new Channel
                        {
                            Name = fileName,
                            StreamUrl = filePath,
                            Type = ChannelType.VOD,
                            LocalSizeText = FormatDownloadBytes(size)
                        });
                    }
                }

                CleanupStaleDownloadFolders(downloadRoot, videoExtensions);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Filesystem discovery for orphan downloads failed.");
        }

        // ── 4.1 Calculate Final Series Sizes (including orphans) ──
        foreach (var series in seriesMap.Values)
        {
            long totalSeriesSize = 0;
            foreach (var season in series.Seasons)
            {
                foreach (var ep in season.Episodes)
                {
                    if (!string.IsNullOrEmpty(ep.StreamUrl) && File.Exists(ep.StreamUrl))
                    {
                        totalSeriesSize += new FileInfo(ep.StreamUrl).Length;
                    }
                }
            }
            series.LocalSizeText = $"Toplam {FormatDownloadBytes(totalSeriesSize)}";
        }

        // ── 5. Filtering and Sorting ──
        var finalVodList = vodList.AsEnumerable();
        var finalSeriesList = seriesMap.Values.AsEnumerable();

        // Helper to get file date
        static DateTime GetFileDate(string? path) => 
            string.IsNullOrEmpty(path) || !File.Exists(path) ? DateTime.MinValue : File.GetCreationTime(path);

        // Helper to get series date (max of episodes)
        static DateTime GetSeriesDate(Series s) => 
            s.Seasons.SelectMany(sea => sea.Episodes)
              .Select(e => GetFileDate(e.StreamUrl))
              .DefaultIfEmpty(DateTime.MinValue)
              .Max();
        
        // Helper to get size
        static long GetFileSize(string? path) => 
            string.IsNullOrEmpty(path) || !File.Exists(path) ? 0 : new FileInfo(path).Length;
        
        static long GetSeriesSize(Series s) => 
            s.Seasons.SelectMany(sea => sea.Episodes).Sum(e => GetFileSize(e.StreamUrl));

        // Apply Sort
        switch (SelectedDownloadSortOrder)
        {
            case DownloadSortOrder.Latest:
                finalVodList = finalVodList.OrderByDescending(c => GetFileDate(c.StreamUrl));
                finalSeriesList = finalSeriesList.OrderByDescending(s => GetSeriesDate(s));
                break;
            case DownloadSortOrder.NameAZ:
                finalVodList = finalVodList.OrderBy(c => c.Name);
                finalSeriesList = finalSeriesList.OrderBy(s => s.Name);
                break;
            case DownloadSortOrder.SizeLarge:
                finalVodList = finalVodList.OrderByDescending(c => GetFileSize(c.StreamUrl));
                finalSeriesList = finalSeriesList.OrderByDescending(s => GetSeriesSize(s));
                break;
        }

        // ── 5.1 Deduplicate and set UI lists ──
        SetItems(DownloadedVodChannels, finalVodList
            .GroupBy(c => c.StreamUrl ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()));

        SetItems(DownloadedSeriesItems, finalSeriesList);

        // ── 6. Storage Stats ──
        try
        {
            var totalCount = DownloadedVodChannels.Count + seriesMap.Values.Sum(s => s.Seasons.Sum(se => se.Episodes.Count));
            long totalSizeBytes = 0;

            foreach (var vod in vodList)
            {
                if (!string.IsNullOrEmpty(vod.StreamUrl) && File.Exists(vod.StreamUrl))
                {
                    totalSizeBytes += new FileInfo(vod.StreamUrl).Length;
                }
            }

            foreach (var series in seriesMap.Values)
            {
                foreach (var season in series.Seasons)
                {
                    foreach (var ep in season.Episodes)
                    {
                        if (!string.IsNullOrEmpty(ep.StreamUrl) && File.Exists(ep.StreamUrl))
                        {
                            totalSizeBytes += new FileInfo(ep.StreamUrl).Length;
                        }
                    }
                }
            }

            TotalDownloadedCount = totalCount;
            _downloadLandingStoredBytes = totalSizeBytes;
            TotalDownloadsInfoText = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Downloads.Info.Format"),
                totalCount,
                FormatDownloadBytes(totalSizeBytes));

            var root = ResolveGlobalDownloadRoot();
            var driveRoot = Path.GetPathRoot(root);
            if (!string.IsNullOrEmpty(driveRoot))
            {
                var drive = new DriveInfo(driveRoot);
                var totalSpace = drive.TotalSize;
                var freeSpace = drive.AvailableFreeSpace;
                var usedSpace = totalSpace - freeSpace;

                var totalUsedPercent = (usedSpace / (double)totalSpace) * 100.0;
                var noctraPercent = (totalSizeBytes / (double)totalSpace) * 100.0;
                
                StorageOtherPercent = Math.Max(0, totalUsedPercent - noctraPercent);
                StorageNoctraPercent = noctraPercent;
                
                // Calculate pending bytes from active downloads
                long pendingBytes = 0;
                foreach (var activeItem in ActiveDownloadItems)
                {
                    if (activeItem.BytesTotal.HasValue && activeItem.BytesTotal.Value > 0)
                    {
                        var remaining = activeItem.BytesTotal.Value - activeItem.BytesDownloaded;
                        if (remaining > 0) pendingBytes += remaining;
                    }
                }
                
                var pendingPercent = (pendingBytes / (double)totalSpace) * 100.0;
                StoragePendingPercent = pendingPercent;
                StorageFreePercent = Math.Max(0, 100.0 - (totalUsedPercent + pendingPercent));
                
                StorageUsageDetailText = $"{FormatDownloadBytes(usedSpace)} / {FormatDownloadBytes(totalSpace)}";
                ShowStorageWarning = (totalUsedPercent + pendingPercent) > 90.0;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to calculate storage stats.");
        }

        // ── 7. Refresh active/queued downloads globally ──
        await RefreshDownloadsFromServiceAsync(0);
        ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                 DownloadedSeriesItems.Count == 0;
    }

    /// <summary>
    /// Adds a download item as an episode entry into the cross-provider series map.
    /// </summary>
    private void AddSeriesEpisodeFromDownloadItem(
        Dictionary<string, Series> seriesMap,
        DownloadItem item)
    {
        var seriesName = ExtractSeriesBaseName(item.DisplayName);
        var seriesKey = NormalizeFuzzyText(seriesName);
        if (!seriesMap.TryGetValue(seriesKey, out var series))
        {
            series = new Series
            {
                Name = seriesName,
                CoverUrl = item.PosterUrl,
                PlaylistId = item.PlaylistId
            };
            seriesMap[seriesKey] = series;
        }

        var parsed = ParseEpisodeNumbers(item.DisplayName);
        var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == parsed.SeasonNumber);
        if (season == null)
        {
            season = new Season
            {
                SeasonNumber = parsed.SeasonNumber,
                Name = $"Sezon {parsed.SeasonNumber}",
                CoverUrl = item.PosterUrl
            };
            series.Seasons.Add(season);
        }

        if (season.Episodes.Any(e => string.Equals(e.StreamUrl, item.LocalFilePath, StringComparison.OrdinalIgnoreCase)))
            return;

        var episodeNumber = parsed.EpisodeNumber > 0 ? parsed.EpisodeNumber : season.Episodes.Count + 1;
        season.Episodes.Add(new Episode
        {
            EpisodeNumber = episodeNumber,
            Name = item.DisplayName ?? Path.GetFileNameWithoutExtension(item.LocalFilePath ?? ""),
            StreamUrl = item.LocalFilePath,
            CoverUrl = item.PosterUrl
        });
    }

    /// <summary>
    /// Resolves the global download root directory (same logic as ContentDownloadService).
    /// </summary>
    private string ResolveGlobalDownloadRoot()
    {
        var configured = _settingsService.Settings.DownloadPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var trimmed = configured.Trim().Trim('"');
            if (Path.IsPathFullyQualified(trimmed))
                return trimmed;
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra", "Downloads");
    }

    /// <summary>
    /// Checks if the file path parts indicate a Series download (not a standalone movie).
    /// Supports both new layout (Series/Name/Season/file) and legacy layout (Profile_X/Diziler/Name/Season/file).
    /// </summary>
    private static bool IsSeriesFilePath(string[] parts)
    {
        // New layout: Series/<name>/<season>/<file> → parts.Length >= 4, parts[0] == "Series"
        // Legacy:     Profile_X/Diziler/<name>/<season>/<file> → parts.Length >= 5
        // Legacy2:    Profile_X/Series/<name>/<season>/<file> → parts.Length >= 5
        if (parts.Length >= 4)
        {
            var topDir = parts[0];
            if (string.Equals(topDir, "Series", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(topDir, "Diziler", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (parts.Length >= 5)
        {
            var secondDir = parts[1];
            if (string.Equals(secondDir, "Series", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(secondDir, "Diziler", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the series name from the relative path parts.
    /// </summary>
    private static string ExtractSeriesNameFromPath(string[] parts)
    {
        // New layout: Series/<name>/<season>/<file> → name is parts[1]
        if (parts.Length >= 4 &&
            (string.Equals(parts[0], "Series", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parts[0], "Diziler", StringComparison.OrdinalIgnoreCase)))
            return parts[1];

        // Legacy: Profile_X/Diziler/<name>/<season>/<file> → name is parts[2]
        if (parts.Length >= 5 &&
            (string.Equals(parts[1], "Series", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parts[1], "Diziler", StringComparison.OrdinalIgnoreCase)))
            return parts[2];

        // Fallback: use the parent directory name
        return parts.Length >= 2 ? parts[^2] : "Unknown";
    }

    private static HashSet<string> GetDownloadVideoExtensions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv",
            ".mp4",
            ".avi",
            ".ts",
            ".m4v",
            ".mov",
            ".mpg",
            ".mpeg",
            ".webm",
            ".nctra"
        };

    private static bool IsLocalFilesystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Trim().Trim('"', '\'');
        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.IsFile;
        }

        return normalized.StartsWith(@"\\", StringComparison.Ordinal)
               || Regex.IsMatch(normalized, @"^[a-zA-Z]:[\\/]");
    }

    private static string NormalizeLocalFilesystemPath(string path)
    {
        var normalized = path.Trim().Trim('"', '\'');
        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return normalized;
    }

    private void CleanupStaleDownloadFolders(string downloadRoot, HashSet<string> videoExtensions)
    {
        if (!Directory.Exists(downloadRoot))
        {
            return;
        }

        try
        {
            var seriesRoots = Directory
                .EnumerateDirectories(downloadRoot, "*", SearchOption.AllDirectories)
                .Where(IsSeriesRootDirectory)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var seriesRoot in seriesRoots)
            {
                if (!Directory.Exists(seriesRoot))
                {
                    continue;
                }

                var hasVideoFile = Directory
                    .EnumerateFiles(seriesRoot, "*.*", SearchOption.AllDirectories)
                    .Any(f => videoExtensions.Contains(Path.GetExtension(f)));

                if (!hasVideoFile)
                {
                    TryDeleteDownloadDirectoryTree(seriesRoot);
                }
            }

            TryDeleteEmptyDownloadDirectoriesBottomUp(downloadRoot);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Stale download folder cleanup failed.");
        }
    }

    private static bool IsSeriesRootDirectory(string directory)
    {
        var parent = Directory.GetParent(directory);
        if (parent == null)
        {
            return false;
        }

        var parentName = parent.Name;
        return string.Equals(parentName, "Series", StringComparison.OrdinalIgnoreCase)
               || string.Equals(parentName, "Diziler", StringComparison.OrdinalIgnoreCase);
    }

    private void TryDeleteDownloadDirectoryTree(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory)
            || !IsPathInsideDownloadRoot(directory)
            || PathsEqual(directory, ResolveGlobalDownloadRoot()))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to delete download directory tree: {Directory}", directory);
        }
    }

    private void TryDeleteEmptyDownloadParents(string? startDirectory)
    {
        var root = ResolveGlobalDownloadRoot();
        var current = startDirectory;
        while (!string.IsNullOrWhiteSpace(current)
               && Directory.Exists(current)
               && IsPathInsideDownloadRoot(current)
               && !PathsEqual(current, root))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    return;
                }

                Directory.Delete(current, false);
                current = Path.GetDirectoryName(current);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to delete empty download parent directory: {Directory}", current);
                return;
            }
        }
    }

    private void TryDeleteEmptyDownloadDirectoriesBottomUp(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(directory).ToList())
        {
            TryDeleteEmptyDownloadDirectoriesBottomUp(child);
        }

        TryDeleteEmptyDownloadParents(directory);
    }

    private bool IsPathInsideDownloadRoot(string path)
    {
        var root = ResolveGlobalDownloadRoot();
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || PathsEqual(fullPath, fullRoot);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private async Task RefreshDownloadsFromServiceAsync(int profileId)
    {
        try
        {
            // Phase 25/26: Use a unified global directory, ignoring the profile parameter
            // The instruction provided a code edit for this method, but it seems to be a placeholder or incorrect.
            // The instruction was to refactor RefreshDownloadedItemsFromDatabaseAsync.
            // Applying the provided snippet directly would cause syntax errors due to undefined variables.
            // Therefore, I'm keeping the original implementation of RefreshDownloadsFromServiceAsync
            // as the instruction for it was unclear and the snippet was problematic.
            var downloads = await _contentDownloadService.GetDownloadsAsync(profileId);
            var allActive = downloads
                .Where(d => d.IsActive)
                .OrderByDescending(d => d.CreatedAt)
                .ToList();

            SetItems(ActiveDownloadItems, allActive
                .Select((d, index) =>
                {
                    d.QueueOrder = index + 1;
                    return d;
                }));

            SetItems(ActiveDownloadingItems, allActive
                .Where(d => d.Status == DownloadStatus.Downloading || d.Status == DownloadStatus.Paused)
                .OrderBy(d => d.Status == DownloadStatus.Paused ? 1 : 0)
                .ThenBy(d => d.CreatedAt));

            SetItems(QueuedDownloadItems, allActive
                .Where(d => d.Status == DownloadStatus.Queued)
                .OrderBy(d => d.CreatedAt));

            SetItems(CompletedDownloadItems, downloads
                .Where(d => d.Status == DownloadStatus.Completed)
                .Where(d =>
                {
                    var ts = (d.CompletedAt ?? d.UpdatedAt).ToUniversalTime();
                    return ts >= _downloadCenterSessionStartUtc;
                })
                .OrderByDescending(d => d.CompletedAt ?? d.UpdatedAt)
                .Take(100));
            UpdateDownloadCenterSummary(profileId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"RefreshDownloadsFromServiceAsync failed: {ex.Message}");
            SetItems(ActiveDownloadItems, Enumerable.Empty<DownloadItem>());
            SetItems(ActiveDownloadingItems, Enumerable.Empty<DownloadItem>());
            SetItems(QueuedDownloadItems, Enumerable.Empty<DownloadItem>());
            SetItems(CompletedDownloadItems, Enumerable.Empty<DownloadItem>());
            SetDownloadCenterSummaryEmpty();
        }

        ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                 DownloadedSeriesItems.Count == 0;
    }

    private void SetDownloadCenterSummaryEmpty()
    {
        ActiveDownloadCount = 0;
        ActiveDownloadsTotalSpeedText = string.Format(CultureInfo.CurrentCulture,
            _localizationService.GetString("Downloads.Speed.PerSecondFormat"),
            FormatDownloadBytes(0));
        DownloadFreeDiskSpaceText = "-";
        ActiveDownloadingItems.Clear();
        QueuedDownloadItems.Clear();
        CompletedDownloadItems.Clear();
    }

    private void UpdateDownloadCenterSummary(int profileId)
    {
        ActiveDownloadCount = ActiveDownloadingItems.Count;
        var totalSpeed = ActiveDownloadItems
            .Where(d => d.Status == DownloadStatus.Downloading)
            .Sum(d => Math.Max(0, d.SpeedBytesPerSecond));
        ActiveDownloadsTotalSpeedText = string.Format(CultureInfo.CurrentCulture,
            _localizationService.GetString("Downloads.Speed.PerSecondFormat"),
            FormatDownloadBytes((long)totalSpeed));
        DownloadFreeDiskSpaceText = ResolveDownloadFreeSpaceText(profileId);
    }

    private string ResolveDownloadFreeSpaceText(int profileId)
    {
        try
        {
            // Phase 27: Use global download root, do NOT create Profile_X subfolders
            var root = ResolveGlobalDownloadRoot();

            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            var driveRoot = Path.GetPathRoot(root);
            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                return "-";
            }

            var drive = new DriveInfo(driveRoot);
            return string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Downloads.Disk.FreeSpaceFormat"),
                FormatDownloadBytes(drive.AvailableFreeSpace));
        }
        catch
        {
            return "-";
        }
    }

    private static string FormatDownloadBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        // Use a format that respects local culture for decimals (like comma in Turkish)
        return $"{value.ToString("N2", CultureInfo.CurrentCulture)} {units[unitIndex]}";
    }

    private void ScheduleDownloadsLandingRefresh(int profileId)
    {
        _downloadsLandingRefreshCts?.Cancel();
        _downloadsLandingRefreshCts?.Dispose();

        var cts = new CancellationTokenSource();
        _downloadsLandingRefreshCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                if (Interlocked.Exchange(ref _isDownloadsLandingRefreshing, 1) == 1)
                {
                    return;
                }

                await _dispatcherService.InvokeAsync(async () =>
                {
                    if (CurrentProfileId != profileId ||
                        ActiveView != AppView.Downloads ||
                        IsDownloadCenterVisible)
                    {
                        return;
                    }

                    await RefreshDownloadedItemsFromDatabaseAsync();
                });
            }
            catch (OperationCanceledException)
            {
                // no-op
            }
            finally
            {
                Interlocked.Exchange(ref _isDownloadsLandingRefreshing, 0);
            }
        });
    }

    [RelayCommand]
    private void SetDownloadCenterVisible(bool visible)
    {
        IsDownloadCenterVisible = visible;
    }

    [RelayCommand]
    private async Task DeleteDownloadedMediaAsync(object? media)
    {
        if (media == null) return;

        string title = media is Series s ? s.Name : (media is Channel c ? c.Name : _localizationService.GetString("Download.DefaultName"));
        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("Downloads.Dialog.DeleteContent.Title"),
            string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Downloads.Dialog.DeleteContent.MessageFormat"), title));

        if (!confirmed) return;

        try
        {
            var filesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seriesDirectoriesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (media is Series series)
            {
                foreach (var season in series.Seasons)
                {
                    foreach (var ep in season.Episodes)
                    {
                        if (IsLocalFilesystemPath(ep.StreamUrl))
                        {
                            var localPath = NormalizeLocalFilesystemPath(ep.StreamUrl!);
                            filesToDelete.Add(localPath);
                            var seasonDir = Path.GetDirectoryName(localPath);
                            var seriesDir = seasonDir == null ? null : Path.GetDirectoryName(seasonDir);
                            if (!string.IsNullOrWhiteSpace(seriesDir))
                            {
                                seriesDirectoriesToDelete.Add(seriesDir);
                            }
                        }
                    }
                }
            }
            else if (media is Channel channel)
            {
                if (IsLocalFilesystemPath(channel.StreamUrl))
                {
                    filesToDelete.Add(NormalizeLocalFilesystemPath(channel.StreamUrl!));
                }
            }

            // 1. Delete Files
            foreach (var file in filesToDelete)
            {
                try
                {
                    if (File.Exists(file) && IsPathInsideDownloadRoot(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to delete downloaded file: {File}", file);
                }
            }

            foreach (var dir in seriesDirectoriesToDelete)
            {
                TryDeleteDownloadDirectoryTree(dir);
            }

            foreach (var file in filesToDelete)
            {
                TryDeleteEmptyDownloadParents(Path.GetDirectoryName(file));
            }

            // 2. Delete from DB
            using var db = await _contextFactory.CreateDbContextAsync();
            foreach (var file in filesToDelete)
            {
                var record = await db.DownloadItems.FirstOrDefaultAsync(d => d.LocalFilePath == file);
                if (record != null) db.DownloadItems.Remove(record);
            }
            await db.SaveChangesAsync();

            // 3. Refresh
            await RefreshDownloadedItemsFromDatabaseAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Failed to delete media: {title}");
            await _dialogService.ShowErrorAsync(
                _localizationService.GetString("Common.Error"),
                _localizationService.GetString("Downloads.Dialog.DeleteContent.ErrorMessage"));
        }
    }

    [RelayCommand]
    private async Task DeleteAllDownloadsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("Downloads.Dialog.DeleteAll.Title"),
            _localizationService.GetString("Downloads.Dialog.DeleteAll.Message"));

        if (!confirmed) return;

        try
        {
            var root = ResolveGlobalDownloadRoot();
            if (Directory.Exists(root))
            {
                var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { /* ignore */ }
                }
                
                var dirs = Directory.GetDirectories(root);
                foreach (var dir in dirs)
                {
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            var items = await db.DownloadItems.ToListAsync();
            db.DownloadItems.RemoveRange(items);
            await db.SaveChangesAsync();

            await RefreshDownloadedItemsFromDatabaseAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete all downloads.");
            await _dialogService.ShowErrorAsync(
                _localizationService.GetString("Common.Error"),
                _localizationService.GetString("Downloads.Dialog.DeleteAll.ErrorMessage"));
        }
    }

    [RelayCommand]
    private void OpenDownloadsFolder()
    {
        try
        {
            var root = ResolveGlobalDownloadRoot();
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = root,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open downloads folder.");
        }
    }

    [RelayCommand]
    private async Task CancelDownloadAsync(DownloadItem? item)
    {
        if (item == null || item.Id <= 0)
        {
            return;
        }

        await _contentDownloadService.CancelDownloadAsync(item.Id);
    }

    [RelayCommand]
    private async Task TogglePauseDownloadAsync(DownloadItem? item)
    {
        if (item == null || item.Id <= 0)
        {
            return;
        }

        if (item.IsPaused)
        {
            // Phase 28: Enforce profile guard for manual resume
            if (CurrentProfileId.HasValue && item.ProfileId != CurrentProfileId.Value)
            {
                await _dialogService.ShowErrorAsync(
                    _localizationService.GetString("Download.Error.ResumeWrongProfile.Title"),
                    _localizationService.GetString("Download.Error.ResumeWrongProfile.Message"));
                return;
            }

            await _contentDownloadService.ResumeDownloadAsync(item.Id);
        }
        else
        {
            await _contentDownloadService.PauseDownloadAsync(item.Id);
        }
    }

    [RelayCommand]
    private async Task StopAllDownloadsAsync()
    {
        try
        {
            // Pause active downloads
            foreach (var item in ActiveDownloadingItems.Where(d => !d.IsPaused).ToList())
            {
                await _contentDownloadService.PauseDownloadAsync(item.Id);
            }

            // Also pause queued items so they don't start automatically
            foreach (var item in QueuedDownloadItems.ToList())
            {
                await _contentDownloadService.PauseDownloadAsync(item.Id);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop all downloads");
        }
    }

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                _localizationService.GetString("Downloads.Dialog.ClearQueue.Title"),
                _localizationService.GetString("Downloads.Dialog.ClearQueue.Message"));

            if (!confirmed) return;

            foreach (var item in QueuedDownloadItems.ToList())
            {
                await _contentDownloadService.CancelDownloadAsync(item.Id);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to clear download queue");
        }
    }

    partial void OnIsDownloadCenterVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDownloadsLandingEmptyState));
        if (!value && CurrentProfileId.HasValue && ActiveView == AppView.Downloads)
        {
            ScheduleDownloadsLandingRefresh(CurrentProfileId.Value);
        }
    }

    partial void OnShowDownloadsEmptyStateChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDownloadsLandingEmptyState));
    }

    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    private async Task RefreshPersonalListsFromDatabaseAsync()
    {
        await _refreshSemaphore.WaitAsync();
        try
        {
            if (!CurrentProfileId.HasValue)
            {
                MyList.Clear();
                FavoriteChannels.Clear();
                HistoryChannels.Clear();
                HistoryLiveChannels.Clear();
                HistoryVodChannels.Clear();
                DownloadedSeriesItems.Clear();
                DownloadedVodChannels.Clear();
                if (CurrentProfileId.HasValue)
                {
                    await RefreshDownloadsFromServiceAsync(CurrentProfileId.Value);
                }
                else
                {
                    ActiveDownloadItems.Clear();
                    SetDownloadCenterSummaryEmpty();
                }
                ShowMyListEmptyState = true;
                ShowFavoritesEmptyState = true;
                ShowHistoryEmptyState = true;
                ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                         DownloadedSeriesItems.Count == 0;
                return;
            }

            using var db = await _contextFactory.CreateDbContextAsync();
            await _watchHistoryService.CleanupOlderThanDaysAsync(CurrentProfileId.Value, 7);
            var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);

            if (profilePlaylistIds.Count == 0)
            {
                MyList.Clear();
                FavoriteChannels.Clear();
                HistoryChannels.Clear();
                HistoryLiveChannels.Clear();
                HistoryVodChannels.Clear();
                DownloadedSeriesItems.Clear();
                DownloadedVodChannels.Clear();
                await RefreshDownloadsFromServiceAsync(CurrentProfileId.Value);
                ShowMyListEmptyState = true;
                ShowFavoritesEmptyState = true;
                ShowHistoryEmptyState = true;
                ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                         DownloadedSeriesItems.Count == 0;
                return;
            }

            var myListChannels = await db.Channels
                .AsNoTracking()
                .Where(c => profilePlaylistIds.Contains(c.PlaylistId) && c.Type != ChannelType.Series && c.IsInMyList)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var myListSeries = await db.Series
                .AsNoTracking()
                .Where(s => profilePlaylistIds.Contains(s.PlaylistId) && s.IsInMyList)
                .OrderBy(s => s.Name)
                .ToListAsync();

            SetItems(MyList, myListChannels
                .Cast<object>()
                .Concat(myListSeries.Cast<object>())
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty),
                () => {
                    ShowMyListEmptyState = MyList.Count == 0;
                    _ = EnrichChannelsWithEpgAsync(MyList.OfType<Channel>());
                });

            var favoriteChannels = await db.Channels
                .AsNoTracking()
                .Where(c => profilePlaylistIds.Contains(c.PlaylistId) && c.Type != ChannelType.Series && c.IsFavorite)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var favoriteSeries = await db.Series
                .AsNoTracking()
                .Where(s => profilePlaylistIds.Contains(s.PlaylistId) && s.IsFavorite)
                .OrderBy(s => s.Name)
                .ToListAsync();

            SetItems(FavoriteChannels, favoriteChannels
                .Cast<object>()
                .Concat(favoriteSeries.Cast<object>())
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty),
                () => {
                    ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
                    _ = EnrichChannelsWithEpgAsync(FavoriteChannels.OfType<Channel>());
                });

            SetItems(HistoryChannels, await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds), () => {
                _ = UpdateHistoryBucketsAsync();
                _ = EnrichChannelsWithEpgAsync(HistoryChannels);
            });

            if (ActiveView == AppView.Downloads)
            {
                await RefreshDownloadedItemsFromDatabaseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh personal lists from database.");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private async Task RefreshHistoryChannelsOnlyAsync()
    {
        if (!CurrentProfileId.HasValue)
        {
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync();
        
        var retentionDays = _settingsService.Settings.WatchHistoryRetentionDays;
        if (retentionDays > 0)
        {
            await _watchHistoryService.CleanupOlderThanDaysAsync(CurrentProfileId.Value, retentionDays);
        }
        
        var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);

        if (profilePlaylistIds.Count == 0)
        {
            HistoryChannels.Clear();
            HistoryLiveChannels.Clear();
            HistoryVodChannels.Clear();
            ShowHistoryEmptyState = true;
            return;
        }

        _historyPage = 0;
        _hasMoreHistory = true;
        _isLoadingMoreHistory = false;

        try
        {
            var initialChannels = await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds, skip: 0, take: IncrementalPageSize);
            _historyPage = 1;
            _hasMoreHistory = initialChannels.Count == IncrementalPageSize;

            SetItems(HistoryChannels, initialChannels, () => _ = EnrichChannelsWithEpgAsync(HistoryChannels));
            _ = UpdateHistoryBucketsAsync(initialChannels);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh history channels only.");
        }
    }

    private static async Task<List<int>> GetProfilePlaylistIdsAsync(AppDbContext db, int profileId)
    {
        var activeIds = await db.Playlists
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync();

        if (activeIds.Count > 0)
        {
            return activeIds;
        }

        return await db.Playlists
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .Select(p => p.Id)
            .ToListAsync();
    }

    private async Task<List<Channel>> GetHistoryChannelsFromWatchHistoryAsync(AppDbContext db, List<int> profilePlaylistIds, int skip = 0, int take = 50)
    {
        if (!CurrentProfileId.HasValue)
        {
            return new List<Channel>();
        }

        var profileId = CurrentProfileId.Value;
        var histories = await db.WatchHistories
            .AsNoTracking()
            .Include(h => h.Channel)
            .Include(h => h.Episode)
                .ThenInclude(e => e!.Season)
                .ThenInclude(s => s!.Series)
            .Where(h => h.ProfileId == profileId &&
                        ((h.ChannelId.HasValue && h.Channel != null && profilePlaylistIds.Contains(h.Channel.PlaylistId)) ||
                         (h.EpisodeId.HasValue && h.Episode != null && h.Episode.Season != null && h.Episode.Season.Series != null &&
                          profilePlaylistIds.Contains(h.Episode.Season.Series.PlaylistId))))
            .OrderByDescending(h => h.WatchedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var result = new List<Channel>(histories.Count);
        foreach (var history in histories)
        {
            var resolvedPosition = ResolveHistoryPosition(history.StoppedAt, history.WatchedDuration);

            if (history.Channel != null)
            {
                var channelItem = history.Channel;
                channelItem.LastWatched = history.WatchedAt;
                if (resolvedPosition.HasValue && resolvedPosition.Value > TimeSpan.Zero)
                {
                    channelItem.WatchedPosition = resolvedPosition.Value;
                }
                if ((!channelItem.Duration.HasValue || channelItem.Duration.Value.TotalSeconds <= 0) &&
                    channelItem.WatchedPosition.HasValue &&
                    channelItem.WatchedPosition.Value.TotalSeconds > 0)
                {
                    // Unknown total duration: keep a visible partial progress instead of zero.
                    channelItem.Duration = channelItem.WatchedPosition.Value + TimeSpan.FromMinutes(30);
                }
                result.Add(channelItem);
                continue;
            }

            if (history.Episode?.Season?.Series == null)
            {
                continue;
            }

            var series = history.Episode.Season.Series;
            result.Add(new Channel
            {
                Id = 0,
                Name = history.Episode.Name,
                StreamUrl = history.Episode.StreamUrl,
                LogoUrl = history.Episode.CoverUrl ?? series.CoverUrl,
                Type = ChannelType.Series,
                PlaylistId = series.PlaylistId,
                LastWatched = history.WatchedAt,
                WatchedPosition = resolvedPosition ?? history.Episode.WatchedPosition,
                Duration = ResolveHistoryDuration(history.Episode.Duration, resolvedPosition ?? history.Episode.WatchedPosition)
            });
        }

        return result;
    }

    [RelayCommand]
    public async Task LoadMoreHistoryAsync()
    {
        if (!_hasMoreHistory || _isLoadingMoreHistory || !CurrentProfileId.HasValue)
        {
            return;
        }

        _isLoadingMoreHistory = true;

        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);
            
            var nextPage = await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds, 
                skip: _historyPage * IncrementalPageSize, 
                take: IncrementalPageSize);

            if (nextPage.Count == 0)
            {
                _hasMoreHistory = false;
                return;
            }

            _historyPage++;
            _hasMoreHistory = nextPage.Count == IncrementalPageSize;

            _dispatcherService.Invoke(() =>
            {
                foreach (var item in nextPage)
                {
                    HistoryChannels.Add(item);
                }
                _ = UpdateHistoryBucketsAsync();
                _ = EnrichChannelsWithEpgAsync(nextPage);
            });
        }
        finally
        {
            _isLoadingMoreHistory = false;
        }
    }

    public async Task LoadMoreHistoryIfNeededAsync(double verticalOffset, double scrollableHeight)
    {
        if (scrollableHeight <= 0)
        {
            return;
        }

        if ((verticalOffset / scrollableHeight) >= LoadMoreThreshold)
        {
            await LoadMoreHistoryAsync();
        }
    }

    private static TimeSpan? ResolveHistoryDuration(TimeSpan? duration, TimeSpan? watchedPosition)
    {
        if (duration.HasValue && duration.Value.TotalSeconds > 0)
        {
            return duration;
        }

        if (watchedPosition.HasValue && watchedPosition.Value.TotalSeconds > 0)
        {
            return watchedPosition.Value + TimeSpan.FromMinutes(30);
        }

        return duration;
    }

    private static TimeSpan? ResolveHistoryPosition(TimeSpan stoppedAt, TimeSpan watchedDuration)
    {
        if (stoppedAt > TimeSpan.Zero)
        {
            return stoppedAt;
        }

        if (watchedDuration > TimeSpan.Zero)
        {
            return watchedDuration;
        }

        return null;
    }

    private void UpdateSearchBuckets()
    {
        using var trace = _perfTrace?.BeginOperation("SEARCH", "UpdateSearchBuckets", $"len={SearchText?.Length ?? 0}");
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchLiveChannels.Clear();
            SearchSeriesChannels.Clear();
            SearchVodChannels.Clear();
            SearchSuggestion = string.Empty;
            SearchSimilarLiveChannels.Clear();
            SearchSimilarSeriesChannels.Clear();
            SearchSimilarVodChannels.Clear();
            ShowSearchSimilarSection = false;
            ShowSearchEmptyState = false;
            _perfTrace?.Event("SEARCH", "UpdateSearchBuckets cleared");
            return;
        }

        var rawQuery = SearchText.Trim();
        var normalizedSeriesQuery = NormalizeSeriesQuery(rawQuery);

        // Scoring and Sorting Logic
        int CalculateScore(string? name, string? group, ChannelType type)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            
            // Exact match is king
            if (name.Equals(rawQuery, StringComparison.OrdinalIgnoreCase)) return 100;
            if (!string.IsNullOrEmpty(normalizedSeriesQuery) && name.Equals(normalizedSeriesQuery, StringComparison.OrdinalIgnoreCase)) return 95;

            // Starts with is very strong
            if (name.StartsWith(rawQuery, StringComparison.OrdinalIgnoreCase)) return 80;
            if (!string.IsNullOrEmpty(normalizedSeriesQuery) && name.StartsWith(normalizedSeriesQuery, StringComparison.OrdinalIgnoreCase)) return 75;

            // Contains as a word boundary
            if (name.Contains(" " + rawQuery, StringComparison.OrdinalIgnoreCase) || name.Contains("-" + rawQuery, StringComparison.OrdinalIgnoreCase)) return 60;

            // Contains anywhere
            if (name.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)) return 40;
            if (!string.IsNullOrEmpty(normalizedSeriesQuery) && name.Contains(normalizedSeriesQuery, StringComparison.OrdinalIgnoreCase)) return 35;

            // Group match
            if (!string.IsNullOrWhiteSpace(group) && group.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)) return 20;

            return 0;
        }

        var channelsSnapshot = FilteredChannels.ToList();

        SetItems(SearchLiveChannels, channelsSnapshot
            .Where(c => c.Type == ChannelType.Live)
            .Select(c => new { Item = c, Score = CalculateScore(c.Name, c.GroupTitle, c.Type) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => HasDisplayImage(x.Item))
            .ThenBy(x => x.Item.Name)
            .Select(x => x.Item));

        var hiddenSeriesGroups = _settingsService.Settings.HiddenSeriesGroups;
        var seriesSnapshot = _allSeriesCache.Where(s => s.GroupTitle == null || !hiddenSeriesGroups.Contains(s.GroupTitle)).ToList();
        
        int CalculateSeriesScore(Series s)
        {
            var nameScore = CalculateScore(s.Name, s.Genre, ChannelType.Series);
            if (nameScore >= 40) return nameScore; // Name match is enough

            // Only check episodes if query is long enough to avoid noise like "%3" matching "Episode 3"
            if (rawQuery.Length >= 3)
            {
                if (s.Seasons.SelectMany(sea => sea.Episodes).Any(ep => ep.Name?.Contains(rawQuery, StringComparison.OrdinalIgnoreCase) ?? false))
                    return 10;
            }
            
            return 0;
        }

        SetItems(SearchSeriesChannels, seriesSnapshot
            .Select(s => new { Item = s, Score = CalculateSeriesScore(s) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => HasDisplayImage(x.Item))
            .ThenBy(x => x.Item.Name)
            .Select(x => x.Item));

        SetItems(SearchVodChannels, channelsSnapshot
            .Where(c => c.Type == ChannelType.VOD)
            .Select(c => new { Item = c, Score = CalculateScore(c.Name, c.GroupTitle, c.Type) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => HasDisplayImage(x.Item))
            .ThenBy(x => x.Item.Name)
            .Select(x => x.Item));

        var hasAnyExact = SearchLiveChannels.Count > 0
            || SearchSeriesChannels.Count > 0
            || SearchVodChannels.Count > 0;

        UpdateSearchSuggestionAndSimilar(rawQuery, seriesSnapshot);
        _perfTrace?.Counter("SEARCH", "SearchLiveChannels", SearchLiveChannels.Count);
        _perfTrace?.Counter("SEARCH", "SearchSeriesChannels", SearchSeriesChannels.Count);
        _perfTrace?.Counter("SEARCH", "SearchVodChannels", SearchVodChannels.Count);

        ShowSearchEmptyState = !hasAnyExact && !ShowSearchSimilarSection;
    }

    private void UpdateSearchSuggestionAndSimilar(string rawQuery, List<Series> seriesSnapshot)
    {
        var normalizedQuery = NormalizeFuzzyText(rawQuery);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            SearchSuggestion = string.Empty;
            SearchSimilarLiveChannels.Clear();
            SearchSimilarSeriesChannels.Clear();
            SearchSimilarVodChannels.Clear();
            ShowSearchSimilarSection = false;
            return;
        }

        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in Channels)
        {
            if (!string.IsNullOrWhiteSpace(channel.Name))
            {
                candidateNames.Add(channel.Name);
            }
        }

        foreach (var series in seriesSnapshot)
        {
            if (!string.IsNullOrWhiteSpace(series.Name))
            {
                candidateNames.Add(series.Name);
            }
        }

        SearchSuggestion = ComputeBestSuggestion(rawQuery, candidateNames);

        // Precompute ID sets for O(1) deduplication instead of O(n*m) Any()
        var liveIds = new HashSet<int>(SearchLiveChannels.Select(x => x.Id));
        var seriesIds = new HashSet<int>(SearchSeriesChannels.Select(x => x.Id));
        var vodIds = new HashSet<int>(SearchVodChannels.Select(x => x.Id));

        var similarLive = Channels
            .Where(c => c.Type == ChannelType.Live)
            .Where(c => IsLikelySimilar(rawQuery, c.Name))
            .Where(c => !liveIds.Contains(c.Id))
            .OrderByDescending(HasDisplayImage)
            .Take(12)
            .ToList();

        var similarSeries = seriesSnapshot
            .Where(s => IsLikelySimilar(rawQuery, s.Name))
            .Where(s => !seriesIds.Contains(s.Id))
            .OrderByDescending(HasDisplayImage)
            .Take(12)
            .ToList();

        var similarVod = Channels
            .Where(c => c.Type == ChannelType.VOD)
            .Where(c => IsLikelySimilar(rawQuery, c.Name))
            .Where(c => !vodIds.Contains(c.Id))
            .OrderByDescending(HasDisplayImage)
            .Take(12)
            .ToList();

        SetItems(SearchSimilarLiveChannels, similarLive);
        SetItems(SearchSimilarSeriesChannels, similarSeries);
        SetItems(SearchSimilarVodChannels, similarVod);
        ShowSearchSimilarSection = similarLive.Count > 0 || similarSeries.Count > 0 || similarVod.Count > 0;
    }

    private string ComputeBestSuggestion(string query, IEnumerable<string> candidates)
    {
        var normalizedQuery = NormalizeFuzzyText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery) || normalizedQuery.Length < 2)
        {
            return string.Empty;
        }

        string best = string.Empty;
        double bestScore = 0; // Higher is better

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var normalizedCandidate = NormalizeFuzzyText(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate) || normalizedCandidate == normalizedQuery)
                continue;

            double similarity = GetFuzzySimilarity(normalizedQuery, normalizedCandidate);

            // Favor names that start with the query
            if (normalizedCandidate.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                similarity += 0.15;
            }
            else if (normalizedCandidate.StartsWith(normalizedQuery[..Math.Min(2, normalizedQuery.Length)], StringComparison.OrdinalIgnoreCase))
            {
                similarity += 0.05;
            }

            // Penalty for extra length (prefer shorter matches when query is short)
            double lengthDiffPenalty = Math.Abs(normalizedCandidate.Length - normalizedQuery.Length) * 0.01;
            similarity -= lengthDiffPenalty;

            // CRITICAL: Avoid suggesting a specific episode (Sxx Exx) if the query is just the series name
            // and we likely already found the series.
            if (normalizedCandidate.Contains(" s0") || normalizedCandidate.Contains(" s1") || 
                normalizedCandidate.Contains(" buelum") || normalizedCandidate.Contains(" episode"))
            {
                // If the query doesn't contain season/episode info, penalize candidates that do.
                if (!normalizedQuery.Contains(" s0") && !normalizedQuery.Contains(" s1") && 
                    !normalizedQuery.Contains(" buelum") && !normalizedQuery.Contains(" episode"))
                {
                    similarity -= 0.3;
                }
            }

            if (similarity > bestScore && similarity > 0.80)
            {
                bestScore = similarity;
                best = candidate;
            }
        }

        // If the best suggestion is basically the same as what we already found in exact results, skip it.
        if (!string.IsNullOrEmpty(best))
        {
            var bestLower = best.ToLowerInvariant();
            if (SearchSeriesChannels.Any(s => s.Name?.ToLowerInvariant() == bestLower) ||
                SearchLiveChannels.Any(c => c.Name?.ToLowerInvariant() == bestLower) ||
                SearchVodChannels.Any(c => c.Name?.ToLowerInvariant() == bestLower))
            {
                return string.Empty;
            }
        }

        return best;
    }

    private static bool IsLikelySimilar(string query, string? candidate)
    {
        var normalizedQuery = NormalizeFuzzyText(query);
        var normalizedCandidate = NormalizeFuzzyText(candidate);
        
        if (string.IsNullOrWhiteSpace(normalizedQuery) || string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return false;
        }

        if (normalizedCandidate.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
            normalizedQuery.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetFuzzySimilarity(normalizedQuery, normalizedCandidate) >= 0.70;
    }

    private static double GetFuzzySimilarity(string normalizedQuery, string normalizedCandidate)
    {
        var qWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cWords = normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (qWords.Length == 0 || cWords.Length == 0) return 0;

        double totalScore = 0;

        foreach (var qw in qWords)
        {
            double bestWordScore = 0;
            foreach (var cw in cWords)
            {
                if (cw == qw)
                {
                    bestWordScore = 1.0;
                    break;
                }
                
                if (cw.StartsWith(qw, StringComparison.OrdinalIgnoreCase))
                {
                    bestWordScore = Math.Max(bestWordScore, 0.9);
                }
                else if (cw.Contains(qw, StringComparison.OrdinalIgnoreCase))
                {
                    bestWordScore = Math.Max(bestWordScore, 0.6);
                }

                int maxDist = GetDistanceThreshold(Math.Max(qw.Length, cw.Length));
                
                // Daha esnek uzunluk kontrolü
                if (Math.Abs(qw.Length - cw.Length) > maxDist + 1) continue;

                int dist = LevenshteinDistance(qw, cw, maxDist);
                if (dist >= 0)
                {
                    double score = 1.0 - ((double)dist / Math.Max(qw.Length, cw.Length));
                    if (score > bestWordScore)
                    {
                        bestWordScore = score;
                    }
                }
            }
            totalScore += bestWordScore;
        }

        // Adayda çok fazla ekstra kelime varsa ufak bir ceza (aşırı eşleşmeyi önler)
        double penalty = 0;
        if (cWords.Length > qWords.Length + 2) 
        {
            penalty = (cWords.Length - qWords.Length - 2) * 0.05;
        }

        return Math.Max(0, (totalScore / qWords.Length) - penalty);
    }

    private static int GetDistanceThreshold(int length)
    {
        if (length <= 4) return 1;
        if (length <= 7) return 2;
        if (length <= 11) return 3;
        if (length <= 15) return 4;
        return 5;
    }

    private static string NormalizeFuzzyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Normalize Turkish characters to ASCII and lowercase
        var normalized = value.Trim().ToLowerInvariant();

        normalized = normalized
            .Replace('ı', 'i')
            .Replace('ş', 's')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ö', 'o')
            .Replace('ç', 'c')
            .Replace('İ', 'i')
            .Replace('I', 'i');

        // Remove special characters, keep letters and digits
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{Nd}\s]", " ");
        // Collapse multiple spaces
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }

    private static int LevenshteinDistance(string source, string target, int maxDistance)
    {
        if (source == target) return 0;

        int n = source.Length;
        int m = target.Length;

        if (Math.Abs(n - m) > maxDistance) return -1;

        // Damerau-Levenshtein (Optimal String Alignment) variant
        var pool = System.Buffers.ArrayPool<int>.Shared;
        var pprev = pool.Rent(m + 1);
        var prev = pool.Rent(m + 1);
        var curr = pool.Rent(m + 1);

        try
        {
            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                int rowMin = curr[0];

                for (int j = 1; j <= m; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);

                    // Transposition check (Damerau-Levenshtein)
                    if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                    {
                        curr[j] = Math.Min(curr[j], pprev[j - 2] + 1);
                    }

                    if (curr[j] < rowMin) rowMin = curr[j];
                }

                if (rowMin > maxDistance) return -1;

                // Rotate rows: pprev = prev; prev = curr;
                Array.Copy(prev, pprev, m + 1);
                Array.Copy(curr, prev, m + 1);
            }

            return prev[m] <= maxDistance ? prev[m] : -1;
        }
        finally
        {
            pool.Return(pprev);
            pool.Return(prev);
            pool.Return(curr);
        }
    }

    private void UpdateSeriesViewItems()
    {
        using var trace = _perfTrace?.BeginOperation(
            "FILTER",
            "UpdateSeriesViewItems",
            $"source={_allSeriesCache.Count} group={SelectedGroup ?? "<all>"} search={(string.IsNullOrWhiteSpace(SearchText) ? "<empty>" : SearchText)}");
        var source = _allSeriesCache;
        if (source.Count == 0)
        {
            ResetSeriesIncrementalState();
            _seriesFilteredSource.Clear();
            SeriesViewItems.Clear();
            NotifyContentStateChanged();
            return;
        }

        var query = SearchText?.Trim() ?? string.Empty;
        var normalizedQuery = NormalizeSeriesQuery(query);
        var selectedGroup = SelectedGroup?.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(query);
        var s = _settingsService.Settings;

        var filtered = source.Where(series =>
        {
            // Filter out hidden groups
            if (series.GroupTitle != null && s.HiddenSeriesGroups.Contains(series.GroupTitle))
            {
                return false;
            }

            var groupOk = true;
            if (!hasSearch && !string.IsNullOrWhiteSpace(selectedGroup))
            {
                var category = series.GroupTitle ?? string.Empty;
                groupOk = category.Contains(selectedGroup, StringComparison.OrdinalIgnoreCase);
            }

            if (!groupOk)
            {
                return false;
            }

            if (!hasSearch)
            {
                return true;
            }

            return SeriesMatchesSearch(series, query, normalizedQuery);
        });

        filtered = SelectedSortOrder switch
        {
            ChannelSortOrder.NameAsc => filtered.OrderBy(s => s.Name),
            ChannelSortOrder.NameDesc => filtered.OrderByDescending(s => s.Name),
            ChannelSortOrder.OldestFirst => filtered.OrderBy(s => s.ReleaseYear ?? int.MaxValue).ThenBy(s => s.Name),
            _ => filtered.OrderByDescending(s => s.ReleaseYear ?? 0).ThenBy(s => s.Name)
        };

        _seriesFilteredSource = filtered.ToList();
        _perfTrace?.Counter("FILTER", "SeriesFilteredSource", _seriesFilteredSource.Count, $"source={source.Count}");
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = true;
        SeriesViewItems.Clear();
        _ = LoadMoreSeriesAsync();
        NotifyContentStateChanged();
    }


    [RelayCommand]
    private void CommitSearch()
    {
        _perfTrace?.Event("SEARCH", "CommitSearch", $"len={SearchQuery?.Length ?? 0}");
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            // Sync with global search and navigate
            SearchText = SearchQuery;
            Navigate(AppView.Search);
        }
    }

    [RelayCommand]
    private void ApplySearchSuggestion()
    {
        _perfTrace?.Event("SEARCH", "ApplySearchSuggestion", $"len={SearchSuggestion?.Length ?? 0}");
        if (string.IsNullOrWhiteSpace(SearchSuggestion))
        {
            return;
        }

        SearchQuery = SearchSuggestion;
        SearchText = SearchSuggestion;
        Navigate(AppView.Search);
    }


    partial void OnSearchQueryChanged(string value)
    {
        _perfTrace?.Event("SEARCH", "OnSearchQueryChanged", $"len={value?.Length ?? 0}");
        // Only clear results when query is emptied.
        // Actual search triggers on Enter/button via CommitSearch.
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
        }
    }

    private async Task SearchOverlayAsync(string query, CancellationToken token)
    {
        using var trace = _perfTrace?.BeginOperation("SEARCH", "SearchOverlayAsync", $"len={query?.Length ?? 0}");
        try
        {
            await Task.Delay(_searchDelayMs, token);

            var channelsSnapshot = Channels;
            var seriesSnapshot = _allSeriesCache;
            var searchLower = query.ToLowerInvariant();

            var results = await Task.Run(() =>
            {
                var localResults = new List<object>();
                var normalizedSeriesQuery = NormalizeSeriesQuery(query);

                localResults.AddRange(channelsSnapshot.Where(c =>
                    c.Type != ChannelType.Series &&
                    (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     (c.GroupTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
                    .OrderByDescending(HasDisplayImage)
                    .ThenBy(c => c.Name)
                    .Take(10));

                localResults.AddRange(seriesSnapshot.Where(s =>
                    SeriesMatchesSearch(s, query, normalizedSeriesQuery))
                    .OrderByDescending(HasDisplayImage)
                    .ThenBy(s => s.Name)
                    .Take(10));

                return localResults;
            }, token);

            if (token.IsCancellationRequested) return;
            _perfTrace?.Counter("SEARCH", "SearchOverlay results", results.Count, $"channels={channelsSnapshot.Count} series={seriesSnapshot.Count}");

            _dispatcherService.BeginInvoke(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    SetItems(SearchResults, results);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 3sn'de timeout → sunucu yanıt vermiyor
            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = _localizationService.GetString("Main.Status.ServerNoResponse");
            });
        }
        catch (ObjectDisposedException)
        {
            // Ignore races from rapid search token replacement.
        }
    }

    [RelayCommand]
    private async Task AddToMyList(object media)
    {
        if (!CurrentProfileId.HasValue)
        {
            StatusMessage = _localizationService.GetString("Main.Status.SelectProfileFirst");
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync();
        
        if (media is Channel channel)
        {

            if (channel.Type == ChannelType.Live)
            {
                StatusMessage = _localizationService.GetString("Main.Status.LiveCannotAddToList");
                return;
            }

            if (channel.PlaylistId <= 0)
            {
                channel.PlaylistId = SelectedPlaylist?.Id
                    ?? await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
            }

            channel.IsInMyList = !channel.IsInMyList;
            await _channelService.UpdateChannelAsync(channel);
            StatusMessage = channel.IsInMyList
                ? _localizationService.GetString("Main.Status.ListAdded")
                : _localizationService.GetString("Main.Status.RemovedFromList");
        }
        else if (media is Series series)
        {
            series.IsInMyList = !series.IsInMyList;
            await _mediaService.UpdateSeriesAsync(series);
            var seriesFromDb = await db.Series
                .Include(s => s.Seasons)
                .ThenInclude(sn => sn.Episodes)
                .FirstOrDefaultAsync(s => s.Id == series.Id);

            if (seriesFromDb != null)
            {
                var episodeUrls = seriesFromDb.Seasons
                    .SelectMany(sn => sn.Episodes)
                    .Select(ep => ep.StreamUrl)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct()
                    .ToList();

                if (episodeUrls.Count > 0)
                {
                    var relatedChannels = await db.Channels
                        .Where(c => episodeUrls.Contains(c.StreamUrl))
                        .ToListAsync();

                    foreach (var relatedChannel in relatedChannels)
                    {
                        relatedChannel.IsInMyList = series.IsInMyList;
                    }

                    await db.SaveChangesAsync();
                }
            }

            StatusMessage = series.IsInMyList
                ? _localizationService.GetString("Main.Status.ListAdded")
                : _localizationService.GetString("Main.Status.RemovedFromList");
        }

        UpdateMyList();
        UpdateFavoriteChannels();
        UpdateHistoryChannels();
        await RefreshPersonalListsFromDatabaseAsync();
        ScheduleImmediateFilter();
    }

    [RelayCommand]
    private async Task RemoveFromMyList(object media)
    {
        if (!CurrentProfileId.HasValue)
        {
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync();

        if (media is Channel channel)
        {
            if (channel.PlaylistId <= 0)
            {
                channel.PlaylistId = SelectedPlaylist?.Id
                    ?? await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
            }

            if (channel.IsInMyList)
            {
                channel.IsInMyList = false;
                await _channelService.UpdateChannelAsync(channel);
            }
        }
        else if (media is Series series && series.IsInMyList)
        {
            series.IsInMyList = false;
            await _mediaService.UpdateSeriesAsync(series);

            var seriesFromDb = await db.Series
                .Include(s => s.Seasons)
                .ThenInclude(sn => sn.Episodes)
                .FirstOrDefaultAsync(s => s.Id == series.Id);

            if (seriesFromDb != null)
            {
                var episodeUrls = seriesFromDb.Seasons
                    .SelectMany(sn => sn.Episodes)
                    .Select(ep => ep.StreamUrl)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct()
                    .ToList();

                if (episodeUrls.Count > 0)
                {
                    var relatedChannels = await db.Channels
                        .Where(c => episodeUrls.Contains(c.StreamUrl))
                        .ToListAsync();

                    foreach (var relatedChannel in relatedChannels)
                    {
                        relatedChannel.IsInMyList = false;
                    }

                    await db.SaveChangesAsync();
                }
            }
        }

        StatusMessage = _localizationService.GetString("Main.Status.RemovedFromList");
        await RefreshPersonalListsFromDatabaseAsync();
    }

    [RelayCommand]
    private async Task RemoveFromHistoryAsync(object? media)
    {
        if (media == null || !CurrentProfileId.HasValue) return;

        try
        {
            int? channelId = null;
            int? seriesId = null;

            if (media is Channel channel)
            {
                channelId = channel.Id;
                
                // Update local state to hide it from history lists immediately
                channel.LastWatched = null;
                channel.WatchedPosition = TimeSpan.Zero;
                channel.IsCompleted = false;

                HistoryChannels.Remove(channel);
                ContinueWatching.Remove(channel);
                if (channel.Type == ChannelType.Live) HistoryLiveChannels.Remove(channel);
                else if (channel.Type == ChannelType.VOD) HistoryVodChannels.Remove(channel);
            }
            else if (media is Series series)
            {
                seriesId = series.Id;
                
                // Update local episodes state
                if (series.Seasons != null)
                {
                    foreach (var s in series.Seasons)
                    {
                        if (s.Episodes != null)
                        {
                            foreach (var e in s.Episodes)
                            {
                                e.LastWatched = null;
                                e.WatchedPosition = TimeSpan.Zero;
                                e.IsCompleted = false;
                            }
                        }
                    }
                }
                HistorySeriesItems.Remove(series);
            }

            await _watchHistoryService.RemoveFromHistoryAsync(CurrentProfileId.Value, channelId, seriesId);
            
            ShowHistoryEmptyState = HistoryLiveChannels.Count == 0 && 
                                  HistoryVodChannels.Count == 0 && 
                                  HistorySeriesItems.Count == 0;
            
            // Re-update internal buckets for safety and update home rail
            _ = UpdateHistoryBucketsAsync();
            _ = UpdateContinueWatchingRailAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Geçmişten silinirken hata oluştu");
        }
    }

    [RelayCommand]
    private async Task RemoveFromFavorites(object media)
    {
        if (!CurrentProfileId.HasValue)
        {
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync();

        if (media is Channel channel)
        {
            if (channel.PlaylistId <= 0)
            {
                channel.PlaylistId = SelectedPlaylist?.Id
                    ?? await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
            }

            if (channel.IsFavorite)
            {
                channel.IsFavorite = false;
                await _channelService.UpdateChannelAsync(channel);
            }
        }
        else if (media is Series series)
        {
            if (series.PlaylistId <= 0)
            {
                series.PlaylistId = SelectedPlaylist?.Id
                    ?? await db.Playlists
                        .AsNoTracking()
                        .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
            }

            series.IsFavorite = false;
            await _mediaService.UpdateSeriesAsync(series);

            var seriesFromDb = await db.Series
                .Include(s => s.Seasons)
                .ThenInclude(sn => sn.Episodes)
                .FirstOrDefaultAsync(s =>
                    s.Id == series.Id ||
                    (s.PlaylistId == series.PlaylistId && s.Name == series.Name));

            if (seriesFromDb != null)
            {
                seriesFromDb.IsFavorite = false;

                var episodeUrls = seriesFromDb.Seasons
                    .SelectMany(sn => sn.Episodes)
                    .Select(ep => ep.StreamUrl)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct()
                    .ToList();

                if (episodeUrls.Count > 0)
                {
                    var relatedChannels = await db.Channels
                        .Where(c => episodeUrls.Contains(c.StreamUrl))
                        .ToListAsync();

                    foreach (var relatedChannel in relatedChannels)
                    {
                        relatedChannel.IsFavorite = false;
                    }
                }

                await db.SaveChangesAsync();
            }
        }
        else
        {
            return;
        }

        StatusMessage = _localizationService.GetString("Main.Status.RemovedFromFavorites");
        await RefreshPersonalListsFromDatabaseAsync();
    }

    [RelayCommand]
    private void PlayEpisode(Episode episode)
    {
        _ = PlayEpisodeSafeAsync(episode);
    }

    [RelayCommand]
    private void WatchTrailer()
    {
        if (!string.IsNullOrWhiteSpace(SelectedSeriesTrailerUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SelectedSeriesTrailerUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to open trailer URL");
            }
        }
    }

    [RelayCommand]
    private async Task DownloadEpisode(Episode? episode)
    {
        if (episode == null || !CurrentProfileId.HasValue) return;

        if (string.IsNullOrWhiteSpace(episode.StreamUrl))
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Download.Error.SourceNotFoundFormat"), episode.Name);
            return;
        }

        try
        {
            var request = new DownloadContentRequest(
                CurrentProfileId.Value,
                DownloadItemType.SeriesEpisode,
                episode.Name,
                episode.StreamUrl,
                SelectedSeries?.CoverUrl,
                SelectedPlaylist?.Id ?? 0,
                0,
                episode.Id);

            var result = await _contentDownloadService.QueueDownloadAsync(request);
            StatusMessage = result.Success
                ? string.Format(CultureInfo.CurrentCulture,
                    _localizationService.GetString("Download.Status.AddedFormat"), episode.Name)
                : result.AlreadyExists
                    ? string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("Download.Status.AlreadyExistsFormat"), episode.Name)
                    : string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("Download.Status.ErrorFormat"), result.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Episode download failed: {Name}", episode.Name);
            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Download.Status.ExceptionFormat"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedSeason()
    {
        if (SelectedSeason == null || !CurrentProfileId.HasValue) return;

        var episodes = SelectedSeason.Episodes
            .OrderBy(e => e.EpisodeNumber)
            .ToList();
        
        if (episodes.Count == 0) return;

        int queued = 0;
        int skipped = 0;
        int failed = 0;

        StatusMessage = string.Format(CultureInfo.CurrentCulture,
            _localizationService.GetString("Download.Season.StartingFormat"),
            SelectedSeason.SeasonNumber,
            episodes.Count);

        foreach (var episode in episodes)
        {
            if (string.IsNullOrWhiteSpace(episode.StreamUrl))
            {
                failed++;
                continue;
            }

            try
            {
                var request = new DownloadContentRequest(
                    CurrentProfileId.Value,
                    DownloadItemType.SeriesEpisode,
                    episode.Name,
                    episode.StreamUrl,
                    SelectedSeries?.CoverUrl,
                    SelectedPlaylist?.Id ?? 0,
                    0,
                    episode.Id);

                var result = await _contentDownloadService.QueueDownloadAsync(request);
                if (result.Success) queued++;
                else if (result.AlreadyExists) skipped++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Season download failed for episode: {Name}", episode.Name);
            }
        }

        var parts = new List<string>();
        if (queued > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Download.Season.Result.QueuedFormat"), queued));
        if (failed > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Download.Season.Result.FailedFormat"), failed));
        if (skipped > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("Download.Season.Result.SkippedFormat"), skipped));
        StatusMessage = string.Format(CultureInfo.CurrentCulture,
            _localizationService.GetString("Download.Season.ResultFormat"),
            SelectedSeason.SeasonNumber,
            string.Join(", ", parts));
    }

    private async Task PlayEpisodeSafeAsync(Episode? episode)
    {
        try
        {
            await PlayEpisodeInternalAsync(episode);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[HealthCheck] {Msg}", ex.Message);
            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = _localizationService.GetString("Main.Status.PlaylistUnreachable");
            });
        }
    }

    private async Task PlayEpisodeInternalAsync(Episode? episode)
    {
        if (episode == null)
        {
            return;
        }

        var preferredStreamUrl = await ResolvePreferredStreamUrlAsync(episode.StreamUrl);
        if (!string.Equals(preferredStreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase))
        {
            episode = new Episode
            {
                Id = episode.Id,
                EpisodeNumber = episode.EpisodeNumber,
                Name = episode.Name,
                StreamUrl = preferredStreamUrl,
                Plot = episode.Plot,
                CoverUrl = episode.CoverUrl,
                Duration = episode.Duration,
                LastWatched = episode.LastWatched,
                WatchedPosition = episode.WatchedPosition,
                SeasonId = episode.SeasonId,
                IntroStartSec = episode.IntroStartSec,
                IntroEndSec = episode.IntroEndSec,
                CreditsStartSec = episode.CreditsStartSec,
                IsCompleted = episode.IsCompleted,
                Season = episode.Season
            };
        }

        var channel = BuildSeriesEpisodeChannel(episode);
        CurrentEpisodePlaybackContext = episode;
        CurrentSeriesPlaybackContext = ResolveSeriesForEpisode(episode);
        NextEpisodePlaybackContext = FindNextEpisode(episode);
        
        SelectChannel(channel);
        IsSeriesDetailVisible = false;
    }

    [RelayCommand]
    private void CloseSeriesDetail()
    {
        IsSeriesDetailVisible = false;
        _seriesDetailDownloadedOnlyMode = false;
        OnPropertyChanged(nameof(IsDownloadedSeriesDetailMode));
        SelectedSeries = null;
        SelectedSeriesPosterUrl = null;
        SelectedSeriesBackdropUrl = null;
        SelectedSeriesTrailerUrl = null;
        SelectedSeriesNetworkLogoUrl = null;
        SelectedSeriesOverview = string.Empty;
        SelectedSeriesCast = string.Empty;
    }


    [RelayCommand]
    private void EditChannel(Channel channel)
    {
        if (channel == null) return;
        
        // This requires UI interaction (opening a window). 
        // In clean MVVM, we'd use a DialogService. 
        // For simplicity here, we'll raise an event or use a service if available.
        // Let's assume a DialogService interface or event.
        
        RequestEditChannel?.Invoke(channel);
    }

    public event Action<Channel>? RequestEditChannel;

    // Event for media selection - MainWindow subscribes to this for video playback
    public event Action<object>? OnMediaSelected;

    [RelayCommand]
    private async Task SelectMedia(object? media)
    {
        if (media == null) return;

        var selectionVersion = BeginMediaSelectionIntent();

        SearchQuery = string.Empty;

        if (media is Channel channel)
        {
            var resolvedStreamUrl = await ResolvePreferredStreamUrlAsync(channel.StreamUrl);
            if (!IsMediaSelectionIntentCurrent(selectionVersion))
            {
                return;
            }

            channel.StreamUrl = resolvedStreamUrl;

            if (channel.Type == ChannelType.Series)
            {
                TryPrepareEpisodePlaybackContext(channel);
            }
            else
            {
                CurrentEpisodePlaybackContext = null;
                NextEpisodePlaybackContext = null;
                CurrentSeriesPlaybackContext = null;
                
                if (channel.Type == ChannelType.Live)
                {
                    if (ActiveView == AppView.Live && FilteredChannels.Any(c => c.Id == channel.Id))
                    {
                        _livePlaybackContext = FilteredChannels.ToList();
                    }
                    else
                    {
                        var groupChannels = Channels.Where(c => c.GroupTitle == channel.GroupTitle && c.Type == ChannelType.Live).ToList();
                        
                        if (groupChannels.Count == 0 && channel.PlaylistId > 0)
                        {
                            using var db = await _contextFactory.CreateDbContextAsync();
                            groupChannels = await db.Channels
                                .Where(c => c.PlaylistId == channel.PlaylistId && c.Type == ChannelType.Live && c.GroupTitle == channel.GroupTitle)
                                .ToListAsync();

                            if (!IsMediaSelectionIntentCurrent(selectionVersion))
                            {
                                return;
                            }
                        }

                        _livePlaybackContext = SelectedSortOrder switch
                        {
                            ChannelSortOrder.NameAsc => groupChannels.OrderBy(c => c.Name).ToList(),
                            ChannelSortOrder.NameDesc => groupChannels.OrderByDescending(c => c.Name).ToList(),
                            ChannelSortOrder.OldestFirst => groupChannels.OrderBy(c => c.Id).ToList(),
                            _ => groupChannels.OrderByDescending(c => c.Id).ToList()
                        };
                    }
                }
                else
                {
                    _livePlaybackContext = null;
                }
            }

            if (!IsMediaSelectionIntentCurrent(selectionVersion))
            {
                return;
            }

            SelectedChannel = channel;
            OnMediaSelected?.Invoke(channel);
        }
        else if (media is Series series)
        {
            var selectedSeries = series;

            // Phase 27: If we're in Downloads view and this is a synthetic series
            // (from filesystem scanner, Id <= 0), do NOT re-fetch from DB — it would
            // overwrite our local file paths with the provider's remote URLs.
            var isSyntheticDownloadSeries = ActiveView == AppView.Downloads && series.Id <= 0;

            if (!isSyntheticDownloadSeries)
            {
                try
                {
                    selectedSeries = await LoadSeriesWithProfileProgressAsync(series);
                    // Xtream API is authoritative — skip M3U-style fallback that creates bogus Season 0
                    if (CurrentProfile?.ProviderAccount?.Type != ProfileType.XtreamCodes)
                    {
                        EnsureSeriesEpisodes(selectedSeries);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"EnsureSeriesEpisodes failed: {ex.Message}");
                }
            }

            if (!IsMediaSelectionIntentCurrent(selectionVersion))
            {
                return;
            }

            _seriesDetailDownloadedOnlyMode = ActiveView == AppView.Downloads;
            OnPropertyChanged(nameof(IsDownloadedSeriesDetailMode));
            if (_seriesDetailDownloadedOnlyMode && !isSyntheticDownloadSeries)
            {
                selectedSeries = BuildDownloadedOnlySeries(selectedSeries);
            }

            SelectedSeries = selectedSeries;
            IsSeriesDetailVisible = true;
            OnMediaSelected?.Invoke(selectedSeries);
            _ = LoadSelectedSeriesMetadataAsync(selectedSeries);
        }
    }


    private static string NormalizeSeriesQuery(string query) => SeriesInfoParser.NormalizeKey(query);

    private static string ExtractSeriesBaseName(string? displayName) => SeriesInfoParser.Parse(displayName).SeriesName;

    private static (int SeasonNumber, int EpisodeNumber) ParseEpisodeNumbers(string title)
    {
        var info = SeriesInfoParser.Parse(title);
        return (info.Season, info.Episode);
    }

    private static string NormalizeSeriesTitleForMatching(string? value) => SeriesInfoParser.NormalizeKey(value);

    private async Task<string> ResolvePreferredStreamUrlAsync(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return string.Empty;
        }

        if (ActiveView == AppView.Downloads || !NetworkInterface.GetIsNetworkAvailable())
        {
            return streamUrl;
        }

        if (!IsDownloadedStreamUrl(streamUrl))
        {
            return streamUrl;
        }

        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var item = await db.DownloadItems
                .AsNoTracking()
                .Where(d => d.Status == DownloadStatus.Completed &&
                            d.LocalFilePath == streamUrl &&
                            !string.IsNullOrWhiteSpace(d.SourceUrl))
                .OrderByDescending(d => d.CompletedAt ?? d.UpdatedAt)
                .FirstOrDefaultAsync();

            return item?.SourceUrl ?? streamUrl;
        }
        catch
        {
            return streamUrl;
        }
    }

    private static bool SeriesMatchesSearch(Series series, string rawQuery, string normalizedQuery)
    {
        var seriesName = series.Name ?? string.Empty;
        var genre = series.Genre ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(normalizedQuery) && seriesName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (genre.Contains(rawQuery, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(normalizedQuery) && genre.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var episodes = series.Seasons.SelectMany(s => s.Episodes);
        return episodes.Any(ep =>
        {
            var episodeName = ep.Name ?? string.Empty;
            return episodeName.Contains(rawQuery, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(normalizedQuery) && episodeName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        });
    }

    private Task LoadSelectedSeriesMetadataAsync(Series series)
    {
        try
        {
            IsSelectedSeriesMetadataLoading = true;
            NormalizeSeriesDetailForDisplay(series);

            SelectedSeriesPosterUrl = series.CoverUrl;
            SelectedSeriesBackdropUrl = series.BackdropUrl;
            SelectedSeriesOverview = series.Plot ?? string.Empty;
            SelectedSeriesCast = series.Cast ?? string.Empty;
            SelectedSeriesGenres = series.Genre ?? string.Empty;
            SelectedSeriesAgeRating = series.ContentRating ?? string.Empty;
            SelectedSeriesTrailerUrl = series.TrailerUrl;
            SelectedSeriesNetworkLogoUrl = series.NetworkLogoUrl;

            // Initial basic metadata
            SelectedSeriesTotalEpisodesCount = series.Seasons.Sum(s => s.Episodes.Count);
            SelectedSeriesTotalSeasonsCount = series.SeasonCountSafe;
            SelectedSeriesYears = series.ReleaseYear?.ToString() ?? "";

            // Find last watched or first episode for "Continue" button
            var allEpisodes = series.Seasons
                .OrderBy(s => s.SeasonNumber)
                .SelectMany(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
                .ToList();
            
            // Find the last episode that was watched
            var lastWatched = allEpisodes.LastOrDefault(e => e.LastWatched.HasValue);
            
            if (lastWatched != null)
            {
                // If the last watched episode is not completed, suggest resuming it
                if (!lastWatched.IsCompleted)
                {
                    SelectedSeriesContinueEpisode = lastWatched;
                    SelectedSeriesContinueText = string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("Series.Continue.StartFromFormat"),
                        lastWatched.Season?.SeasonNumber ?? 1,
                        lastWatched.EpisodeNumber);
                }
                else
                {
                    // If completed, suggest the next one
                    var nextIndex = allEpisodes.IndexOf(lastWatched) + 1;
                    if (nextIndex < allEpisodes.Count)
                    {
                        var next = allEpisodes[nextIndex];
                        SelectedSeriesContinueEpisode = next;
                        SelectedSeriesContinueText = string.Format(CultureInfo.CurrentCulture,
                            _localizationService.GetString("Series.Continue.NextFormat"),
                            next.Season?.SeasonNumber ?? 1,
                            next.EpisodeNumber);
                    }
                    else
                    {
                        // All watched, suggest rewatching the last one
                        SelectedSeriesContinueEpisode = lastWatched;
                        SelectedSeriesContinueText = string.Format(CultureInfo.CurrentCulture,
                            _localizationService.GetString("Series.Continue.WatchAgainFormat"),
                            lastWatched.Season?.SeasonNumber ?? 1,
                            lastWatched.EpisodeNumber);
                    }
                }
            }
            else
            {
                // No history, start from the beginning
                var first = allEpisodes.FirstOrDefault();
                if (first != null)
                {
                    SelectedSeriesContinueEpisode = first;
                    SelectedSeriesContinueText = string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("Series.Continue.NextFormat"),
                        first.Season?.SeasonNumber ?? 1,
                        first.EpisodeNumber);
                }
            }

            // Set first season by default
            if (series.Seasons.Count > 0)
            {
                SelectedSeason = SelectedSeason == null
                    ? series.Seasons.FirstOrDefault()
                    : series.Seasons.FirstOrDefault(s => s.SeasonNumber == SelectedSeason.SeasonNumber)
                      ?? series.Seasons.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"Series metadata load failed: {ex.Message}");
        }
        finally
        {
            IsSelectedSeriesMetadataLoading = false;
        }

        return Task.CompletedTask;
    }

    private void EnsureSeriesEpisodes(Series series)
    {
        if (series.Seasons.Count > 0 && series.Seasons.Any(s => s.Episodes.Count > 0))
        {
            return;
        }

        var normalizedSeriesName = NormalizeSeriesTitleForMatching(series.Name);
        if (string.IsNullOrWhiteSpace(normalizedSeriesName))
        {
            return;
        }

        var candidates = Channels
            .Where(c => c.Type == ChannelType.Series)
            .Where(c =>
            {
                var normalizedChannelName = NormalizeSeriesTitleForMatching(c.Name);
                return normalizedChannelName.Equals(normalizedSeriesName, StringComparison.OrdinalIgnoreCase)
                    || normalizedChannelName.StartsWith(normalizedSeriesName + " ", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var seasons = new Dictionary<int, Season>();
        foreach (var channel in candidates)
        {
            var (seasonNumber, episodeNumber) = ParseEpisodeNumbers(channel.Name);
            if (!seasons.TryGetValue(seasonNumber, out var season))
            {
                season = new Season
                {
                    SeasonNumber = seasonNumber,
                    Name = $"Sezon {seasonNumber}",
                    CoverUrl = series.CoverUrl,
                    SeriesId = series.Id
                };
                seasons[seasonNumber] = season;
            }

            season.Episodes.Add(new Episode
            {
                EpisodeNumber = episodeNumber,
                Name = channel.Name,
                StreamUrl = channel.StreamUrl,
                CoverUrl = FirstNonEmpty(channel.LogoUrl, series.CoverUrl)
            });
        }

        series.Seasons = seasons
            .OrderBy(kvp => kvp.Key)
            .Select(kvp =>
            {
                kvp.Value.Episodes = kvp.Value.Episodes.OrderBy(ep => ep.EpisodeNumber).ToList();
                return kvp.Value;
            })
            .ToList();

        NormalizeSeriesDetailForDisplay(series);
    }


    private async Task<Series> LoadSeriesWithProfileProgressAsync(Series series)
    {
        if (SelectedPlaylist == null)
        {
            return series;
        }

        using var db = await _contextFactory.CreateDbContextAsync();

        var dbSeries = await db.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            // Use tracking to save Tmdb changes back to the DB immediately if lazy load occurs
            .FirstOrDefaultAsync(s =>
                s.PlaylistId == SelectedPlaylist.Id &&
                (s.Id == series.Id || s.Name == series.Name));

        var source = dbSeries ?? series;
        var providerOnlyFieldsCleared = PrepareProviderOnlySeriesMetadata(source);

        // ── YENİ: Xtream veya Stalker serisi ve hiç bölüm yoksa → lazy load ──
        if (ShouldLazyLoadProviderSeriesEpisodes(source))
        {
            await LazyLoadProviderSeriesEpisodesAsync(source, db);
            NormalizeSeriesDetailForDisplay(source);

            // --- UI SENKRONİZASYONU ---
            // Eğer veritabanından farklı bir instance (source != series) yüklendiyse, 
            // ana listedeki (cache) nesnenin de güncellenmesi için verileri kopyala.
            SyncSeriesDetailState(series, source);
        }

        // --- LAZY LOAD TMDB METADATA (Seasons & Episodes) ---
        // Sadece M3U profillerinde TMDB API kullan; Xtream/Stalker zaten verilerle geliyor.
        // Koşul: (TmdbId biliniyorsa VE MetadataFetchedAt boşsa) VEYA (hiç TMDB araması yapılmadıysa)
        bool needsTmdbFetch = IsM3UProfile() &&
                              source.MetadataFetchedAt == null &&
                              (source.TmdbId.HasValue || source.LastTmdbSync == null);

        if (needsTmdbFetch)
        {
            try
            {
                var languageCode = SeriesInfoParser.ExtractLanguageCode(source.GroupTitle ?? source.Genre ?? source.Name);

                // --- Adım 0: TmdbId bilinmiyorsa önce arama yap ---
                if (!source.TmdbId.HasValue)
                {
                    var cleanName = SeriesInfoParser.CleanSeriesName(source.Name);
                    var searchMeta = await _metadataService.SearchSeriesAsync(cleanName, languageCode);

                    // Ağ hatası durumunda ikinci deneme farklı dille
                    if (searchMeta?.TmdbId == null && !languageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                        searchMeta = await _metadataService.SearchSeriesAsync(cleanName, "en-US");

                    if (searchMeta?.TmdbId != null)
                    {
                        source.TmdbId = searchMeta.TmdbId;
                        if (!string.IsNullOrEmpty(searchMeta.PosterUrl)) source.CoverUrl = searchMeta.PosterUrl;
                        if (!string.IsNullOrEmpty(searchMeta.Description)) source.Plot = searchMeta.Description;
                        if (searchMeta.Rating.HasValue) source.Rating = searchMeta.Rating;
                        if (searchMeta.ReleaseYear.HasValue) source.ReleaseYear = searchMeta.ReleaseYear;

                        // DB'ye kaydet, UI zaten ObservableProperty ile güncellendi
                        if (dbSeries != null)
                        {
                            dbSeries.TmdbId = source.TmdbId;
                            dbSeries.CoverUrl = source.CoverUrl;
                            dbSeries.Plot = source.Plot;
                            dbSeries.Rating = source.Rating;
                            dbSeries.ReleaseYear = source.ReleaseYear;
                        }
                    }
                    source.LastTmdbSync = DateTime.UtcNow;
                    if (dbSeries != null) dbSeries.LastTmdbSync = source.LastTmdbSync;
                }

                // --- Adım 1: TmdbId varsa tam dizi detayını çek ---
                TmdbDetail? tmdbSeries = null;
                if (source.TmdbId.HasValue)
                {
                    tmdbSeries = await _metadataService.FetchSeriesDetailsAsync(source.TmdbId.Value, languageCode);
                    if (tmdbSeries != null)
                    {
                        if (string.IsNullOrEmpty(source.Cast) && tmdbSeries.Credits?.Cast != null)
                            source.Cast = string.Join(", ", tmdbSeries.Credits.Cast.OrderBy(c => c.Order).Take(5).Select(c => c.Name));
                        if (string.IsNullOrEmpty(source.Director))
                            source.Director = tmdbSeries.DirectorName;
                        if (string.IsNullOrEmpty(source.Plot) && !string.IsNullOrEmpty(tmdbSeries.Overview))
                            source.Plot = tmdbSeries.Overview;
                        if (!string.IsNullOrEmpty(tmdbSeries.PosterPath))
                            source.CoverUrl = $"https://image.tmdb.org/t/p/w500{tmdbSeries.PosterPath}";
                        if (string.IsNullOrEmpty(source.BackdropUrl) && !string.IsNullOrEmpty(tmdbSeries.BackdropPath))
                            source.BackdropUrl = $"https://image.tmdb.org/t/p/original{tmdbSeries.BackdropPath}";
                        if (tmdbSeries.VoteAverage > 0 && (source.Rating == null || source.Rating == 0))
                            source.Rating = tmdbSeries.VoteAverage;
                        if (string.IsNullOrEmpty(source.Genre) && tmdbSeries.Genres?.Count > 0)
                            source.Genre = string.Join(", ", tmdbSeries.Genres.Select(g => g.Name));

                        var tempMeta = new ChannelMetadata();
                        _metadataService.ApplyHeuristics(tmdbSeries, tempMeta, languageCode, source.GroupTitle ?? source.Name);
                        source.ContentRating = tempMeta.ContentRating;
                        source.NetworkName = tempMeta.NetworkName;
                        source.NetworkLogoUrl = tempMeta.NetworkLogoUrl;

                        if (string.IsNullOrEmpty(source.TrailerUrl))
                        {
                            var trailer = tmdbSeries.Videos?.Results?
                                .Where(v => v.Site == "YouTube" && (v.Type == "Trailer" || v.Type == "Teaser"))
                                .OrderByDescending(v => v.Official)
                                .ThenByDescending(v => v.Type == "Trailer")
                                .FirstOrDefault();
                            if (trailer != null && !string.IsNullOrEmpty(trailer.Key))
                                source.TrailerUrl = $"https://www.youtube.com/watch?v={trailer.Key}";
                        }

                        // Anında UI güncellemesi — source != series ise kopyala
                        if (!ReferenceEquals(source, series))
                            SyncSeriesDetailState(series, source);
                    }

                    // --- Adım 2: Sezonları paralel olarak çek (N+1 yerine paralel) ---
                    var seasonsToFetch = source.Seasons.Where(s => s.SeasonNumber > 0).ToList();
                    if (seasonsToFetch.Count > 0)
                    {
                        var seasonTasks = seasonsToFetch.Select(season =>
                            _metadataService.FetchSeasonDetailsAsync(source.TmdbId.Value, season.SeasonNumber, languageCode)
                                .ContinueWith(t => (season, tmdbSeason: t.Result), TaskContinuationOptions.ExecuteSynchronously)
                        );

                        var seasonResults = await Task.WhenAll(seasonTasks);

                        foreach (var (season, tmdbSeason) in seasonResults)
                        {
                            if (tmdbSeason == null) continue;

                            season.TmdbSeasonId = tmdbSeason.Id;
                            if (!string.IsNullOrEmpty(tmdbSeason.PosterPath))
                                season.CoverUrl = $"https://image.tmdb.org/t/p/w500{tmdbSeason.PosterPath}";
                            if (string.IsNullOrEmpty(season.Plot))
                                season.Plot = tmdbSeason.Overview;

                            foreach (var episode in season.Episodes)
                            {
                                var tmdbEp = tmdbSeason.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode.EpisodeNumber);
                                if (tmdbEp == null) continue;

                                if (!string.IsNullOrEmpty(tmdbEp.StillPath))
                                    episode.CoverUrl = $"https://image.tmdb.org/t/p/w500{tmdbEp.StillPath}";
                                if (string.IsNullOrEmpty(episode.Plot))
                                    episode.Plot = tmdbEp.Overview;
                                if (string.IsNullOrEmpty(episode.TmdbEpisodeName) && !string.IsNullOrEmpty(tmdbEp.Name))
                                    episode.TmdbEpisodeName = tmdbEp.Name;
                                if (tmdbEp.Runtime.HasValue && tmdbEp.Runtime.Value > 0 && !episode.Duration.HasValue)
                                    episode.Duration = TimeSpan.FromMinutes(tmdbEp.Runtime.Value);
                                if (!string.IsNullOrEmpty(tmdbEp.AirDate) && !episode.AirDate.HasValue)
                                    if (DateTime.TryParse(tmdbEp.AirDate, out var airDate))
                                        episode.AirDate = airDate;
                            }
                        }

                        // Sezon verileri yüklendi — UI'yi anında güncelle (Seasons setter PropertyChanged fırlatır)
                        NormalizeSeriesDetailForDisplay(source);
                        if (!ReferenceEquals(source, series))
                            SyncSeriesDetailState(series, source);
                    }
                }

                // --- Adım 3: Tamamlandı olarak işaretle ve kaydet ---
                if (source.TmdbId.HasValue && dbSeries != null)
                {
                    source.MetadataFetchedAt = DateTime.UtcNow;
                    dbSeries.Cast = source.Cast;
                    dbSeries.Director = source.Director;
                    dbSeries.Plot = source.Plot;
                    dbSeries.CoverUrl = source.CoverUrl;
                    dbSeries.BackdropUrl = source.BackdropUrl;
                    dbSeries.Genre = source.Genre;
                    dbSeries.ContentRating = source.ContentRating;
                    dbSeries.NetworkName = source.NetworkName;
                    dbSeries.NetworkLogoUrl = source.NetworkLogoUrl;
                    dbSeries.TrailerUrl = source.TrailerUrl;
                    dbSeries.Rating = source.Rating;
                    dbSeries.TmdbId = source.TmdbId;
                    dbSeries.LastTmdbSync = DateTime.UtcNow;
                    dbSeries.MetadataFetchedAt = source.MetadataFetchedAt;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lazy load TMDB fetch failed for Series {Name}", source.Name);
            }
        }
        // ----------------------------------------------------

        if (providerOnlyFieldsCleared && dbSeries != null)
        {
            await db.SaveChangesAsync();
        }

        await ApplyProfileProgressAsync(source, db);
        NormalizeSeriesDetailForDisplay(source);

        // Son kez UI senkronizasyonu — tüm değişiklikler yansısın
        if (!ReferenceEquals(source, series))
            SyncSeriesDetailState(series, source);

        return source;
    }

    private bool ShouldUseProviderOnlySeriesMetadata()
        => IsCurrentProviderType(ProfileType.XtreamCodes) || IsCurrentProviderType(ProfileType.StalkerPortal);

    private bool IsCurrentProviderType(ProfileType profileType)
        => CurrentProfile?.ProviderAccount?.Type == profileType;

    private bool IsM3UProfile()
        => IsCurrentProviderType(ProfileType.M3U);

    private bool PrepareProviderOnlySeriesMetadata(Series series)
        => ShouldUseProviderOnlySeriesMetadata() && ClearTmdbSeasonAndEpisodeMetadata(series);

    private bool ShouldLazyLoadProviderSeriesEpisodes(Series series)
        => series.Seasons.All(s => s.Episodes.Count == 0) &&
           (IsCurrentProviderType(ProfileType.XtreamCodes) || IsCurrentProviderType(ProfileType.StalkerPortal));

    private async Task LazyLoadProviderSeriesEpisodesAsync(Series series, AppDbContext db)
    {
        Debug.WriteLine($"[SelectMedia] Series '{series.Name}' has no episodes. Attempting lazy load for {CurrentProfile?.ProviderAccount?.Type}");

        if (IsCurrentProviderType(ProfileType.XtreamCodes))
        {
            await TryLazyLoadXtreamEpisodesAsync(series, db);
            return;
        }

        if (IsCurrentProviderType(ProfileType.StalkerPortal))
        {
            await TryLazyLoadStalkerEpisodesAsync(series, db);
        }
    }

    private static void SyncSeriesDetailState(Series target, Series source)
    {
        if (ReferenceEquals(target, source))
        {
            return;
        }

        target.CoverUrl = source.CoverUrl;
        target.Plot = source.Plot;
        target.Genre = source.Genre;
        target.Cast = source.Cast;
        target.Director = source.Director;
        target.ReleaseYear = source.ReleaseYear;
        target.Rating = source.Rating;
        target.ContentRating = source.ContentRating;
        target.TmdbId = source.TmdbId;
        target.TmdbTitle = source.TmdbTitle;
        target.BackdropUrl = source.BackdropUrl;
        target.TrailerUrl = source.TrailerUrl;
        target.NetworkName = source.NetworkName;
        target.NetworkLogoUrl = source.NetworkLogoUrl;
        target.Seasons = source.Seasons;
        target.MetadataFetchedAt = source.MetadataFetchedAt;
        target.LastTmdbSync = source.LastTmdbSync;
        NormalizeSeriesDetailForDisplay(target);
    }

    private static void NormalizeSeriesDetailForDisplay(Series? series)
    {
        if (series?.Seasons == null)
        {
            return;
        }

        foreach (var season in series.Seasons)
        {
            if (string.IsNullOrWhiteSpace(season.CoverUrl))
            {
                season.CoverUrl = series.CoverUrl;
            }

            season.Episodes = season.Episodes
                .OrderBy(e => e.EpisodeNumber <= 0 ? int.MaxValue : e.EpisodeNumber)
                .ThenBy(e => e.Name)
                .Select(e =>
                {
                    if (string.IsNullOrWhiteSpace(e.CoverUrl))
                    {
                        e.CoverUrl = FirstNonEmpty(season.CoverUrl, series.CoverUrl);
                    }

                    return e;
                })
                .ToList();
        }

        series.Seasons = series.Seasons
            .Where(s => s.Episodes.Count > 0 || s.SeasonNumber > 0)
            .OrderBy(s => s.SeasonNumber <= 0 ? int.MaxValue : s.SeasonNumber)
            .ThenBy(s => s.Name)
            .ToList();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void ApplyStalkerSeriesMetadata(Series series, StalkerSeriesInfo detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.CoverUrl))
            series.CoverUrl = detail.CoverUrl;
        if (!string.IsNullOrWhiteSpace(detail.Description) && string.IsNullOrWhiteSpace(series.Plot))
            series.Plot = detail.Description;
        if (!string.IsNullOrWhiteSpace(detail.GenresStr) && string.IsNullOrWhiteSpace(series.Genre))
            series.Genre = detail.GenresStr;
        if (!string.IsNullOrWhiteSpace(detail.Actors) && string.IsNullOrWhiteSpace(series.Cast))
            series.Cast = detail.Actors;
        if (!string.IsNullOrWhiteSpace(detail.Director) && string.IsNullOrWhiteSpace(series.Director))
            series.Director = detail.Director;
        if (!string.IsNullOrWhiteSpace(detail.Year) && int.TryParse(detail.Year.Split('-').FirstOrDefault(), out var year))
            series.ReleaseYear = year;
        if (!string.IsNullOrWhiteSpace(detail.Age))
            series.ContentRating = detail.Age;
        if (!string.IsNullOrWhiteSpace(detail.RatingImdb) && double.TryParse(detail.RatingImdb, out var rate))
            series.Rating = rate;
        if (!string.IsNullOrWhiteSpace(detail.TmdbId) && int.TryParse(detail.TmdbId, out var tmdb))
            series.TmdbId = tmdb;
    }

    private static void ApplyXtreamSeriesMetadata(Series series, XtreamSeriesDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.Name))
        {
            series.Name = System.Net.WebUtility.UrlDecode(detail.Name).Trim();
        }
        if (!string.IsNullOrWhiteSpace(detail.Cover))
            series.CoverUrl = detail.Cover;
        if (!string.IsNullOrWhiteSpace(detail.Plot) && string.IsNullOrWhiteSpace(series.Plot))
            series.Plot = detail.Plot;
        if (!string.IsNullOrWhiteSpace(detail.Genre) && string.IsNullOrWhiteSpace(series.Genre))
            series.Genre = detail.Genre;
        if (!string.IsNullOrWhiteSpace(detail.Cast) && string.IsNullOrWhiteSpace(series.Cast))
            series.Cast = detail.Cast;
    }

    private static bool ClearTmdbSeasonAndEpisodeMetadata(Series series)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(series.TmdbTitle))
        {
            series.TmdbTitle = null;
            changed = true;
        }

        if (series.MetadataFetchedAt != null)
        {
            series.MetadataFetchedAt = null;
            changed = true;
        }

        if (series.LastTmdbSync != null)
        {
            series.LastTmdbSync = null;
            changed = true;
        }

        foreach (var season in series.Seasons)
        {
            if (season.TmdbSeasonId != null)
            {
                season.TmdbSeasonId = null;
                changed = true;
            }

            foreach (var episode in season.Episodes)
            {
                if (!string.IsNullOrWhiteSpace(episode.TmdbEpisodeName))
                {
                    episode.TmdbEpisodeName = null;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private async Task TryLazyLoadStalkerEpisodesAsync(Series series, AppDbContext db)
    {
        if (!IsCurrentProviderType(ProfileType.StalkerPortal)) return;

        // Bu diziye ait Channel kaydını bul — StreamUrl'de series_id var
        var seriesChannel = await db.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.PlaylistId == series.PlaylistId &&
                c.Type == ChannelType.Series &&
                c.StreamUrl.StartsWith("stalker-series://") &&
                c.Name == series.Name);

        if (seriesChannel == null)
        {
            // İsim eşleşmesi yoksa normalized key ile dene
            var allSeriesChannels = await db.Channels
                .AsNoTracking()
                .Where(c => c.PlaylistId == series.PlaylistId &&
                            c.Type == ChannelType.Series &&
                            c.StreamUrl.StartsWith("stalker-series://"))
                .ToListAsync();

            var targetKey = SeriesInfoParser.NormalizeKey(series.Name);
            seriesChannel = allSeriesChannels.FirstOrDefault(c =>
                SeriesInfoParser.NormalizeKey(c.Name) == targetKey);
        }

        if (seriesChannel == null) return;

        var idStr = seriesChannel.StreamUrl.Replace("stalker-series://", "");
        if (string.IsNullOrWhiteSpace(idStr)) return;

        var providerAccount = CurrentProfile?.ProviderAccount;
        if (providerAccount == null) return;

        var portalUrl = providerAccount.Url;
        var macAddress = providerAccount.Username ?? string.Empty;

        _logger?.LogDebug("[Stalker] Lazy loading episodes for series {Name} (id={Id})", series.Name, idStr);

        var detail = await _stalkerPortalService.GetSeriesInfoAsync(
            portalUrl, macAddress, idStr);

        if (detail == null) return;

        // Poster/metadata/name güncelle (Kullanıcı İsteği: Stalker API'den gelen zengin TMDB metadatasını kaydet)
        ApplyStalkerSeriesMetadata(series, detail);

        // Seasons ve Episodes'ları oluştur
        series.Seasons.Clear();

        int defaultSeasonNum = 1;
        foreach (var stalkerSeason in detail.Seasons)
        {
            int seasonNum = defaultSeasonNum;
            // "Season 1" vs içinden rakamı ayıkla
            var match = System.Text.RegularExpressions.Regex.Match(stalkerSeason.Name, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var parsedNum))
            {
                seasonNum = parsedNum;
            }

            var season = new Season
            {
                SeasonNumber = seasonNum,
                Name = string.IsNullOrWhiteSpace(stalkerSeason.Name) ? $"Sezon {seasonNum}" : stalkerSeason.Name,
                CoverUrl = series.CoverUrl,
                Series = series
            };
            series.Seasons.Add(season);

            foreach (var stalkerEp in stalkerSeason.Episodes)
            {
                var epNum = stalkerEp.EpisodeNumber;
                var episodeCmd = FirstNonEmpty(stalkerEp.Cmd, stalkerSeason.Cmd);
                if (string.IsNullOrWhiteSpace(episodeCmd))
                {
                    _logger?.LogWarning("[Stalker] Episode {Episode} in season {Name} has no cmd, skipping", epNum, stalkerSeason.Name);
                    continue;
                }

                // PlayChannelAsync intercept etmesi için stalker-series-ep://episode?cmd={cmd}&ep={epNum} formatında özel link
                // Cmd içinde '/' gibi karakterler olabildiği için query param olarak taşımak daha güvenli (Uri host kısmında hata veriyor)
                var encodedCmd = System.Net.WebUtility.UrlEncode(episodeCmd);
                var interceptUrl = $"stalker-series-ep://episode?cmd={encodedCmd}&ep={epNum}";

                if (season.Episodes.Any(e => string.Equals(e.StreamUrl, interceptUrl, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var epDescription = stalkerEp.Description;
                var epDuration = stalkerEp.Duration;
                var epCover = stalkerEp.Pic;
                var epAdded = stalkerEp.Added;

                var duration = ParseProviderDuration(epDuration);
                var airDate = ParseProviderDate(epAdded);

                season.Episodes.Add(new Episode
                {
                    EpisodeNumber = epNum,
                    Name = string.IsNullOrWhiteSpace(stalkerEp.Name) ? $"Bölüm {epNum}" : stalkerEp.Name,
                    StreamUrl = interceptUrl,
                    Plot = epDescription,
                    Duration = duration,
                    CoverUrl = FirstNonEmpty(epCover, season.CoverUrl, series.CoverUrl),
                    AirDate = airDate,
                    Season = season
                });
            }

            defaultSeasonNum++;
        }

        NormalizeSeriesDetailForDisplay(series);

        // DB'ye kaydet
        if (series.Id > 0)
        {
            try
            {
                // Mevcut seriyi de güncelle (CoverUrl, Plot vb. için)
                db.Series.Update(series);
                await db.SaveChangesAsync();
                _logger?.LogDebug("[Stalker] Series metadata & episodes saved to DB.");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[Stalker] DB Save failed for lazy loaded series: {ex.Message}");
            }
        }
    }

    private async Task TryLazyLoadXtreamEpisodesAsync(Series series, AppDbContext db)
    {
        if (!IsCurrentProviderType(ProfileType.XtreamCodes)) return;

        // Bu diziye ait Channel kaydını bul — StreamUrl'de series_id var
        System.Diagnostics.Debug.WriteLine($"[Xtream-LazyLoad] Searching channel for series: '{series.Name}' (PlaylistId: {series.PlaylistId})");

        var seriesChannel = await db.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.PlaylistId == series.PlaylistId &&
                c.Type == ChannelType.Series &&
                c.StreamUrl.StartsWith("xtream-series://") &&
                c.Name == series.Name);

        if (seriesChannel == null)
        {
            // İsim eşleşmesi yoksa normalized key ile dene
            var allSeriesChannels = await db.Channels
                .AsNoTracking()
                .Where(c => c.PlaylistId == series.PlaylistId &&
                            c.Type == ChannelType.Series &&
                            c.StreamUrl.StartsWith("xtream-series://"))
                .ToListAsync();

            
            var targetKey = SeriesInfoParser.NormalizeKey(series.Name);
            seriesChannel = allSeriesChannels.FirstOrDefault(c =>
                SeriesInfoParser.NormalizeKey(c.Name) == targetKey);
        }

        if (seriesChannel == null)
        {
             return;
        }


        var idStr = seriesChannel.StreamUrl.Replace("xtream-series://", "");
        if (!long.TryParse(idStr, out var xtreamSeriesId)) return;

        var providerAccount = CurrentProfile?.ProviderAccount;
        if (providerAccount == null) return;

        var baseUrl = providerAccount.Url.TrimEnd('/');
        if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
        var username = providerAccount.Username ?? string.Empty;
        var password = _securityService.Decrypt(providerAccount.Password) ?? string.Empty;

        _logger?.LogDebug("[Xtream] Lazy loading episodes for series {Name} (id={Id})", series.Name, xtreamSeriesId);

        var detail = await _xtreamCodesService.GetSeriesInfoAsync(
            baseUrl, username, password, xtreamSeriesId);

        if (detail == null)
        {
            return;
        }

        // Poster/metadata/name güncelle
        ApplyXtreamSeriesMetadata(series, detail);

        // Authoritative data geldi — mevcut tahminleri/fallbakleri temizle
        series.Seasons.Clear();

        // Season + Episode'ları oluştur
        var streamBase = $"{baseUrl}/series/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}";

        // Seasons haritası — season_number → Season
        var seasonMap = detail.Seasons
            .Where(s => s.SeasonNumber >= 0) // Season 0 is often used for Specials
            .ToDictionary(s => s.SeasonNumber);

        foreach (var kvp in detail.Episodes.OrderBy(k => k.Key))
        {
            if (!int.TryParse(kvp.Key, out var seasonNum)) continue;

            // DB'de bu sezon var mı?
            var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);
            if (season == null)
            {
                season = new Season
                {
                    SeasonNumber = seasonNum,
                    Name = seasonMap.TryGetValue(seasonNum, out var sd) ? sd.Name : $"Sezon {seasonNum}",
                    CoverUrl = seasonMap.TryGetValue(seasonNum, out var sd2) ? sd2.Cover : null,
                    Series = series
                };
                series.Seasons.Add(season);
            }

            foreach (var ep in kvp.Value.OrderBy(e => e.EpisodeNum))
            {
                if (ep.Id <= 0) continue;

                var streamUrl = $"{streamBase}/{ep.Id}.{ep.ContainerExtension ?? "mp4"}";

                // Duplicate kontrolü
                if (season.Episodes.Any(e =>
                    string.Equals(e.StreamUrl, streamUrl, StringComparison.OrdinalIgnoreCase)))
                    continue;

                TimeSpan? duration = ep.DurationSecs.HasValue && ep.DurationSecs.Value > 0
                    ? TimeSpan.FromSeconds(ep.DurationSecs.Value)
                    : null;

        DateTime? airDate = null;
        if (!string.IsNullOrWhiteSpace(ep.AirDate))
        {
            if (DateTime.TryParse(ep.AirDate, out var ad))
            {
                airDate = ad;
            }
        }

                season.Episodes.Add(new Episode
                {
                    EpisodeNumber = ep.EpisodeNum,
                    Name = ep.Title ?? $"Bölüm {ep.EpisodeNum}",
                    StreamUrl = streamUrl,
                    CoverUrl = FirstNonEmpty(ep.CoverUrl, season.CoverUrl, series.CoverUrl),
                    Plot = ep.Plot,
                    Duration = duration,
                    AirDate = airDate,
                    SeasonId = season.Id
                });
            }
        }

        NormalizeSeriesDetailForDisplay(series);

        // DB'ye kaydet (series.Id > 0 ise tracking ile güncellenir)
        if (series.Id > 0)
        {
            try
            {
                // Seasons ve Episodes DB'de yeni — ekle
                foreach (var season in series.Seasons.Where(s => s.Id == 0))
                {
                    season.SeriesId = series.Id;
                    db.Seasons.Add(season);
                }

                // SERİ meta verisini de güncelle (CoverUrl, Plot vb.)
                db.Series.Update(series);
                
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Xtream] Failed to persist lazy-loaded episodes for {Name}", series.Name);
            }
        }
    }


    private static TimeSpan? ParseProviderDuration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();

        if (TimeSpan.TryParse(value, out var parsed) && parsed > TimeSpan.Zero)
        {
            return parsed;
        }

        if (double.TryParse(value, out var minutesOnly) && minutesOnly > 0)
        {
            return TimeSpan.FromMinutes(minutesOnly);
        }

        var parts = Regex.Matches(value, @"\d+").Select(m => m.Value).ToList();
        if (parts.Count >= 3 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out var minutes) &&
            int.TryParse(parts[2], out var seconds))
        {
            var hhmmss = new TimeSpan(hours, minutes, seconds);
            if (hhmmss > TimeSpan.Zero)
            {
                return hhmmss;
            }
        }

        if (parts.Count >= 1 && int.TryParse(parts[0], out var firstNumber) && firstNumber > 0)
        {
            return TimeSpan.FromMinutes(firstNumber);
        }

        return null;
    }

    private static DateTime? ParseProviderDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        if (long.TryParse(raw, out var unixSeconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private async Task ApplyProfileProgressAsync(Series series, AppDbContext db)
    {
        var episodes = series.Seasons.SelectMany(s => s.Episodes).ToList();
        if (episodes.Count == 0)
        {
            return;
        }

        foreach (var episode in episodes)
        {
            episode.IsCompleted = false;
        }

        if (!CurrentProfileId.HasValue)
        {
            return;
        }

        var profileId = CurrentProfileId.Value;
        var episodeIds = episodes
            .Where(e => e.Id > 0)
            .Select(e => e.Id)
            .Distinct()
            .ToList();
        var historyByEpisodeId = new Dictionary<int, WatchHistory>();
        if (episodeIds.Count > 0)
        {
            var latestEpisodeHistories = await db.WatchHistories
                .AsNoTracking()
                .Where(h => h.ProfileId == profileId && h.EpisodeId.HasValue && episodeIds.Contains(h.EpisodeId.Value))
                .GroupBy(h => h.EpisodeId!.Value)
                .Select(g => g.OrderByDescending(x => x.WatchedAt).First())
                .ToListAsync();

            historyByEpisodeId = latestEpisodeHistories.ToDictionary(h => h.EpisodeId!.Value);
        }

        var seriesKey = SeriesProgressIdentity.NormalizeSeriesKey(series.Name);
        var episodeProgressByKey = new Dictionary<string, SeriesProgressSnapshot>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(seriesKey))
        {
            var seasonNumbers = series.Seasons
                .Select(s => Math.Max(1, s.SeasonNumber))
                .Distinct()
                .ToList();

            var persistedProgress = await db.SeriesEpisodeProgresses
                .AsNoTracking()
                .Where(p =>
                    p.ProfileId == profileId &&
                    p.SeriesKey == seriesKey &&
                    seasonNumbers.Contains(p.SeasonNumber))
                .ToListAsync();

            foreach (var item in persistedProgress)
            {
                var key = SeriesProgressIdentity.BuildEpisodeKey(item.SeasonNumber, item.EpisodeNumber);
                episodeProgressByKey[key] = new SeriesProgressSnapshot(
                    item.LastWatchedAt,
                    item.StoppedAt,
                    item.Duration,
                    item.Completed);
            }

            if (episodeProgressByKey.Count == 0)
            {
                var legacySnapshots = await LoadLegacySeriesProgressSnapshotsAsync(db, profileId, seriesKey);
                if (legacySnapshots.Count > 0)
                {
                    episodeProgressByKey = legacySnapshots;
                    await PersistSeriesProgressSnapshotsAsync(db, profileId, series.Name, seriesKey, legacySnapshots);
                }
            }
        }

        foreach (var episode in episodes)
        {
            if (episode.Id > 0 && historyByEpisodeId.TryGetValue(episode.Id, out var history))
            {
                episode.LastWatched = history.WatchedAt;
                episode.WatchedPosition = history.StoppedAt;
                episode.IsCompleted = history.Completed;
                continue;
            }

            var (seasonNumber, episodeNumber) = SeriesProgressIdentity.ResolveSeasonEpisode(episode);
            var key = SeriesProgressIdentity.BuildEpisodeKey(seasonNumber, episodeNumber);
            if (!episodeProgressByKey.TryGetValue(key, out var snapshot))
            {
                continue;
            }

            episode.LastWatched = snapshot.LastWatchedAt;
            episode.WatchedPosition = snapshot.StoppedAt;
            episode.IsCompleted = snapshot.Completed;
            if (snapshot.Duration.HasValue && snapshot.Duration.Value.TotalSeconds > 0)
            {
                episode.Duration = snapshot.Duration;
            }
        }
    }

    private async Task<Dictionary<string, SeriesProgressSnapshot>> LoadLegacySeriesProgressSnapshotsAsync(
        AppDbContext db,
        int profileId,
        string seriesKey)
    {
        var rows = await db.WatchHistories
            .AsNoTracking()
            .Where(h => h.ProfileId == profileId && h.EpisodeId.HasValue)
            .Include(h => h.Episode)
            .ThenInclude(e => e!.Season)
            .ThenInclude(s => s!.Series)
            .OrderByDescending(h => h.WatchedAt)
            .ToListAsync();

        var snapshots = new Dictionary<string, SeriesProgressSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var episode = row.Episode;
            var rowSeriesName = episode?.Season?.Series?.Name;
            if (string.IsNullOrWhiteSpace(rowSeriesName))
            {
                continue;
            }

            var rowSeriesKey = SeriesProgressIdentity.NormalizeSeriesKey(rowSeriesName);
            if (!string.Equals(rowSeriesKey, seriesKey, StringComparison.Ordinal))
            {
                continue;
            }

            var (seasonNumber, episodeNumber) = SeriesProgressIdentity.ResolveSeasonEpisode(episode!);
            var key = SeriesProgressIdentity.BuildEpisodeKey(seasonNumber, episodeNumber);
            if (snapshots.ContainsKey(key))
            {
                continue;
            }

            snapshots[key] = new SeriesProgressSnapshot(
                row.WatchedAt,
                row.StoppedAt,
                episode?.Duration,
                row.Completed);
        }

        return snapshots;
    }

    private async Task PersistSeriesProgressSnapshotsAsync(
        AppDbContext db,
        int profileId,
        string seriesTitle,
        string seriesKey,
        IReadOnlyDictionary<string, SeriesProgressSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        var parsedEntries = new List<(int SeasonNumber, int EpisodeNumber, SeriesProgressSnapshot Snapshot)>();
        foreach (var entry in snapshots)
        {
            var token = entry.Key;
            if (string.IsNullOrWhiteSpace(token) || token.Length < 9)
            {
                continue;
            }

            if (!int.TryParse(token.AsSpan(1, 3), out var seasonNumber) ||
                !int.TryParse(token.AsSpan(5, 4), out var episodeNumber))
            {
                continue;
            }

            parsedEntries.Add((seasonNumber, episodeNumber, entry.Value));
        }

        if (parsedEntries.Count == 0)
        {
            return;
        }

        var existingKeys = await db.SeriesEpisodeProgresses
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.SeriesKey == seriesKey)
            .Select(p => new { p.SeasonNumber, p.EpisodeNumber })
            .ToListAsync();

        var existingKeySet = new HashSet<string>(
            existingKeys.Select(k => SeriesProgressIdentity.BuildEpisodeKey(k.SeasonNumber, k.EpisodeNumber)),
            StringComparer.OrdinalIgnoreCase);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var parsed in parsedEntries)
            {
                var key = SeriesProgressIdentity.BuildEpisodeKey(parsed.SeasonNumber, parsed.EpisodeNumber);
                if (!existingKeySet.Add(key))
                {
                    continue;
                }

                db.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
                {
                    ProfileId = profileId,
                    SeriesKey = seriesKey,
                    SeriesTitle = seriesTitle,
                    SeasonNumber = parsed.SeasonNumber,
                    EpisodeNumber = parsed.EpisodeNumber,
                    LastWatchedAt = parsed.Snapshot.LastWatchedAt,
                    StoppedAt = parsed.Snapshot.StoppedAt,
                    Duration = parsed.Snapshot.Duration,
                    Completed = parsed.Snapshot.Completed
                });
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger?.LogError(ex, "Failed to migrate legacy series progress for series key '{SeriesKey}'. The operation was safely rolled back.", seriesKey);
        }
    }

    private sealed record SeriesProgressSnapshot(
        DateTime LastWatchedAt,
        TimeSpan StoppedAt,
        TimeSpan? Duration,
        bool Completed);

    private Channel BuildSeriesEpisodeChannel(Episode episode, Series? series = null)
    {
        var matchedChannel = Channels.FirstOrDefault(c =>
            c.Type == ChannelType.Series &&
            !string.IsNullOrWhiteSpace(c.StreamUrl) &&
            string.Equals(c.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));

        if (matchedChannel != null)
        {
            // Update matched channel with latest episode progress
            matchedChannel.WatchedPosition = episode.WatchedPosition;
            matchedChannel.Duration = episode.Duration;
            matchedChannel.LastWatched = episode.LastWatched;
            matchedChannel.IsCompleted = episode.IsCompleted;
            
            if (series != null)
            {
                matchedChannel.GroupTitle = series.Name;
            }

            return matchedChannel;
        }

        return new Channel
        {
            Id = 0,
            Name = episode.Name,
            StreamUrl = episode.StreamUrl,
            LogoUrl = episode.CoverUrl,
            Type = ChannelType.Series,
            PlaylistId = SelectedPlaylist?.Id ?? 0,
            WatchedPosition = episode.WatchedPosition,
            Duration = episode.Duration,
            LastWatched = episode.LastWatched,
            IsCompleted = episode.IsCompleted,
            GroupTitle = series?.Name
        };
    }

    public bool TryPrepareEpisodePlaybackContext(Channel channel)
    {
        if (channel == null || channel.Type != ChannelType.Series || string.IsNullOrWhiteSpace(channel.StreamUrl))
        {
            CurrentEpisodePlaybackContext = null;
            NextEpisodePlaybackContext = null;
            CurrentSeriesPlaybackContext = null;
            return false;
        }

        if (CurrentEpisodePlaybackContext != null &&
            string.Equals(CurrentEpisodePlaybackContext.StreamUrl, channel.StreamUrl, StringComparison.OrdinalIgnoreCase))
        {
            if (NextEpisodePlaybackContext == null)
            {
                NextEpisodePlaybackContext = FindNextEpisode(CurrentEpisodePlaybackContext);
            }

            if (CurrentSeriesPlaybackContext == null)
            {
                CurrentSeriesPlaybackContext = ResolveSeriesForEpisode(CurrentEpisodePlaybackContext);
            }

            return true;
        }

        var resolvedEpisode = FindEpisodeByStreamUrl(channel.StreamUrl!);
        if (resolvedEpisode == null)
        {
            CurrentEpisodePlaybackContext = null;
            NextEpisodePlaybackContext = null;
            CurrentSeriesPlaybackContext = null;
            return false;
        }

        CurrentEpisodePlaybackContext = resolvedEpisode;
        CurrentSeriesPlaybackContext = ResolveSeriesForEpisode(resolvedEpisode);
        NextEpisodePlaybackContext = FindNextEpisode(resolvedEpisode);
        return true;
    }

    private Series? ResolveSeriesForEpisode(Episode episode)
    {
        if (episode == null)
        {
            return null;
        }

        if (SelectedSeries != null && SeriesContainsEpisode(SelectedSeries, episode))
        {
            return SelectedSeries;
        }

        return FindSeriesContainingEpisode(episode);
    }

    public void SyncEpisodeProgress(Episode episode)
    {
        if (episode == null)
        {
            return;
        }

        var candidates = new List<Series>();
        if (SelectedSeries != null)
        {
            candidates.Add(SelectedSeries);
        }

        if (CurrentSeriesPlaybackContext != null)
        {
            candidates.Add(CurrentSeriesPlaybackContext);
        }

        candidates.AddRange(SeriesViewItems);

        if (_allSeriesCache != null)
        {
            candidates.AddRange(_allSeriesCache);
        }

        var seenSeries = new HashSet<int>();
        var anyUpdated = false;

        foreach (var series in candidates)
        {
            if (series == null)
            {
                continue;
            }

            if (series.Id > 0 && !seenSeries.Add(series.Id))
            {
                continue;
            }

            foreach (var season in series.Seasons)
            {
                foreach (var item in season.Episodes)
                {
                    var isMatch =
                        (item.Id > 0 && episode.Id > 0 && item.Id == episode.Id) ||
                        (!string.IsNullOrWhiteSpace(item.StreamUrl) &&
                         string.Equals(item.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));

                    if (!isMatch)
                    {
                        continue;
                    }

                    item.LastWatched = episode.LastWatched;
                    item.WatchedPosition = episode.WatchedPosition;
                    item.Duration = episode.Duration;
                    item.IsCompleted = episode.IsCompleted;
                    anyUpdated = true;
                }
            }
        }

        if (anyUpdated)
        {
            OnPropertyChanged(nameof(SelectedSeries));
            OnPropertyChanged(nameof(SeriesViewItems));
            _ = UpdateHistoryBucketsAsync();
        }
    }

    private Episode? FindNextEpisode(Episode episode)
    {
        if (episode == null)
        {
            return null;
        }

        var series = SelectedSeries;
        if (series == null || !SeriesContainsEpisode(series, episode))
        {
            series = FindSeriesContainingEpisode(episode);
        }

        if (series == null)
        {
            return null;
        }

        var orderedEpisodes = series.Seasons
            .OrderBy(s => s.SeasonNumber)
            .SelectMany(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
            .ToList();

        var currentIndex = orderedEpisodes.FindIndex(e =>
            e.Id > 0 && episode.Id > 0
                ? e.Id == episode.Id
                : string.Equals(e.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0 || currentIndex + 1 >= orderedEpisodes.Count)
        {
            return null;
        }

        return orderedEpisodes[currentIndex + 1];
    }

    private Series? FindSeriesContainingEpisode(Episode episode)
    {
        var candidates = new List<Series>();
        if (SelectedSeries != null)
        {
            candidates.Add(SelectedSeries);
        }

        candidates.AddRange(SeriesViewItems);
        candidates.AddRange(_allSeriesCache);

        var seen = new HashSet<int>();
        foreach (var series in candidates)
        {
            if (series == null)
            {
                continue;
            }

            if (series.Id > 0 && !seen.Add(series.Id))
            {
                continue;
            }

            if (SeriesContainsEpisode(series, episode))
            {
                return series;
            }
        }

        return null;
    }

    private static bool SeriesContainsEpisode(Series series, Episode episode)
    {
        return series.Seasons
            .SelectMany(s => s.Episodes)
            .Any(e =>
                e.Id > 0 && episode.Id > 0
                    ? e.Id == episode.Id
                    : !string.IsNullOrWhiteSpace(e.StreamUrl) &&
                      string.Equals(e.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));
    }

    private Episode? FindEpisodeByStreamUrl(string streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return null;
        }

        var candidates = new List<Series>();
        if (SelectedSeries != null)
        {
            candidates.Add(SelectedSeries);
        }

        candidates.AddRange(SeriesViewItems);
        candidates.AddRange(_allSeriesCache);

        var seen = new HashSet<int>();
        foreach (var series in candidates)
        {
            if (series == null)
            {
                continue;
            }

            if (series.Id > 0 && !seen.Add(series.Id))
            {
                continue;
            }

            var match = series.Seasons
                .OrderBy(s => s.SeasonNumber)
                .SelectMany(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
                .FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e.StreamUrl) &&
                    string.Equals(e.StreamUrl, streamUrl, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task EnrichChannelsWithEpgAsync(IEnumerable<Channel> channels)
    {
        try
        {
            var liveChannels = channels.Where(c => c.Type == ChannelType.Live).ToList();
            if (liveChannels.Count == 0) return;

            var epgData = await _epgService.GetCurrentProgramsAsync(liveChannels);
            
            _dispatcherService.Invoke(() =>
            {
                foreach (var channel in liveChannels)
                {
                    if (epgData.TryGetValue(channel.Id, out var program) && program != null)
                    {
                        if (channel.CurrentProgramTitle != program.Title)
                            channel.CurrentProgramTitle = program.Title;
                        
                        if (channel.EpgProgress != program.ProgressPercentage)
                            channel.EpgProgress = program.ProgressPercentage;
                    }
                    else
                    {
                        if (channel.CurrentProgramTitle != null)
                            channel.CurrentProgramTitle = null;
                        
                        if (channel.EpgProgress != 0)
                            channel.EpgProgress = 0;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"EPG enrichment error: {ex.Message}");
        }
    }

    private void SetItems<T>(BatchObservableCollection<T> collection, IEnumerable<T> items, Action? onComplete = null)
    {
        if (items == null) return;
        var list = items.ToList();
        _dispatcherService.Invoke(() =>
        {
            collection.ReplaceAll(list);
            onComplete?.Invoke();
        });
    }

    [GeneratedRegex(@"(?:\b|_)(adult|xxx|porn|sexy|18\+| \+18|pink|redlight|erotik|erotic|lust|hentai|brazzers|bangbros|babes|realitykings|digitalplayground|naughtyamerica|passion|penthouse|hustler|playboy|blue movie|hardcore|softcore|x-rated|sex|cam|strip|fetish|bondage|bdsm|amateur|milf|gay|lesbian|pornstar|yetişkin|mature)(?:\b|_)", RegexOptions.IgnoreCase)]
    private static partial Regex AdultContentRegex();
}


