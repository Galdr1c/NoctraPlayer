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
    private const int IncrementalPageSize = 50;
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
    private readonly DateTime _downloadCenterSessionStartUtc = DateTime.UtcNow;
    private CancellationTokenSource? _slowLoadingWarnCts;

    [ObservableProperty]
    private AppView _activeView = AppView.Home;

    [ObservableProperty]
    private ObservableCollection<Channel> _trendingChannels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _continueWatching = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _latestMovies = new();

    [ObservableProperty]
    private ObservableCollection<Series> _latestSeries = new();

    private List<Series> _allSeriesCache = new();

    [ObservableProperty]
    private ObservableCollection<Series> _seriesViewItems = new();

    [ObservableProperty]
    private Channel? _featuredChannel;

    [ObservableProperty]
    private ObservableCollection<Playlist> _playlists = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _channels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _filteredChannels = new();

    [ObservableProperty]
    private ObservableCollection<string> _groups = new();

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
    private ObservableCollection<object> _searchResults = new();

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
    private string _statusMessage = "Hazır";

    [ObservableProperty]
    private bool _isChannelLoading;

    [ObservableProperty]
    private double _channelLoadingProgress;

    [ObservableProperty]
    private string _channelLoadingStats = string.Empty;

    [ObservableProperty]
    private ConnectionHealth _connectionQuality = ConnectionHealth.Good;

    [ObservableProperty]
    private string _connectionStatusText = "Bağlantı İyi";

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

    public IReadOnlyList<KeyValuePair<ChannelSortOrder, string>> SortOptions { get; } = new List<KeyValuePair<ChannelSortOrder, string>>
    {
        new(ChannelSortOrder.NewestFirst, "Son eklenen (Yeni > Eski)"),
        new(ChannelSortOrder.OldestFirst, "En eski (Eski > Yeni)"),
        new(ChannelSortOrder.NameAsc, "Alfabetik A -> Z"),
        new(ChannelSortOrder.NameDesc, "Alfabetik Z -> A")
    };

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
        ILogger<MainViewModel>? logger = null)
    {
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

        // When background aggregation completes, reload series data
        _mediaService.OnAggregationCompleted += (playlistId) =>
        {
            _dispatcherService.BeginInvoke(async () =>
            {
                if (SelectedPlaylist?.Id == playlistId)
                {
                    try
                    {
                        SetItems(LatestSeries, await _mediaService.GetSeriesAsync(playlistId));
                        UpdateSeriesViewItems();
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] Series refreshed after background aggregation for playlist {playlistId}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] Series refresh after aggregation failed: {ex.Message}");
                    }
                }
            });
        };
    }

    public Task InitializeAsync()
    {
        // Otomatik yükleme yerine profil yüklenmesini bekle
        return Task.CompletedTask;
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
            ConnectionStatusText = "Bağlantı Yavaş";
            LoadingWarningMessage = "⚠️ Bağlantı normalden uzun sürüyor...";

            await Task.Delay(12_000, ct);
            if (ct.IsCancellationRequested) return;

            ConnectionQuality = ConnectionHealth.Critical;
            ConnectionStatusText = "Bağlantı Sorunlu";
            LoadingWarningMessage = "⚠️ Sunucuya erişilemiyor olabilir. Playlist adresinizi kontrol edin.";
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
                    StatusMessage = $"⚠️ Playlist kaynağına erişilemiyor (HTTP {(int)response.StatusCode}). " +
                                    "URL değişmiş olabilir, profil ayarlarını kontrol edin.";
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[HealthCheck] Playlist URL erişim hatası: {Msg}", ex.Message);
            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = "⚠️ Playlist kaynağına ulaşılamıyor. İnternet bağlantınızı veya URL'yi kontrol edin.";
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

        // Clear UI state from previous profile
        ClearProfileState();

        IsLoading = true;
        IsChannelLoading = true;
        ChannelLoadingProgress = 0;
        ChannelLoadingStats = string.Empty;
        ConnectionQuality = ConnectionHealth.Good;
        ConnectionStatusText = "Bağlantı İyi";
        StatusMessage = "Kanal ve içerik listeleriniz hazırlanıyor...";
        CurrentProfileId = profile.Id;
        CurrentProfile = profile;
        
        try
        {
            // Ensure provider account is loaded
            if (profile.ProviderAccount == null)
            {
                StatusMessage = "Hesap bilgileri yüklenemedi";
                IsLoading = false;
                return;
            }

                        // Check if playlist already exists (cache-first approach)
                        var existingPlaylists = await _playlistService.GetAllAsync(profile.Id);
            
                        if (existingPlaylists.Count > 0)
                        {
                            // Use cached playlist - much faster!
                            _logger?.LogDebug($"[MainViewModel] Using cached playlist for profile {profile.Id}");
                            StatusMessage = "İçerikleriniz hızla yükleniyor...";
                            await LoadPlaylistsAsync();
            
                            // Arka planda URL sağlık kontrolü yap (cache varken bile)
                            var playlistUrl = existingPlaylists[0].Url;
                            if (!string.IsNullOrWhiteSpace(playlistUrl))
                            {
                                _ = CheckPlaylistUrlHealthAsync(playlistUrl);
                            }
            
                                            if (profile.ProviderAccount.Type == ProfileType.StalkerPortal)
                                            {
                                                _ = ResumeStalkerProgressiveLoadingAsync(profile, existingPlaylists[0]);
                                            }
                                            else if (profile.ProviderAccount.Type == ProfileType.XtreamCodes)
                                            {
                                                _ = ResumeXtreamProgressiveLoadingAsync(profile, existingPlaylists[0]);
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
                                                    StatusMessage = "Kanal listeniz arka planda yükleniyor...";
                                                    
                                                    _ = Task.Run(async () =>
                                                    {
                                                        try
                                                        {
                                                            await _playlistService.AddFromUrlAsync(profile.Name, m3uUrl, profile.Id);
                                                            _dispatcherService.BeginInvoke(async () =>
                                                            {
                                                                await LoadPlaylistsAsync();
                                                                StatusMessage = "M3U Listesi Hazır ✓";
                                                            });
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _dispatcherService.BeginInvoke(() => StatusMessage = UserFriendlyErrorMessage.WithPrefix("M3U yükleme hatası", ex));
                                                        }
                                                    });
                                                    break;
                                                }
                                                                    case ProfileType.XtreamCodes:
                                                                    {
                                                                        StatusMessage = "Sunucuyla bağlantı kuruluyor...";
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
                                                                    totalCats = categories.Count;
                                                                    _dispatcherService.BeginInvoke(() => 
                                                                    {
                                                                        ChannelLoadingStats = $"0 / {totalCats} kategori";
                                                                    });

                                                                    // Arayüzün anında dolması için kategori isimleriyle "sahte" kanallar ekle
                                                                    var dummyChannels = categories.Select(c => new Channel
                                                                    {
                                                                        Name = "İçerik yükleniyor...",
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
                                                                        ChannelLoadingStats = $"{loadedCats} / {totalCats} kategori • {groupName}";
                                                                    });

                                                                    if (SelectedPlaylist?.Id == playlist.Id && SelectedGroup == groupName)
                                                                    {
                                                                        _ = ThrottledLoadChannelsAsync(playlist.Id);
                                                                    }
                                                                    },
                                                                    cancellationToken: CancellationToken.None);

                                                                    _ = Task.Run(async () =>
                                                                    {
                                                                    try
                                                                    {
                                                                    await _mediaService.AggregateContentAsync(playlist.Id);
                                                                    _mediaService.RaiseAggregationCompleted(playlist.Id);
                                                                    }
                                                                    catch { }
                                                                    });

                                                                    _dispatcherService.BeginInvoke(() =>
                                                                    {
                                                                    StatusMessage = "Xtream içerikleri yüklendi ✓";
                                                                    IsChannelLoading = false;
                                                                    ChannelLoadingProgress = 100;
                                                                    });                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _logger?.LogDebug($"[Xtream] Error: {ex}");
                                                            _dispatcherService.BeginInvoke(() => IsChannelLoading = false);
                                                        }
                                                    });
                                                    break;
                                                }
                    case ProfileType.StalkerPortal:
                    {
                        StatusMessage = "Stalker Portal kategorileri yükleniyor...";
                        var portalUrl  = profile.ProviderAccount.Url;
                        var macAddress = profile.ProviderAccount.Username ?? string.Empty;
                        var sourceUrl  = $"{portalUrl.TrimEnd('/')}/stalker_portal#{macAddress}";
                        var epgUrl     = _stalkerPortalService.GetEpgUrl(portalUrl);

                        // ── Adım 1: Boş playlist oluştur — UI hemen açılabilir ──────────
                        // Kanallar geldikçe buraya eklenecek
                        var playlist = await _playlistService.CreateEmptyPlaylistAsync(
                            profile.Name, sourceUrl, profile.Id, epgUrl);
                        await LoadPlaylistsAsync();

                        StatusMessage = "İçerikler yükleniyor, bu biraz sürebilir...";

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
                                        _prioritizeStalkerCategoryAction = prioritizeAction;

                                        // Arayüzün anında dolması için kategori isimleriyle "sahte" kanallar ekle
                                        var dummyChannels = categories.Select(c => new Channel
                                        {
                                            Name = "İçerik yükleniyor...",
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

                                // Tüm içerik yüklendi
                                _dispatcherService.BeginInvoke(() =>
                                {
                                    StatusMessage = $"Tüm içerikler hazır ✓";
                                    IsChannelLoading = false;
                                    ChannelLoadingProgress = 100;

                                    // Dizi yapısını arka planda oluştur
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await _mediaService.AggregateContentAsync(playlist.Id);
                                            _mediaService.RaiseAggregationCompleted(playlist.Id);
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine(
                                                $"[Stalker] AggregateContent failed: {ex.Message}");
                                        }
                                    });
                                });
                            }
                            catch (Exception ex)
                            {
                                _dispatcherService.BeginInvoke(() =>
                                {
                                    StatusMessage = UserFriendlyErrorMessage.WithPrefix(
                                        "İçerik yükleme hatası", ex);
                                });
                            }
                        });

                        // ── Bu satıra kadar geçen süre: ~3-5 saniye ─────────────────────
                        // Kullanıcı artık uygulamayı kullanabilir
                        break;
                    }
                    default:
                        throw new NotSupportedException($"Desteklenmeyen profil tipi: {profile.ProviderAccount.Type}");
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Profil yuklenirken hata olustu", ex);
            _logger?.LogDebug($"LoadProfile Error: {ex}");
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
        FeaturedChannel = null;
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

        // Clear collections
        SetItems(Playlists, Enumerable.Empty<Playlist>());
        SetItems(Channels, Enumerable.Empty<Channel>());
        SetItems(FilteredChannels, Enumerable.Empty<Channel>());
        SetItems(TrendingChannels, Enumerable.Empty<Channel>());
        SetItems(ContinueWatching, Enumerable.Empty<Channel>());
        SetItems(LatestMovies, Enumerable.Empty<Channel>());
        SetItems(LatestSeries, Enumerable.Empty<Series>());
        SetItems(SeriesViewItems, Enumerable.Empty<Series>());
        SetItems(SearchResults, Enumerable.Empty<object>());
        SetItems(Groups, Enumerable.Empty<string>());
        
        // My List, Favorites, History
        SetItems(MyList, Enumerable.Empty<object>());
        SetItems(FavoriteChannels, Enumerable.Empty<object>());
        SetItems(HistoryChannels, Enumerable.Empty<Channel>());
        SetItems(HistoryLiveChannels, Enumerable.Empty<Channel>());
        SetItems(HistorySeriesChannels, Enumerable.Empty<Channel>());
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
                onCategoriesDiscovered: (categories, _) =>
                {
                    if (isFullRefresh) return Task.FromResult(categories);

                    var toLoad = categories.Where(c => pendingGroups.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                    return Task.FromResult(toLoad);
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await _mediaService.AggregateContentAsync(playlist.Id);
                    _mediaService.RaiseAggregationCompleted(playlist.Id);
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[Xtream] Resume error: {ex}");
        }
    }

    private async Task ResumeStalkerProgressiveLoadingAsync(Profile profile, Playlist playlist, bool isFullRefresh = false)
    {
        if (profile.ProviderAccount == null) return;

        try
        {
            List<string> pendingGroups = new();
            if (!isFullRefresh)
            {
                pendingGroups = await _playlistService.GetPendingDummyGroupsAsync(playlist.Id);
                if (pendingGroups.Count == 0)
                {
                    return; // Everything is loaded!
                }
            }

            _logger?.LogDebug($"[Stalker] {(isFullRefresh ? "Full Refresh" : "Resume background load")} for categories.");
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
                onCategoriesDiscovered: (categories, prioritizeAction) =>
                {
                    _prioritizeStalkerCategoryAction = prioritizeAction;

                    if (isFullRefresh) return Task.FromResult(categories);

                    // Yalnızca pendingCategories içinde olanları indirilecek listeye filtrele
                    var categoriesToDownload = categories
                        .Where(c => pendingGroups.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    return Task.FromResult(categoriesToDownload);
                },
                onCategoryLoaded: async (channels, category) =>
                {
                    await _playlistService.ReplaceDummyWithRealChannelsAsync(playlist.Id, category.Name, channels);

                    if (SelectedPlaylist?.Id == playlist.Id && SelectedGroup == category.Name)
                    {
                        _ = ThrottledLoadChannelsAsync(playlist.Id);
                    }
                },
                progress: progress,
                cancellationToken: CancellationToken.None);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _mediaService.AggregateContentAsync(playlist.Id);
                    _mediaService.RaiseAggregationCompleted(playlist.Id);
                }
                catch { }
            });

            _dispatcherService.BeginInvoke(() =>
            {
                StatusMessage = $"{(isFullRefresh ? "Yenileme tamamlandı" : "Tüm içerikler hazır")} ✓";
                IsChannelLoading = false;
                ChannelLoadingStats = string.Empty;
                ChannelLoadingProgress = 100;
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[Stalker] Failed to {(isFullRefresh ? "refresh" : "resume")} load: {ex}");
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
            StatusMessage = "Kanal ve kategori düzeni optimize ediliyor...";
            
            // Single-pass query: Fetch all groups and total count at once (Significantly faster)
            var meta = await _playlistService.GetChannelGroupMetadataAsync(playlistId);
            
            _allGroupsCache = OrderGroupsByLanguagePreference(meta.AllGroups);
            _liveGroupsCache = OrderGroupsByLanguagePreference(meta.LiveGroups);
            _vodGroupsCache = OrderGroupsByLanguagePreference(meta.VodGroups);
            _seriesGroupsCache = OrderGroupsByLanguagePreference(meta.SeriesGroups);
            
            UpdateGroupsForSelectedType();
            ResetIncrementalState();
            await LoadMoreChannelsAsync();
 
            StatusMessage = $"{meta.TotalCount:N0} içerik keyfinize hazır";

            // Fire-and-forget tasks are wrapped to avoid unobserved failures and task races.
            StartPostChannelLoadBackgroundTasks();
            EnsureChannelBackgroundRefresh();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"LoadChannels error: {ex}");
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Kanallar yuklenemedi", ex);
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
        // Rail içeriklerini yükle
        SetItems(TrendingChannels, Channels
            .Where(c => c.Type == ChannelType.Live)
            .OrderByDescending(HasDisplayImage)
            .ThenBy(c => c.Name)
            .Take(10));

        SetItems(LatestMovies, Channels
            .Where(c => c.Type == ChannelType.VOD)
            .OrderByDescending(HasDisplayImage)
            .ThenByDescending(c => c.Id)
            .Take(10));

        var playlistId = SelectedPlaylist?.Id ?? 0;
        _allSeriesCache = await _mediaService.GetSeriesAsync(playlistId);
        
        SetItems(LatestSeries, _allSeriesCache.Take(20));
        UpdateSeriesViewItems();

        SetItems(ContinueWatching, Channels
            .Where(c => c.LastWatched.HasValue)
            .OrderByDescending(c => c.LastWatched)
            .Take(10));

        // Hero içeriği
        FeaturedChannel = TrendingChannels.FirstOrDefault() ?? LatestMovies.FirstOrDefault();
    }

    private static bool HasDisplayImage(Channel channel)
        => IsDisplayImageUrl(channel.CoverUrl) || IsDisplayImageUrl(channel.LogoUrl);

    private static bool HasDisplayImage(Series series)
        => IsDisplayImageUrl(series.CoverUrl);

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
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _downloadsLandingRefreshCts;
    private int _isDownloadsLandingRefreshing;
    private readonly int _filterDelayMs = 300;
    private readonly int _searchDelayMs = 200;
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
    private Timer? _channelSyncTimer;
    private int _isBackgroundEpgSyncRunning;
    private int _isBackgroundChannelSyncRunning;
    private int _isManualEpgRefreshRunning;
    private int _isRefreshingPlaylist;
    private int _isAddingPlaylist;
    private int _isManualRefreshRunning;
    private readonly Dictionary<int, DateTime> _playlistNoChangeUntilUtc = new();
    private bool _suppressFilterRefresh;
    private Action<string>? _prioritizeStalkerCategoryAction;
    private int _isThrottledLoadPending = 0;
    private bool _seriesDetailDownloadedOnlyMode;

    private async Task ThrottledLoadChannelsAsync(int playlistId)
    {
        // Eğer zaten bir güncelleme sıradaysa (pending), yenisini ekleme
        if (Interlocked.CompareExchange(ref _isThrottledLoadPending, 1, 0) == 1)
        {
            return;
        }

        try
        {
            // 500ms bekle (Aynı anda biten diğer kategorilerin de veritabanına yazılmasına izin ver)
            await Task.Delay(500);
            
            _dispatcherService.BeginInvoke(async () =>
            {
                try
                {
                    await LoadChannelsAsync(playlistId);
                }
                finally
                {
                    Interlocked.Exchange(ref _isThrottledLoadPending, 0);
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref _isThrottledLoadPending, 0);
        }
    }

    public bool IsDownloadedSeriesDetailMode => _seriesDetailDownloadedOnlyMode;

    private void ResetIncrementalState()
    {
        _currentPage = 0;
        _hasMoreChannels = true;
        _isLoadingMoreChannels = false;
        Channels.Clear();
        FilteredChannels.Clear();
    }

    private void ResetSeriesIncrementalState()
    {
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = true;
        _isLoadingMoreSeriesItems = false;
        _seriesFilteredSource = new List<Series>();
        SeriesViewItems.Clear();
    }

    public async Task LoadMoreChannelsAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
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

            var page = await _playlistService.GetChannelsFilteredPageAsync(
                SelectedPlaylist.Id,
                skip: _currentPage * IncrementalPageSize,
                take: IncrementalPageSize,
                searchText: SearchText,
                group: effectiveGroup,
                type: effectiveType,
                onlyFavorites: ShowOnlyFavorites,
                sortOrder: SelectedSortOrder);

            // If selected group returns nothing on first page, fallback to "all" to avoid false empty UI.
            if (_currentPage == 0 &&
                page.Count == 0 &&
                !hasSearch &&
                !string.IsNullOrWhiteSpace(effectiveGroup))
            {
                var fallbackPage = await _playlistService.GetChannelsFilteredPageAsync(
                    SelectedPlaylist.Id,
                    skip: 0,
                    take: IncrementalPageSize,
                    searchText: SearchText,
                    group: null,
                    type: effectiveType,
                    onlyFavorites: ShowOnlyFavorites,
                    sortOrder: SelectedSortOrder);

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
                UpdateSearchBuckets();
                return;
            }

            _currentPage++;
            _hasMoreChannels = page.Count == IncrementalPageSize;

            foreach (var item in page)
            {
                FilteredChannels.Add(item);
                if (!ReferenceEquals(Channels, FilteredChannels))
                {
                    Channels.Add(item);
                }
            }

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
        }
        finally
        {
            _isLoadingMoreChannels = false;
        }
    }

    public async Task LoadMoreChannelsIfNeededAsync(double verticalOffset, double scrollableHeight)
    {
        if (scrollableHeight <= 0)
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
        if (scrollableHeight <= 0)
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
                return Task.CompletedTask;
            }

            _currentSeriesPage++;
            _hasMoreSeriesItems = page.Count == IncrementalPageSize;

            foreach (var item in page)
            {
                SeriesViewItems.Add(item);
            }

            // On-demand TMDB enrichment for newly visible series
            var enrichPage = page.Where(s => s.TmdbId == null && s.LastTmdbSync == null).ToList();
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
        }
        finally
        {
            _isLoadingMoreSeriesItems = false;
        }

        return Task.CompletedTask;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Debounce logic
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        _ = ApplyFiltersWithDelayAsync(token);
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        if (_suppressFilterRefresh)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            _prioritizeStalkerCategoryAction?.Invoke(value);
        }

        ScheduleImmediateFilter();
    }

    partial void OnSelectedChannelTypeChanged(ChannelType? value)
    {
        UpdateGroupsForSelectedType();
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
    }

    partial void OnShowOnlyFavoritesChanged(bool value)
    {
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

        foreach (var group in groups)
        {
            if (IsCountryPreferredGroup(group, preferredCountry))
            {
                preferred.Add(group);
            }
            else
            {
                others.Add(group);
            }
        }

        preferred.AddRange(others);
        return preferred;
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
        if (string.IsNullOrWhiteSpace(preferredCountry))
        {
            return;
        }

        var preferred = Groups.FirstOrDefault(g => IsCountryPreferredGroup(g, preferredCountry));
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return;
        }

        _suppressFilterRefresh = true;
        try
        {
            SelectedGroup = preferred;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private void UpdateGroupsForSelectedType()
    {
        var nextGroups = SelectedChannelType switch
        {
            ChannelType.Live => _liveGroupsCache,
            ChannelType.VOD => _vodGroupsCache,
            ChannelType.Series => _seriesGroupsCache,
            _ => _allGroupsCache
        };

        _dispatcherService.Invoke(() => Groups = new ObservableCollection<string>(nextGroups));

        if (!string.IsNullOrWhiteSpace(SelectedGroup) && !Groups.Contains(SelectedGroup))
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

    private void ScheduleImmediateFilter()
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        _ = ApplyFiltersAsync(token);
    }

    private async Task ApplyFiltersWithDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_filterDelayMs, token);
            await ApplyFiltersAsync(token);
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

    private async Task ApplyFiltersAsync(CancellationToken token)
    {
        if (SelectedPlaylist == null || token.IsCancellationRequested) return;

        BeginLoading();

        try
        {
            if (token.IsCancellationRequested) return;
            ResetIncrementalState();
            ResetSeriesIncrementalState();
            if (token.IsCancellationRequested) return;
            await LoadMoreChannelsAsync(token);
            UpdateSeriesViewItems();
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException) return;

            _logger?.LogDebug($"ApplyFilters error: {ex}");
            StatusMessage = "Filtreleme sırasında hata oluştu";
        }
        finally
        {
            EndLoading();
        }
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
                    // Fire and forget deep DB fallback
                    _ = RefreshLivePlaybackContextAsync(channel);
                }
            }
            else
            {
                _livePlaybackContext = null;
            }
        }
        
        SelectedChannel = channel;
        
        // Update last watched
        channel.LastWatched = DateTime.UtcNow;
        _ = _channelService.UpdateChannelAsync(channel);
        UpdateHistoryChannels();

        // Notify UI to play
        OnMediaSelected?.Invoke(channel);
    }

    private async Task RefreshLivePlaybackContextAsync(Channel targetChannel)
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
                _livePlaybackContext = null;
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
                 _livePlaybackContext = null;
                 return;
            }

            _livePlaybackContext = SelectedSortOrder switch
            {
                ChannelSortOrder.NameAsc => groupChannels.OrderBy(c => c.Name).ToList(),
                ChannelSortOrder.NameDesc => groupChannels.OrderByDescending(c => c.Name).ToList(),
                ChannelSortOrder.OldestFirst => groupChannels.OrderBy(c => c.Id).ToList(),
                _ => groupChannels.OrderByDescending(c => c.Id).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"RefreshLivePlaybackContextAsync failed: {ex.Message}");
            _livePlaybackContext = null;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(object media)
    {
        if (!CurrentProfileId.HasValue)
        {
            StatusMessage = "Önce bir profil seçmelisiniz";
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
            StatusMessage = channel.IsFavorite ? "Favorilere eklendi" : "Favorilerden çıkarıldı";
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
            StatusMessage = shouldFavorite ? "Favorilere eklendi" : "Favorilerden çıkarıldı";
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
            StatusMessage = "Lütfen playlist adı ve URL'sini girin";
            return;
        }

        if (Interlocked.Exchange(ref _isAddingPlaylist, 1) == 1)
        {
            StatusMessage = "Playlist ekleme zaten devam ediyor...";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Playlist ekleniyor...";
            
            var playlist = await _playlistService.AddFromUrlAsync(NewPlaylistName, NewPlaylistUrl, CurrentProfileId);
            await LoadPlaylistsAsync();
            SelectedPlaylist = playlist;
            
            StatusMessage = $"'{NewPlaylistName}' playlist eklendi ({playlist.ChannelCount} kanal)";
            NewPlaylistName = string.Empty;
            NewPlaylistUrl = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Islem basarisiz", ex);
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
        if (SelectedPlaylist == null)
        {
            if (CurrentProfile != null && !isBackground)
            {
                // If profile has no playlist record, try to load/create it
                await LoadProfileAsync(CurrentProfile);
            }
            return;
        }

        if (!isBackground &&
            _playlistNoChangeUntilUtc.TryGetValue(SelectedPlaylist.Id, out var noChangeUntil) &&
            noChangeUntil > DateTime.UtcNow)
        {
            StatusMessage = "Kanal listesi zaten guncel";
            await TouchPlaylistLastUpdatedAsync(SelectedPlaylist.Id);
            return;
        }

        if (Interlocked.Exchange(ref _isRefreshingPlaylist, 1) == 1)
        {
            if (!isBackground)
            {
                StatusMessage = "Playlist zaten yenileniyor...";
            }
            return;
        }

        if (!isBackground)
        {
            if (Interlocked.Exchange(ref _isManualRefreshRunning, 1) == 1)
            {
                StatusMessage = "Baska bir yenileme islemi zaten devam ediyor...";
                Interlocked.Exchange(ref _isRefreshingPlaylist, 0);
                return;
            }
        }

        try
        {
            if (!isBackground)
            {
                BeginLoading();
                StatusMessage = "Kanal listesi güncelleniyor...";
            }

            var playlistId = SelectedPlaylist.Id;
            var profile = CurrentProfile;

            if (profile != null && profile.ProviderAccount != null)
            {
                if (profile.ProviderAccount.Type == ProfileType.StalkerPortal)
                {
                    // Stalker için tam yenileme başlat
                    _ = Task.Run(() => ResumeStalkerProgressiveLoadingAsync(profile, SelectedPlaylist, isFullRefresh: true));
                    if (!isBackground) StatusMessage = "Stalker listesi arka planda yenileniyor...";
                    return;
                }
                else if (profile.ProviderAccount.Type == ProfileType.XtreamCodes)
                {
                    // Xtream için tam yenileme başlat
                    _ = Task.Run(() => ResumeXtreamProgressiveLoadingAsync(profile, SelectedPlaylist, isFullRefresh: true));
                    if (!isBackground) StatusMessage = "Xtream listesi arka planda yenileniyor...";
                    return;
                }
            }

            // Varsayılan M3U mantığı
            var beforeCount = await _playlistService.GetChannelCountAsync(playlistId);
            await _playlistService.RefreshAsync(playlistId);
            var afterCount = await _playlistService.GetChannelCountAsync(playlistId);
            var addedCount = Math.Max(0, afterCount - beforeCount);

            if (addedCount > 0)
            {
                await LoadChannelsAsync(playlistId);
            }

            if (!isBackground)
            {
                StatusMessage = addedCount == 0
                    ? "Kanal listesi zaten guncel"
                    : $"Kanal listesi guncellendi ({addedCount} yeni kanal eklendi)";
                    
                ChannelListLastError = null;

                if (addedCount == 0)
                {
                    _playlistNoChangeUntilUtc[playlistId] = DateTime.UtcNow.AddMinutes(2);
                }
                else
                {
                    _playlistNoChangeUntilUtc.Remove(playlistId);
                }
            }
        }
        catch (Exception ex)
        {
            if (!isBackground)
            {
                StatusMessage = UserFriendlyErrorMessage.WithPrefix("Kanal listesi guncelleme hatasi", ex);
                ChannelListLastError = UserFriendlyErrorMessage.FromException(ex);
            }
        }
        finally
        {
            if (!isBackground)
            {
                IsLoading = false;
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
            StatusMessage = "Baska bir yenileme islemi zaten devam ediyor...";
            return false;
        }

        _ = RunManualEpgRefreshBackgroundAsync();
        return true;
    }

    private async Task RunManualEpgRefreshBackgroundAsync()
    {
        try
        {
            StatusMessage = "EPG yenileme başlatıldı...";
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
        if (CurrentProfile == null) return;

        if (!isBackgroundSync)
        {
            if (Interlocked.Exchange(ref _isManualEpgRefreshRunning, 1) == 1)
            {
                StatusMessage = "EPG yenileme zaten devam ediyor...";
                return;
            }

            if (Interlocked.Exchange(ref _isManualRefreshRunning, 1) == 1)
            {
                StatusMessage = "Baska bir yenileme islemi zaten devam ediyor...";
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
                StatusMessage = "EPG güncelleniyor...";
            }

            using var db = await _contextFactory.CreateDbContextAsync();

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
                channelsForMapping = await _playlistService.GetChannelsAsync(SelectedPlaylist.Id);
            }

            // EPG is relevant for live channels only.
            var liveChannels = channelsForMapping
                .Where(c => c.Type == ChannelType.Live)
                .ToList();

            if (liveChannels.Count == 0)
            {
                if (!isBackgroundSync)
                {
                    StatusMessage = "Canlı kanal bulunamadı, EPG atlandı";
                }
                return;
            }

            // Initial EPG visibility check: ensure we have programs in DB if not refreshing
        if (!forceRefresh && SelectedPlaylist != null)
        {
            var hasCachedPrograms = await HasEpgForChannelsAsync(db, liveChannels);
            if (hasCachedPrograms && isBackgroundSync)
            {
                // In background sync, if we have programs, we still continue to download based on timer
                // but we can skip if we really want to save bandwidth. 
                // However, the user wants "direct connection" and no forced limits.
            }
        }

            // 1. Provider EPG URL (Xtream)
            string? providerEpgUrl = null;
            if (CurrentProfile.ProviderAccount?.Type == ProfileType.XtreamCodes)
            {
                var baseUrl = CurrentProfile.ProviderAccount.Url.TrimEnd('/');
                if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
                var decryptedPassword = _securityService.Decrypt(CurrentProfile.ProviderAccount.Password) ?? "";
                providerEpgUrl = $"{baseUrl}/xmltv.php?username={Uri.EscapeDataString(CurrentProfile.ProviderAccount.Username ?? "")}&password={Uri.EscapeDataString(decryptedPassword)}";
            }

            // 2. Çoklu ülke tespiti (App Language + Top Major Countries)
            var channelNames = channelsForMapping.Select(c => c.Name ?? "").ToList();
            var appLanguage = (_settingsService.Settings.Language ?? "tr").ToUpperInvariant();

            // Detect top countries (limit to top 2 other major countries to save data)
            var majorCountries = _languageDetectionService.DetectCountries(channelNames)
                .Where(c => c.Percentage > 20 || c.ChannelCount > 50) // Daha sıkı eşik: %20 pay veya 50+ kanal
                .OrderByDescending(c => c.Percentage)
                .Take(2)
                .Select(c => c.CountryCode.ToUpperInvariant())
                .ToList();

            // 3. EPG kaynaklarını topla (App Language her zaman dahil edilir)
            var playlistEpgUrl = (SelectedPlaylist?.EpgUrl ?? string.Empty).Trim();
            var customEpgUrl = (_settingsService.Settings.CustomEpgUrl ?? string.Empty).Trim();
            var hasUsableTvgIds = channelsForMapping.Any(c => !string.IsNullOrWhiteSpace(c.TvgId));
            
            var epgSources = _epgSourceResolver.ResolveEpgSources(
                majorCountries, 
                providerEpgUrl, 
                playlistEpgUrl, 
                Uri.TryCreate(customEpgUrl, UriKind.Absolute, out _) ? customEpgUrl : null,
                hasUsableTvgIds,
                preferredLanguageCode: appLanguage);

            if (!isBackgroundSync)
            {
                StatusMessage = "EPG kaynakları deneniyor...";
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

            // Yabancı kanallar için popülerlik filtresi kelimeleri (Büyük harf duyarlı, kanal isimleri üst karakter yapıldığı için)
            var popularKeywords = new[] 
            { 
                "BBC", "ITV", "SKY", "ABC", "CBS", "NBC", "FOX", "CNN", "ESPN", "HBO", "SHOWTIME", "AMC", "TNT", "TBS", "SYFY", "BEIN", "BE IN", "BEN",
                "DISCOVERY", "HISTORY", "NAT GEO", "NATGEO", "USA", "TLC", "DMAX", "BLOOMBERG", "CNBC", "AL JAZEERA", "ALJAZEERA", 
                "EUROSPORT", "ANIMAL PLANET", "HGTV", "FOOD NETWORK", "ARD", "ZDF", "RTL", "SAT1", "PROSIEBEN", "VOX", "WELT", 
                "NTV", "TF1", "CANAL", "M6", "ARTE", "BFM", "RAI", "MEDIASET", "CANALE5", "ITALIA1", "RETE4", "LA7", "TVE", 
                "ANTENA3", "CUATRO", "TELECINCO", "LASEXTA", "MOVISTAR", "FRANCE24", "NICKELODEON", "CARTOON", "PARAMOUNT", 
                "SONY", "MTV", "VH1", "INVESTIGATION", "MOVIES", "SPORTS", "KIDS", "DISNEY", "CINEMA", "NEWS", "NETFLIX", "HD", "4K" 
            };

            foreach (var source in epgSources)
            {
                try
                {
                    if (!isBackgroundSync)
                    {
                        StatusMessage = $"EPG: {source.Type} kaynağına bağlanılıyor...";
                    }

                    // Sadece bu kaynağa (ülkeye) ait olan kanalları filtrele
                    List<Channel> targetChannels;
                    
                    if (source.Type == EpgSourceType.Provider || source.Type == EpgSourceType.CustomUrl || source.Url.Contains($"-{appLanguage.ToLower()}.", StringComparison.OrdinalIgnoreCase))
                    {
                        // Ana kaynaklar veya kendi dilimiz: Tüm kanalları dene
                        targetChannels = liveChannels;
                    }
                    else 
                    {
                        // Yabancı Ülke Kaynağı: Sadece o ülkenin popüler kanallarını işle (Kullanıcı isteği)
                        var sourceCountryCode = ExtractCountryCodeFromUrl(source.Url);
                        targetChannels = liveChannels
                            .Where(c => 
                            {
                                var name = (c.Name ?? "").ToUpperInvariant();
                                // 1. Kanal bu ülkeye mi ait?
                                bool isThisCountry = !string.IsNullOrEmpty(sourceCountryCode) && name.Contains(sourceCountryCode);
                                if (!isThisCountry) return false;

                                // 2. Popüler mi? (İsminde majör kelimeler geçiyor mu?)
                                return popularKeywords.Any(k => name.Contains(k));
                            })
                            .ToList();

                        _logger?.LogDebug($"[EPG] Foreign source {sourceCountryCode} optimized: matching only {targetChannels.Count} popular channels.");
                    }

                    if (targetChannels.Count == 0 && source.Type == EpgSourceType.IptvEpgOrg) continue;

                    var loadedPrograms = await _epgService.LoadEpgAsync(
                        source.Url, 
                        source.IsPrimary, 
                        targetChannels, 
                        daysAhead: 1, 
                        progress: epgProgressReporter,
                        clearBeforeSave: source.ClearBeforeLoad); // ATOMIC CLEAR: Only clear if we actually start saving programs

                    if (loadedPrograms > 0)
                    {
                        anySuccess = true;
                        successfulSourceUrl = source.Url;
                        _logger?.LogDebug($"[MainViewModel] EPG loaded from {source.Type} ({loadedPrograms} programs) - URL: {source.Url}");
                        lastSourceError = null;
                    }
                    else
                    {
                        if (!anySuccess) lastSourceError = $"{source.Type}: 0 program";
                    }
                }
                catch (Exception ex)
                {
                    lastSourceError = $"{source.Type}: {UserFriendlyErrorMessage.FromException(ex)}";
                    _logger?.LogDebug($"[MainViewModel] EPG source failed: {source.Type} - {ex.Message}");
                }
            }

            EpgProgress = null; // Clear progress info when done

            EnsureEpgBackgroundSync();

            if (!isBackgroundSync)
            {
                StatusMessage = anySuccess
                    ? "EPG hazır"
                    : $"EPG yüklenemedi{(string.IsNullOrWhiteSpace(lastSourceError) ? "" : $" ({lastSourceError})")}";
            }

            if (SelectedPlaylist != null)
            {
                var playlistToUpdate = await db.Playlists.FirstOrDefaultAsync(p => p.Id == SelectedPlaylist.Id);
                if (playlistToUpdate != null)
                {
                    if (anySuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(successfulSourceUrl))
                        {
                            playlistToUpdate.EpgUrl = successfulSourceUrl;
                            SelectedPlaylist.EpgUrl = successfulSourceUrl;
                        }

                        playlistToUpdate.EpgLastUpdated = DateTime.UtcNow;
                        playlistToUpdate.EpgLastError = null;
                        await db.SaveChangesAsync();
                        SelectedPlaylist.EpgLastUpdated = playlistToUpdate.EpgLastUpdated;
                        SelectedPlaylist.EpgLastError = null;
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
                StatusMessage = UserFriendlyErrorMessage.WithPrefix("EPG hatasi", ex);
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

    private static string ExtractCountryCodeFromUrl(string url)
    {
        // Örn: https://iptv-epg.org/files/epg-tr.xml.gz -> TR
        try
        {
            var fileName = Path.GetFileName(url);
            var parts = fileName.Split('-');
            if (parts.Length >= 2)
            {
                var codePart = parts[1];
                var dotIdx = codePart.IndexOf('.');
                if (dotIdx > 0) return codePart.Substring(0, dotIdx).ToUpperInvariant();
            }
        }
        catch { }
        return string.Empty;
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
        if (hours <= 0)
        {
            _epgSyncTimer?.Dispose();
            _epgSyncTimer = null;
            return;
        }

        var interval = TimeSpan.FromHours(hours);
        if (_epgSyncTimer != null)
        {
            _epgSyncTimer.Change(interval, interval);
            return;
        }

        _epgSyncTimer = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref _isBackgroundEpgSyncRunning, 1) == 1)
            {
                return;
            }

            try
            {
                await LoadEpgInternalAsync(isBackgroundSync: true, forceRefresh: false);
            }
            finally
            {
                Interlocked.Exchange(ref _isBackgroundEpgSyncRunning, 0);
            }
        }, null, interval, interval);
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
    private ObservableCollection<object> _myList = new();

    [ObservableProperty]
    private ObservableCollection<object> _favoriteChannels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _historyChannels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _historyLiveChannels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _historySeriesChannels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _historyVodChannels = new();

    [ObservableProperty]
    private ObservableCollection<Series> _downloadedSeriesItems = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _downloadedVodChannels = new();

    [ObservableProperty]
    private ObservableCollection<DownloadItem> _activeDownloadItems = new();

    [ObservableProperty]
    private ObservableCollection<DownloadItem> _activeDownloadingItems = new();

    [ObservableProperty]
    private ObservableCollection<DownloadItem> _queuedDownloadItems = new();

    [ObservableProperty]
    private ObservableCollection<DownloadItem> _completedDownloadItems = new();

    [ObservableProperty]
    private int _activeDownloadCount;

    [ObservableProperty]
    private int _totalDownloadedCount;

    [ObservableProperty]
    private string _totalDownloadsInfoText = "0 içerik • 0 B kullanıldı";

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
    private ObservableCollection<Channel> _searchLiveChannels = new();

    [ObservableProperty]
    private ObservableCollection<Series> _searchSeriesChannels = new();


    [ObservableProperty]
    private ObservableCollection<Channel> _searchVodChannels = new();

    [ObservableProperty]
    private string _searchSuggestion = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Channel> _searchSimilarLiveChannels = new();

    [ObservableProperty]
    private ObservableCollection<Series> _searchSimilarSeriesChannels = new();

    [ObservableProperty]
    private ObservableCollection<Channel> _searchSimilarVodChannels = new();

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

    [ObservableProperty]
    private DownloadSortOrder _selectedDownloadSortOrder = DownloadSortOrder.Latest;

    partial void OnSelectedDownloadSortOrderChanged(DownloadSortOrder value) => _ = RefreshDownloadedItemsFromDatabaseAsync();

    public bool ShowDownloadsLandingEmptyState => !IsDownloadCenterVisible && ShowDownloadsEmptyState;

    [RelayCommand]
    private void Navigate(AppView view)
    {
        if (view != AppView.Search && !string.IsNullOrWhiteSpace(SearchText))
        {
            SearchText = string.Empty;
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
        
        ScheduleImmediateFilter();
    }

    private void UpdateMyList()
    {
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
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty));
            ShowMyListEmptyState = MyList.Count == 0;
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
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty));
            ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"UpdateFavoriteChannels failed: {ex}");
            FavoriteChannels.Clear();
            ShowFavoritesEmptyState = true;
        }
    }

    private void UpdateHistoryChannels()
    {
        SetItems(HistoryChannels, Channels
            .Where(c => c.LastWatched.HasValue)
            .OrderByDescending(c => c.LastWatched));
        UpdateHistoryBuckets();

        _ = RefreshHistoryChannelsOnlyAsync();
    }

    private void UpdateHistoryBuckets()
    {
        SetItems(HistoryLiveChannels, HistoryChannels.Where(c => c.Type == ChannelType.Live));
        SetItems(HistorySeriesChannels, HistoryChannels.Where(c => c.Type == ChannelType.Series));
        SetItems(HistoryVodChannels, HistoryChannels.Where(c => c.Type == ChannelType.VOD));
        ShowHistoryEmptyState = HistoryChannels.Count == 0;
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
                var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { ".mkv", ".mp4", ".avi", ".ts", ".m4v", ".mov", ".nctra" };

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
            TotalDownloadsInfoText = $"{totalCount} içerik • {FormatDownloadBytes(totalSizeBytes)} kullanıldı";

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
        ActiveDownloadsTotalSpeedText = "0 B/sn";
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
        ActiveDownloadsTotalSpeedText = $"{FormatDownloadBytes((long)totalSpeed)}/sn";
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
            return $"{FormatDownloadBytes(drive.AvailableFreeSpace)} boş";
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

        string title = media is Series s ? s.Name : (media is Channel c ? c.Name : "İçerik");
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "İçeriği Sil",
            $"'{title}' içeriği ve tüm dosyaları kalıcı olarak silinecektir. Emin misiniz?");

        if (!confirmed) return;

        try
        {
            var filesToDelete = new List<string>();

            if (media is Series series)
            {
                foreach (var season in series.Seasons)
                {
                    foreach (var ep in season.Episodes)
                    {
                        if (!string.IsNullOrEmpty(ep.StreamUrl) && File.Exists(ep.StreamUrl))
                            filesToDelete.Add(ep.StreamUrl);
                    }
                }
            }
            else if (media is Channel channel)
            {
                if (!string.IsNullOrEmpty(channel.StreamUrl) && File.Exists(channel.StreamUrl))
                    filesToDelete.Add(channel.StreamUrl);
            }

            // 1. Delete Files
            foreach (var file in filesToDelete)
            {
                try { File.Delete(file); } catch { /* ignore */ }
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
            await _dialogService.ShowErrorAsync("Hata", "İçerik silinirken bir hata oluştu.");
        }
    }

    [RelayCommand]
    private async Task DeleteAllDownloadsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Tüm İndirmeleri Sil",
            "Tüm indirilen içerikler ve dosyalar kalıcı olarak silinecektir. Emin misiniz?");

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
            await _dialogService.ShowErrorAsync("Hata", "İndirmeler silinirken bir hata oluştu.");
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
                    "Devam Ettirilemedi", 
                    "Bu indirme baska bir hesaba ait. Devam ettirmek icin once o hesaba (profile) gecis yapmalisiniz.");
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
                "Kuyruğu Temizle",
                "Kuyruktaki tüm indirmeler iptal edilecektir. Emin misiniz?");

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
                HistorySeriesChannels.Clear();
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
                HistorySeriesChannels.Clear();
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
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty));

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
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty));

            SetItems(HistoryChannels, await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds));
            UpdateHistoryBuckets();

            ShowMyListEmptyState = MyList.Count == 0;
            ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
            if (ActiveView == AppView.Downloads)
            {
                await RefreshDownloadedItemsFromDatabaseAsync();
            }
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
        await _watchHistoryService.CleanupOlderThanDaysAsync(CurrentProfileId.Value, 7);
        var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);

        if (profilePlaylistIds.Count == 0)
        {
            HistoryChannels.Clear();
            HistoryLiveChannels.Clear();
            HistorySeriesChannels.Clear();
            HistoryVodChannels.Clear();
            ShowHistoryEmptyState = true;
            return;
        }

        SetItems(HistoryChannels, await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds));
        UpdateHistoryBuckets();
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

    private async Task<List<Channel>> GetHistoryChannelsFromWatchHistoryAsync(AppDbContext db, List<int> profilePlaylistIds)
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

        var seriesSnapshot = _allSeriesCache;
        
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

        var similarLive = Channels
            .Where(c => c.Type == ChannelType.Live)
            .Where(c => IsLikelySimilar(rawQuery, c.Name))
            .Where(c => !SearchLiveChannels.Any(x => x.Id == c.Id))
            .OrderByDescending(HasDisplayImage)
            .Take(12)
            .ToList();

        var similarSeries = seriesSnapshot
            .Where(s => IsLikelySimilar(rawQuery, s.Name))
            .Where(s => !SearchSeriesChannels.Any(x => x.Id == s.Id))
            .OrderByDescending(HasDisplayImage)
            .Take(12)
            .ToList();

        var similarVod = Channels
            .Where(c => c.Type == ChannelType.VOD)
            .Where(c => IsLikelySimilar(rawQuery, c.Name))
            .Where(c => !SearchVodChannels.Any(x => x.Id == c.Id))
            .OrderByDescending(HasDisplayImage)
            .Take(12)
            .ToList();

        SetItems(SearchSimilarLiveChannels, similarLive);
        SetItems(SearchSimilarSeriesChannels, similarSeries);
        SetItems(SearchSimilarVodChannels, similarVod);
        ShowSearchSimilarSection = similarLive.Count > 0 || similarSeries.Count > 0 || similarVod.Count > 0;
    }

    private static string ComputeBestSuggestion(string query, IEnumerable<string> candidates)
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

            // Reject if lengths are too different
            if (Math.Abs(normalizedCandidate.Length - normalizedQuery.Length) > 3)
                continue;

            var maxAllowedDist = GetDistanceThreshold(Math.Max(normalizedQuery.Length, normalizedCandidate.Length));
            var distance = LevenshteinDistance(normalizedQuery, normalizedCandidate, maxAllowedDist);
            
            if (distance < 0) continue;

            // Calculate similarity score (0.0 to 1.0)
            double similarity = 1.0 - ((double)distance / Math.Max(normalizedQuery.Length, normalizedCandidate.Length));
            
            // Bonus for starting with the same letters
            if (normalizedCandidate.StartsWith(normalizedQuery[..Math.Min(2, normalizedQuery.Length)], StringComparison.OrdinalIgnoreCase))
                similarity += 0.1;

            if (similarity > bestScore && similarity > 0.7)
            {
                bestScore = similarity;
                best = candidate;
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

        var maxDistance = GetDistanceThreshold(Math.Max(normalizedQuery.Length, normalizedCandidate.Length));
        var distance = LevenshteinDistance(normalizedQuery, normalizedCandidate, maxDistance);
        
        if (distance < 0) return false;

        double similarity = 1.0 - ((double)distance / Math.Max(normalizedQuery.Length, normalizedCandidate.Length));
        return similarity >= 0.75;
    }

    private static int GetDistanceThreshold(int length)
    {
        if (length <= 5) return 1;
        if (length <= 10) return 2;
        if (length <= 16) return 3;
        return 4;
    }

    private static string NormalizeFuzzyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{Nd}\s]", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }

    private static int LevenshteinDistance(string source, string target, int maxDistance)
    {
        if (source == target)
        {
            return 0;
        }

        if (Math.Abs(source.Length - target.Length) > maxDistance)
        {
            return -1;
        }

        var pool = System.Buffers.ArrayPool<int>.Shared;
        var previous = pool.Rent(target.Length + 1);
        var current = pool.Rent(target.Length + 1);

        try
        {
            for (var j = 0; j <= target.Length; j++)
            {
                previous[j] = j;
            }

            for (var i = 1; i <= source.Length; i++)
            {
                current[0] = i;
                var rowMin = current[0];

                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                    if (current[j] < rowMin)
                    {
                        rowMin = current[j];
                    }
                }

                if (rowMin > maxDistance)
                {
                    return -1;
                }

                (previous, current) = (current, previous);
            }

            return previous[target.Length] <= maxDistance ? previous[target.Length] : -1;
        }
        finally
        {
            pool.Return(previous);
            pool.Return(current);
        }
    }

    private void UpdateSeriesViewItems()
    {
        var source = _allSeriesCache;
        if (source.Count == 0)
        {
            ResetSeriesIncrementalState();
            return;
        }

        var query = SearchText?.Trim() ?? string.Empty;
        var normalizedQuery = NormalizeSeriesQuery(query);
        var selectedGroup = SelectedGroup?.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(query);

        var filtered = source.Where(series =>
        {
            var groupOk = true;
            if (!hasSearch && !string.IsNullOrWhiteSpace(selectedGroup))
            {
                var category = series.DisplayCategory ?? string.Empty;
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
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = true;
        SeriesViewItems.Clear();
        _ = LoadMoreSeriesAsync();
    }


    [RelayCommand]
    private void CommitSearch()
    {
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
        if (string.IsNullOrWhiteSpace(SearchSuggestion))
        {
            return;
        }

        SearchText = SearchSuggestion;
        Navigate(AppView.Search);
    }


    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();

        // Only clear results when query is emptied.
        // Actual search triggers on Enter/button via CommitSearch.
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
        }
    }

    private async Task SearchOverlayAsync(string query, CancellationToken token)
    {
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
                StatusMessage = "⚠️ Playlist sunucusu yanıt vermiyor. URL'yi kontrol edin.");

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
            StatusMessage = "Önce bir profil seçmelisiniz";
            return;
        }

        using var db = await _contextFactory.CreateDbContextAsync();
        
        if (media is Channel channel)
        {
            if (channel.Type == ChannelType.Live)
            {
                StatusMessage = "Canlı kanallar listeme eklenemez";
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
            StatusMessage = channel.IsInMyList ? "Listene eklendi" : "Listenden çıkarıldı";
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

            StatusMessage = series.IsInMyList ? "Listene eklendi" : "Listenden çıkarıldı";
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

        StatusMessage = "Listenden çıkarıldı";
        await RefreshPersonalListsFromDatabaseAsync();
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

        StatusMessage = "Favorilerden çıkarıldı";
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
            StatusMessage = $"⚠️ İndirme başarısız: \"{episode.Name}\" için kaynak URL bulunamadı. Provider verisi eksik olabilir.";
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
                ? $"✅ İndirme kuyruğuna eklendi: {episode.Name}"
                : result.AlreadyExists
                    ? $"ℹ️ Zaten indirilmiş: {episode.Name}"
                    : $"❌ {result.Message}";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Episode download failed: {Name}", episode.Name);
            StatusMessage = $"❌ İndirme hatası: {ex.Message}";
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

        StatusMessage = $"📥 Sezon {SelectedSeason.SeasonNumber} indirme kuyruğuna ekleniyor... ({episodes.Count} bölüm)";

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
        if (queued > 0) parts.Add($"{queued} bölüm kuyruğa eklendi");
        if (failed > 0) parts.Add($"{failed} bölüm için kaynak URL bulunamadı");
        if (skipped > 0) parts.Add($"{skipped} bölüm zaten indirilmiş");
        StatusMessage = $"✅ Sezon {SelectedSeason.SeasonNumber}: {string.Join(", ", parts)}";
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
                StatusMessage = "⚠️ Playlist kaynağına ulaşılamıyor.");
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
    private async Task PlayFeatured()
    {
        if (FeaturedChannel != null)
        {
            await SelectMedia(FeaturedChannel);
        }
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

        SearchQuery = string.Empty;

        if (media is Channel channel)
        {
            channel.StreamUrl = await ResolvePreferredStreamUrlAsync(channel.StreamUrl);

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
                    EnsureSeriesEpisodes(selectedSeries);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"EnsureSeriesEpisodes failed: {ex.Message}");
                }
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

    private async Task LoadSelectedSeriesMetadataAsync(Series series)
    {
        try
        {
            IsSelectedSeriesMetadataLoading = true;

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
            
            var lastWatched = allEpisodes.LastOrDefault(e => e.LastWatched.HasValue);
            if (lastWatched != null)
            {
                var nextIndex = allEpisodes.IndexOf(lastWatched) + 1;
                if (nextIndex < allEpisodes.Count)
                {
                    var next = allEpisodes[nextIndex];
                    SelectedSeriesContinueEpisode = next;
                    SelectedSeriesContinueText = $"S{next.Season?.SeasonNumber ?? 1} B{next.EpisodeNumber}'den Devam Et";
                }
                else
                {
                    SelectedSeriesContinueEpisode = lastWatched;
                    SelectedSeriesContinueText = $"S{lastWatched.Season?.SeasonNumber ?? 1} B{lastWatched.EpisodeNumber}'i Yeniden İzle";
                }
            }
            else if (allEpisodes.Count > 0)
            {
                var first = allEpisodes[0];
                SelectedSeriesContinueEpisode = first;
                SelectedSeriesContinueText = $"S{first.Season?.SeasonNumber ?? 1} B{first.EpisodeNumber}'den Başla";
            }
            else
            {
                SelectedSeriesContinueEpisode = null;
                SelectedSeriesContinueText = null;
            }

            // Set first season by default
            if (series.Seasons.Count > 0 && SelectedSeason == null)
            {
                SelectedSeason = series.Seasons.OrderBy(s => s.SeasonNumber).FirstOrDefault();
            }

            // --- TMDB Metadata Fetch Strategy ---
            // MetadataFetchedAt is set only when full detail data (cast, contentRating, trailer) has been fetched.
            // Phase 1 (scroll search-only) intentionally leaves this null.
            var hasFullData = series.MetadataFetchedAt.HasValue;
            if (hasFullData)
            {
                return; // All data already loaded from DB, no API call needed
            }

            var languageCode = SeriesInfoParser.ExtractLanguageCode(series.GroupTitle ?? series.Genre ?? series.Name);
            ChannelMetadata? metadata = null;

            // 2. If we have a TmdbId → use direct ID lookup (no search, no wrong matches)
            if (series.TmdbId.HasValue && series.TmdbId.Value > 0)
            {
                var details = await _metadataService.FetchSeriesDetailsAsync(series.TmdbId.Value, languageCode);
                if (details != null && SelectedSeries?.Id == series.Id)
                {
                    metadata = new ChannelMetadata
                    {
                        TmdbId = series.TmdbId,
                        Title = details.Name,
                        Description = details.Overview,
                        PosterUrl = !string.IsNullOrEmpty(details.PosterPath) ? $"https://image.tmdb.org/t/p/w500{details.PosterPath}" : null,
                        BackdropUrl = !string.IsNullOrEmpty(details.BackdropPath) ? $"https://image.tmdb.org/t/p/original{details.BackdropPath}" : null,
                        Rating = details.VoteAverage,
                        ReleaseYear = details.ReleaseYear
                    };

                    // Genres from detail response
                    if (details.Genres != null && details.Genres.Count > 0)
                    {
                        metadata.Genres = details.Genres.Select(g => g.Name).ToList();
                    }

                    // Credits
                    if (details.Credits != null)
                    {
                        var director = details.Credits.Crew?.FirstOrDefault(c => c.Job == "Director")?.Name;
                        if (!string.IsNullOrEmpty(director))
                            metadata.Director = director;

                        var castList = details.Credits.Cast?.OrderBy(c => c.Order).Take(5).Select(c => c.Name).ToList();
                        if (castList != null && castList.Any())
                            metadata.Cast = string.Join(", ", castList);
                    }

                    // Content Rating
                    if (details.ContentRatings?.Results != null)
                    {
                        var trRating = details.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "US")?.Rating;
                        var usRating = details.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "TR")?.Rating;
                        metadata.ContentRating = usRating ?? trRating;
                    }
                    // Trailer Video (YouTube)
                    var trailer = details.Videos?.Results?
                        .Where(v => v.Site == "YouTube" && (v.Type == "Trailer" || v.Type == "Teaser"))
                        .OrderByDescending(v => v.Official)
                        .ThenByDescending(v => v.Type == "Trailer")
                        .FirstOrDefault();
                    if (trailer != null && !string.IsNullOrEmpty(trailer.Key))
                    {
                        metadata.TrailerUrl = $"https://www.youtube.com/watch?v={trailer.Key}";
                    }
                }
            }

            // 3. Fallback: search by cleaned name (only if no TmdbId)
            if (metadata == null && !series.TmdbId.HasValue)
            {
                var cleanName = SeriesInfoParser.CleanSeriesName(series.Name);
                metadata = await _metadataService.FetchMetadataAsync(cleanName, ChannelType.Series, languageCode);
            }

            if (metadata == null || SelectedSeries?.Id != series.Id)
            {
                return;
            }

            // Apply metadata to UI
            if (!string.IsNullOrWhiteSpace(metadata.PosterUrl))
            {
                SelectedSeriesPosterUrl = metadata.PosterUrl;
                series.CoverUrl = metadata.PosterUrl;
            }

            if (!string.IsNullOrWhiteSpace(metadata.BackdropUrl))
                SelectedSeriesBackdropUrl = metadata.BackdropUrl;

            if (!string.IsNullOrWhiteSpace(metadata.Description))
                SelectedSeriesOverview = metadata.Description;

            if (!string.IsNullOrWhiteSpace(metadata.Cast))
                SelectedSeriesCast = metadata.Cast;

            if (!string.IsNullOrWhiteSpace(metadata.ContentRating))
                SelectedSeriesAgeRating = metadata.ContentRating;

            if (metadata.Genres != null && metadata.Genres.Count > 0)
                SelectedSeriesGenres = string.Join(", ", metadata.Genres);

            if (metadata.ReleaseYear.HasValue && metadata.ReleaseYear.Value > 0)
                SelectedSeriesYears = metadata.ReleaseYear.Value.ToString();

            if (!string.IsNullOrWhiteSpace(metadata.TrailerUrl))
                SelectedSeriesTrailerUrl = metadata.TrailerUrl;

            // 4. Persist fetched data back to DB so future clicks are instant
            _ = Task.Run(async () =>
            {
                try
                {
                    using var db = await _contextFactory.CreateDbContextAsync();
                    var dbSeries = await db.Series.FindAsync(series.Id);
                    if (dbSeries == null) return;

                    var changed = false;
                    if (metadata.TmdbId.HasValue && !dbSeries.TmdbId.HasValue) { dbSeries.TmdbId = metadata.TmdbId; changed = true; }
                    if (!string.IsNullOrWhiteSpace(metadata.Description)) { dbSeries.Plot = metadata.Description; changed = true; }
                    if (!string.IsNullOrWhiteSpace(metadata.Cast)) { dbSeries.Cast = metadata.Cast; changed = true; }
                    if (!string.IsNullOrWhiteSpace(metadata.Director)) { dbSeries.Director = metadata.Director; changed = true; }
                    if (!string.IsNullOrWhiteSpace(metadata.ContentRating)) { dbSeries.ContentRating = metadata.ContentRating; changed = true; }
                    if (!string.IsNullOrWhiteSpace(metadata.BackdropUrl)) { dbSeries.BackdropUrl = metadata.BackdropUrl; changed = true; }
                    if (!string.IsNullOrWhiteSpace(metadata.PosterUrl)) { dbSeries.CoverUrl = metadata.PosterUrl; changed = true; }
                    if (metadata.ReleaseYear.HasValue) { dbSeries.ReleaseYear = metadata.ReleaseYear; changed = true; }
                    if (metadata.Rating.HasValue) { dbSeries.Rating = metadata.Rating; changed = true; }
                    if (!dbSeries.LastTmdbSync.HasValue) { dbSeries.LastTmdbSync = DateTime.UtcNow; changed = true; }
                    if (metadata.Genres != null && metadata.Genres.Count > 0)
                    {
                        dbSeries.Genre = string.Join(", ", metadata.Genres);
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(metadata.TrailerUrl)) { dbSeries.TrailerUrl = metadata.TrailerUrl; changed = true; }

                    if (changed)
                    {
                        dbSeries.MetadataFetchedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"Failed to persist series metadata: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"Series metadata load failed: {ex.Message}");
        }
        finally
        {
            IsSelectedSeriesMetadataLoading = false;
        }
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
                CoverUrl = channel.LogoUrl
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

        // --- LAZY LOAD TMDB METADATA (Seasons & Episodes) ---
        if (source.TmdbId.HasValue && source.MetadataFetchedAt == null)
        {
            try
            {
                var languageCode = SeriesInfoParser.ExtractLanguageCode(source.Name);

                // 1. Fetch deep Series info
                var tmdbSeries = await _metadataService.FetchSeriesDetailsAsync(source.TmdbId.Value, languageCode);
                if (tmdbSeries != null)
                {
                    // Update series fields from the detail response (same API call, no extra request)
                    if (string.IsNullOrEmpty(source.Cast) && tmdbSeries.Credits?.Cast != null)
                    {
                        source.Cast = string.Join(", ", tmdbSeries.Credits.Cast.OrderBy(c => c.Order).Take(5).Select(c => c.Name));
                    }
                    if (string.IsNullOrEmpty(source.Director) && tmdbSeries.Credits?.Crew != null)
                    {
                        source.Director = tmdbSeries.Credits.Crew.FirstOrDefault(c => c.Job == "Director")?.Name;
                    }
                    if (string.IsNullOrEmpty(source.Plot) && !string.IsNullOrEmpty(tmdbSeries.Overview))
                    {
                        source.Plot = tmdbSeries.Overview;
                    }
                    if (!string.IsNullOrEmpty(tmdbSeries.PosterPath))
                    {
                        source.CoverUrl = $"https://image.tmdb.org/t/p/w500{tmdbSeries.PosterPath}";
                    }
                    if (string.IsNullOrEmpty(source.BackdropUrl) && !string.IsNullOrEmpty(tmdbSeries.BackdropPath))
                    {
                        source.BackdropUrl = $"https://image.tmdb.org/t/p/original{tmdbSeries.BackdropPath}";
                    }

                    // Content Rating (from same API call)
                    if (string.IsNullOrEmpty(source.ContentRating) && tmdbSeries.ContentRatings?.Results != null)
                    {
                        var usRating = tmdbSeries.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "US")?.Rating;
                        var trRating = tmdbSeries.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "TR")?.Rating;
                        source.ContentRating = trRating ?? usRating;
                    }

                    // Trailer (from same API call — videos included via append_to_response)
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

                    // Genres
                    if (string.IsNullOrEmpty(source.Genre) && tmdbSeries.Genres != null && tmdbSeries.Genres.Count > 0)
                    {
                        source.Genre = string.Join(", ", tmdbSeries.Genres.Select(g => g.Name));
                    }

                    // Network (Netflix, HBO, Disney+, etc. — from same API call)
                    if (string.IsNullOrEmpty(source.NetworkName) && tmdbSeries.Networks != null && tmdbSeries.Networks.Count > 0)
                    {
                        var network = tmdbSeries.Networks[0];
                        source.NetworkName = network.Name;
                        if (!string.IsNullOrEmpty(network.LogoPath))
                            source.NetworkLogoUrl = $"https://image.tmdb.org/t/p/h50{network.LogoPath}";
                    }
                }

                // 2. Fetch Season and Episode details
                bool changesMade = false;
                foreach (var season in source.Seasons)
                {
                    if (season.SeasonNumber <= 0) continue;

                    var tmdbSeason = await _metadataService.FetchSeasonDetailsAsync(source.TmdbId.Value, season.SeasonNumber, languageCode);
                    if (tmdbSeason != null)
                    {
                        season.TmdbSeasonId = tmdbSeason.Id;
                        if (!string.IsNullOrEmpty(tmdbSeason.PosterPath))
                        {
                            season.CoverUrl = $"https://image.tmdb.org/t/p/w500{tmdbSeason.PosterPath}";
                        }
                        if (string.IsNullOrEmpty(season.Plot))
                        {
                            season.Plot = tmdbSeason.Overview;
                        }

                        // Update Episodes
                        foreach (var episode in season.Episodes)
                        {
                            var tmdbEp = tmdbSeason.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode.EpisodeNumber);
                            if (tmdbEp != null)
                            {
                                if (!string.IsNullOrEmpty(tmdbEp.StillPath))
                                {
                                    episode.CoverUrl = $"https://image.tmdb.org/t/p/w500{tmdbEp.StillPath}";
                                }
                                if (string.IsNullOrEmpty(episode.Plot))
                                {
                                    episode.Plot = tmdbEp.Overview;
                                }
                                if (string.IsNullOrEmpty(episode.TmdbEpisodeName) && !string.IsNullOrEmpty(tmdbEp.Name))
                                {
                                    episode.TmdbEpisodeName = tmdbEp.Name;
                                }
                                // Runtime (from same API call — no extra request)
                                if (tmdbEp.Runtime.HasValue && tmdbEp.Runtime.Value > 0 && !episode.Duration.HasValue)
                                {
                                    episode.Duration = TimeSpan.FromMinutes(tmdbEp.Runtime.Value);
                                }
                                // Air Date (from same API call — no extra request)
                                if (!string.IsNullOrEmpty(tmdbEp.AirDate) && !episode.AirDate.HasValue)
                                {
                                    if (DateTime.TryParse(tmdbEp.AirDate, out var airDate))
                                        episode.AirDate = airDate;
                                }
                            }
                        }

                        changesMade = true;
                    }
                }

                // 3. Mark as fetched and Save
                if (changesMade && dbSeries != null)
                {
                    source.MetadataFetchedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lazy load TMDB fetch failed for Series {Name}", source.Name);
            }
        }
        // ----------------------------------------------------

        await ApplyProfileProgressAsync(source, db);
        return source;
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

    private Channel BuildSeriesEpisodeChannel(Episode episode)
    {
        var matchedChannel = Channels.FirstOrDefault(c =>
            c.Type == ChannelType.Series &&
            !string.IsNullOrWhiteSpace(c.StreamUrl) &&
            string.Equals(c.StreamUrl, episode.StreamUrl, StringComparison.OrdinalIgnoreCase));

        if (matchedChannel != null)
        {
            return matchedChannel;
        }

        return new Channel
        {
            Id = 0,
            Name = episode.Name,
            StreamUrl = episode.StreamUrl,
            LogoUrl = episode.CoverUrl,
            Type = ChannelType.Series,
            PlaylistId = SelectedPlaylist?.Id ?? 0
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
        candidates.AddRange(LatestSeries);

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
            OnPropertyChanged(nameof(LatestSeries));
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
        candidates.AddRange(LatestSeries);

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
        candidates.AddRange(LatestSeries);

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

    private void SetItems<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        if (items == null) return;
        var list = items.ToList();
        _dispatcherService.Invoke(() =>
        {
            collection.Clear();
            foreach (var item in list)
            {
                collection.Add(item);
            }
        });
    }
}




