using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using System.Net.Http;
using System.Text.Json;
using Noctra.Services.Interfaces;
using Noctra.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;

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

/// <summary>
/// Ana sayfa view model
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const int IncrementalPageSize = 50;
    private const double LoadMoreThreshold = 0.8;

    private readonly IDispatcherService _dispatcherService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;
    private readonly IMetadataService _metadataService;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly ILogger<MainViewModel>? _logger;
    private readonly IChannelService _channelService;
    private readonly IMediaService _mediaService;
    private readonly IEpgService _epgService;
    private readonly IPlaylistService _playlistService;
    private readonly IWatchHistoryService _watchHistoryService;
    private readonly IXtreamCodesService _xtreamCodesService;
    private readonly IStalkerPortalService _stalkerPortalService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly DateTime _downloadCenterSessionStartUtc = DateTime.UtcNow;

    [ObservableProperty]
    private AppView _activeView = AppView.Home;

    [ObservableProperty]
    private List<Channel> _trendingChannels = new();

    [ObservableProperty]
    private List<Channel> _continueWatching = new();

    [ObservableProperty]
    private List<Channel> _latestMovies = new();

    [ObservableProperty]
    private List<Series> _latestSeries = new();

    [ObservableProperty]
    private List<Series> _seriesViewItems = new();

    [ObservableProperty]
    private Channel? _featuredChannel;

    [ObservableProperty]
    private List<Playlist> _playlists = new();

    [ObservableProperty]
    private List<Channel> _channels = new();

    [ObservableProperty]
    private List<Channel> _filteredChannels = new();

    [ObservableProperty]
    private List<string> _groups = new();

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
    private string _selectedSeriesOverview = string.Empty;

    [ObservableProperty]
    private string _selectedSeriesCast = string.Empty;

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
    private bool _isSearchOverlayVisible;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private List<object> _searchResults = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _showOnlyFavorites;

    [ObservableProperty]
    private string _statusMessage = "Hazır";

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
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        IContentDownloadService contentDownloadService,
        IMetadataService metadataService,
        IDispatcherService dispatcherService,
        WatermarkViewModel watermarkViewModel,
        IChannelService channelService,
        IMediaService mediaService,
        IEpgService epgService,
        IPlaylistService playlistService,
        IWatchHistoryService watchHistoryService,
        IXtreamCodesService xtreamCodesService,
        IStalkerPortalService stalkerPortalService,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MainViewModel>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _scopeFactory = serviceProvider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        _settingsService = settingsService;
        _contentDownloadService = contentDownloadService;
        _metadataService = metadataService;
        _dispatcherService = dispatcherService;
        _logger = logger;
        WatermarkViewModel = watermarkViewModel;
        _channelService = channelService;
        _mediaService = mediaService;
        _epgService = epgService;
        _playlistService = playlistService;
        _watchHistoryService = watchHistoryService;
        _xtreamCodesService = xtreamCodesService;
        _stalkerPortalService = stalkerPortalService;
        _contextFactory = contextFactory;
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

    [ObservableProperty]
    private int? _currentProfileId;

    [ObservableProperty]
    private Profile? _currentProfile;

    public async Task LoadProfileAsync(Profile profile)
    {
        if (profile == null) return;

        IsLoading = true;
        StatusMessage = $"{profile.Name} yükleniyor...";
        CurrentProfileId = profile.Id;
        CurrentProfile = profile;
        
        try
        {
            // Ensure provider account is loaded
            if (profile.ProviderAccount == null)
            {
                StatusMessage = "Hesap bilgileri yüklenemedi";
                return;
            }

            // Check if playlist already exists (cache-first approach)
            var existingPlaylists = await _playlistService.GetAllAsync(profile.Id);
            
            if (existingPlaylists.Count > 0)
            {
                // Use cached playlist - much faster!
                _logger?.LogDebug($"[MainViewModel] Using cached playlist for profile {profile.Id}");
                StatusMessage = "Önbellekten yükleniyor...";
                await LoadPlaylistsAsync();
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
                        StatusMessage = "Kanal listesi indiriliyor...";
                        await _playlistService.AddFromUrlAsync(profile.Name, m3uUrl, profile.Id);
                        await LoadPlaylistsAsync();
                        break;
                    }
                    case ProfileType.XtreamCodes:
                    {
                        StatusMessage = "Xtream bağlantısı kuruluyor...";
                        var baseUrl = profile.ProviderAccount.Url.TrimEnd('/');
                        if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;

                        _ = CheckXtreamExpirationAsync(profile.ProviderAccount);
                        var username = profile.ProviderAccount.Username ?? string.Empty;
                        var password = profile.ProviderAccount.Password ?? string.Empty;

                        try
                        {
                            StatusMessage = "Xtream API'den kanallar alınıyor...";
                            var xtreamChannels = await _xtreamCodesService.GetChannelsAsync(
                                baseUrl,
                                username,
                                password,
                                includeSeriesEpisodes: true);

                            var sourceUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&type=m3u_plus&output=ts";
                            await _playlistService.AddFromChannelsAsync(profile.Name, sourceUrl, xtreamChannels, profile.Id);
                            await LoadPlaylistsAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug($"[MainViewModel] Xtream API fallback to M3U: {ex.Message}");
                            var fallbackM3uUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&type=m3u_plus&output=ts";
                            StatusMessage = "Kanal listesi indiriliyor...";
                            await _playlistService.AddFromUrlAsync(profile.Name, fallbackM3uUrl, profile.Id);
                            await LoadPlaylistsAsync();
                        }

                        break;
                    }
                    case ProfileType.StalkerPortal:
                    {
                        StatusMessage = "Stalker Portal bağlantısı kuruluyor...";
                        var portalUrl = profile.ProviderAccount.Url;
                        var macAddress = profile.ProviderAccount.Username ?? string.Empty;

                        var stalkerChannels = await _stalkerPortalService.GetChannelsAsync(
                            portalUrl,
                            macAddress,
                            includeVod: true);

                        var sourceUrl = $"{portalUrl.TrimEnd('/')}/stalker_portal#{macAddress}";
                        await _playlistService.AddFromChannelsAsync(profile.Name, sourceUrl, stalkerChannels, profile.Id);
                        await LoadPlaylistsAsync();
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

    private async Task CheckM3UExpirationAsync(ProviderAccount account)
    {
        try
        {
            // Try to find username and password in URL
            var uri = new Uri(account.Url);
            var query = QueryHelpers.ParseQuery(uri.Query);

            // Some providers append expiry directly in M3U URL query.
            var queryExpiration = TryParseExpirationFromQuery(query);
            if (queryExpiration.HasValue)
            {
                await UpdateProviderExpirationAsync(account.Id, queryExpiration.Value);
            }
            
            string? username = null;
            string? password = null;

            if (query.TryGetValue("username", out var u)) username = u.ToString();
            if (query.TryGetValue("password", out var p)) password = p.ToString();

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                // Construct base URL (scheme + host + port)
                var baseUrl = $"{uri.Scheme}://{uri.Host}";
                if (!uri.IsDefaultPort) baseUrl += $":{uri.Port}";
                
                await CheckExpirationInternalAsync(account.Id, baseUrl, username, password);
                
                // Update local model with found credentials if missing? 
                // Maybe not strictly necessary to overwrite, but good for display
                if (string.IsNullOrEmpty(account.Username))
                {
                     account.Username = username;
                     // We don't save this change to DB here to avoid changing user input, 
                     // but we could if we wanted to convert it to a proper Xtream account in the future.
                }
            }
        }
        catch { /* Parsing failed, not an Xtream URL */ }
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

    private static DateTime? TryParseExpirationFromQuery(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query)
    {
        var candidateKeys = new[] { "exp", "expires", "expiry", "expiration", "expire", "exp_date" };
        foreach (var key in candidateKeys)
        {
            if (!query.TryGetValue(key, out var value))
            {
                continue;
            }

            var raw = value.ToString();
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
        await CheckExpirationInternalAsync(account.Id, baseUrl, account.Username, account.Password);
    }

    private async Task CheckExpirationInternalAsync(int accountId, string baseUrl, string? username, string? password)
    {
        try
        {
            var apiUrl = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username ?? "")}&password={Uri.EscapeDataString(password ?? "")}";
            _logger?.LogDebug($"[CheckExpiration] Checking: {apiUrl}");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var response = await client.GetAsync(apiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogDebug($"[CheckExpiration] Failed with status: {response.StatusCode}");
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
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"CheckExpiration Error: {ex}");
        }
    }

    [RelayCommand]
    private async Task LoadPlaylistsAsync()
    {
        try
        {
            IsLoading = true;
            Playlists = await _playlistService.GetAllAsync(CurrentProfileId);
            
            if (Playlists.Count > 0 && SelectedPlaylist == null)
            {
                SelectedPlaylist = Playlists[0];
            }
            else if (Playlists.Count == 0)
            {
                Channels = new List<Channel>();
                FilteredChannels = new List<Channel>();
            }
        }
        finally
        {
            IsLoading = false;
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
        using var scope = _scopeFactory.CreateScope();
        var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
        
        try
        {
            IsLoading = true;
            StatusMessage = "Kanallar yükleniyor...";
            
            // Same DbContext cannot execute multiple operations in parallel.
            var allGroups = await playlistService.GetGroupsAsync(playlistId);
            var liveGroups = await playlistService.GetGroupsByTypeAsync(playlistId, ChannelType.Live);
            var vodGroups = await playlistService.GetGroupsByTypeAsync(playlistId, ChannelType.VOD);
            var seriesGroups = await playlistService.GetGroupsByTypeAsync(playlistId, ChannelType.Series);
            var channelCount = await playlistService.GetChannelCountAsync(playlistId);
            _allGroupsCache = OrderGroupsByLanguagePreference(allGroups);
            _liveGroupsCache = OrderGroupsByLanguagePreference(liveGroups);
            _vodGroupsCache = OrderGroupsByLanguagePreference(vodGroups);
            _seriesGroupsCache = OrderGroupsByLanguagePreference(seriesGroups);
            UpdateGroupsForSelectedType();
            ResetIncrementalState();
            await LoadMoreChannelsAsync();

            StatusMessage = $"{channelCount} kanal hazır";

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
            IsLoading = false;
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
        using var scope = _scopeFactory.CreateScope();
        var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        
        // Rail içeriklerini yükle
        TrendingChannels = Channels
            .Where(c => c.Type == ChannelType.Live)
            .OrderByDescending(HasDisplayImage)
            .ThenBy(c => c.Name)
            .Take(10)
            .ToList();

        LatestMovies = Channels
            .Where(c => c.Type == ChannelType.VOD)
            .OrderByDescending(HasDisplayImage)
            .ThenByDescending(c => c.Id)
            .Take(10)
            .ToList();

        var playlistId = SelectedPlaylist?.Id ?? 0;
        LatestSeries = await mediaService.GetSeriesAsync(playlistId);
        UpdateSeriesViewItems();

        ContinueWatching = Channels
            .Where(c => c.LastWatched.HasValue)
            .OrderByDescending(c => c.LastWatched)
            .Take(10)
            .ToList();

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
    private bool _seriesDetailDownloadedOnlyMode;

    public bool IsDownloadedSeriesDetailMode => _seriesDetailDownloadedOnlyMode;

    private void ResetIncrementalState()
    {
        _currentPage = 0;
        _hasMoreChannels = true;
        _isLoadingMoreChannels = false;
        Channels = new List<Channel>();
        FilteredChannels = new List<Channel>();
    }

    private void ResetSeriesIncrementalState()
    {
        _currentSeriesPage = 0;
        _hasMoreSeriesItems = true;
        _isLoadingMoreSeriesItems = false;
        _seriesFilteredSource = new List<Series>();
        SeriesViewItems = new List<Series>();
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
            using var scope = _scopeFactory.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
            var hasSearch = !string.IsNullOrWhiteSpace(SearchText);
            var effectiveGroup = hasSearch ? null : SelectedGroup;
            var effectiveType = hasSearch ? null : SelectedChannelType;

            var page = await playlistService.GetChannelsFilteredPageAsync(
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
                var fallbackPage = await playlistService.GetChannelsFilteredPageAsync(
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

            var merged = new List<Channel>(FilteredChannels.Count + page.Count);
            merged.AddRange(FilteredChannels);
            merged.AddRange(page);

            Channels = merged;
            FilteredChannels = merged;

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

            var merged = new List<Series>(SeriesViewItems.Count + page.Count);
            merged.AddRange(SeriesViewItems);
            merged.AddRange(page);
            SeriesViewItems = merged;
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

        ScheduleImmediateFilter();
    }

    partial void OnSelectedChannelTypeChanged(ChannelType? value)
    {
        UpdateGroupsForSelectedType();
        ScheduleImmediateFilter();
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

        Groups = nextGroups.ToList();

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
            || normalized.StartsWith(countryCode + " |", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(countryCode, StringComparison.OrdinalIgnoreCase);
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

        IsLoading = true;

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
            IsLoading = false;
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
        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();

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
                    var groupChannels = Channels.Where(c => c.GroupTitle == channel.GroupTitle && c.Type == ChannelType.Live);
                    _livePlaybackContext = SelectedSortOrder switch
                    {
                        ChannelSortOrder.NameAsc => groupChannels.OrderBy(c => c.Name).ToList(),
                        ChannelSortOrder.NameDesc => groupChannels.OrderByDescending(c => c.Name).ToList(),
                        ChannelSortOrder.OldestFirst => groupChannels.OrderBy(c => c.Id).ToList(),
                        _ => groupChannels.OrderByDescending(c => c.Id).ToList() // ChannelSortOrder.NewestFirst
                    };
                }
            }
            else
            {
                _livePlaybackContext = null;
            }
        }
        
        SelectedChannel = channel;
        StatusMessage = $"Seçildi: {channel.Name}";
        
        // Update last watched
        channel.LastWatched = DateTime.UtcNow;
        _ = channelService.UpdateChannelAsync(channel);
        UpdateHistoryChannels();

        // Notify UI to play
        OnMediaSelected?.Invoke(channel);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(object media)
    {
        if (!CurrentProfileId.HasValue)
        {
            StatusMessage = "Önce bir profil seçmelisiniz";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            await channelService.UpdateChannelAsync(channel);
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
            using var scope = _scopeFactory.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
            
            IsLoading = true;
            StatusMessage = "Playlist ekleniyor...";
            
            var playlist = await playlistService.AddFromUrlAsync(NewPlaylistName, NewPlaylistUrl, CurrentProfileId);
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
        if (SelectedPlaylist == null) return;

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
            using var scope = _scopeFactory.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();

            if (!isBackground)
            {
                IsLoading = true;
                StatusMessage = "Kanal listesi güncelleniyor...";
            }

            var beforeCount = await playlistService.GetChannelCountAsync(SelectedPlaylist.Id);
            await playlistService.RefreshAsync(SelectedPlaylist.Id);
            var afterCount = await playlistService.GetChannelCountAsync(SelectedPlaylist.Id);
            var addedCount = Math.Max(0, afterCount - beforeCount);

            if (addedCount > 0)
            {
                await LoadChannelsAsync(SelectedPlaylist.Id);
            }

            if (!isBackground)
            {
                StatusMessage = addedCount == 0
                    ? "Kanal listesi zaten guncel"
                    : $"Kanal listesi guncellendi ({addedCount} yeni kanal eklendi)";

                if (addedCount == 0)
                {
                    _playlistNoChangeUntilUtc[SelectedPlaylist.Id] = DateTime.UtcNow.AddMinutes(2);
                }
                else
                {
                    _playlistNoChangeUntilUtc.Remove(SelectedPlaylist.Id);
                }
            }
        }
        catch (Exception ex)
        {
            if (!isBackground)
            {
                StatusMessage = UserFriendlyErrorMessage.WithPrefix("Kanal listesi guncelleme hatasi", ex);
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
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
            if (!isBackgroundSync && setBusyState)
            {
                IsLoading = true;
                StatusMessage = "EPG güncelleniyor...";
            }

            using var scope = _scopeFactory.CreateScope();
            var epgService = scope.ServiceProvider.GetRequiredService<IEpgService>();
            var languageDetection = scope.ServiceProvider.GetRequiredService<Services.LanguageDetectionService>();
            var epgSourceResolver = scope.ServiceProvider.GetRequiredService<Services.EpgSourceResolver>();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Use full playlist channels for EPG mapping (not only currently paged UI channels)
            var channelsForMapping = Channels;
            if (SelectedPlaylist != null)
            {
                channelsForMapping = await playlistService.GetChannelsAsync(SelectedPlaylist.Id);
            }

            // EPG is relevant for live channels only.
            channelsForMapping = channelsForMapping
                .Where(c => c.Type == ChannelType.Live)
                .ToList();

            if (channelsForMapping.Count == 0)
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
            var hasCachedPrograms = await HasEpgForChannelsAsync(db, channelsForMapping);
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
                providerEpgUrl = $"{baseUrl}/xmltv.php?username={Uri.EscapeDataString(CurrentProfile.ProviderAccount.Username ?? "")}&password={Uri.EscapeDataString(CurrentProfile.ProviderAccount.Password ?? "")}";
            }

            // 2. Çoklu ülke tespiti (Loop through top countries)
            var channelNames = channelsForMapping.Select(c => c.Name ?? "").ToList();
            // Detect top countries (limit to top 3 to avoid excessive downloads)
            var detectedCountries = languageDetection.DetectCountries(channelNames)
                .Where(c => c.Percentage > 20 || c.ChannelCount > 20) // Min threshold — prevents downloading huge EPG files for marginal matches
                .Take(3)
                .ToList();

            if (detectedCountries.Count == 0)
                detectedCountries.Add(("TR", 0, 0));

            _logger?.LogDebug($"[MainViewModel] Detected countries: {string.Join(", ", detectedCountries.Select(c => c.CountryCode))}");

            // 3. EPG kaynaklarını topla
            var countryCodes = detectedCountries.Select(c => c.CountryCode).ToList();
            var playlistEpgUrl = (SelectedPlaylist?.EpgUrl ?? string.Empty).Trim();
            var customEpgUrl = (_settingsService.Settings.CustomEpgUrl ?? string.Empty).Trim();
            
            var hasUsableTvgIds = channelsForMapping.Any(c => !string.IsNullOrWhiteSpace(c.TvgId));
            
            var epgSources = epgSourceResolver.ResolveEpgSources(
                countryCodes, 
                providerEpgUrl, 
                playlistEpgUrl, 
                Uri.TryCreate(customEpgUrl, UriKind.Absolute, out _) ? customEpgUrl : null,
                hasUsableTvgIds);

            if (!isBackgroundSync)
            {
                StatusMessage = "EPG kaynakları deneniyor...";
            }

            // 4. Her kaynağı indirmeyi dene
            bool anySuccess = false;
            string? lastSourceError = null;
            string? successfulSourceUrl = null;
            
            foreach (var source in epgSources)
            {
                try
                {
                    
                    if (!isBackgroundSync)
                    {
                        StatusMessage = $"EPG: {source.Type} yükleniyor...";
                    }

                    // Eğer bu kaynak için temizlik gerekiyorsa
                    if (source.ClearBeforeLoad)
                    {
                        _logger?.LogDebug("[MainViewModel] Clearing existing EPG data...");
                        await epgService.ClearEpgAsync();
                    }

                    var beforeCount = await epgService.GetTotalProgramCountAsync();
                    await epgService.LoadEpgAsync(source.Url, source.IsPrimary, channelsForMapping, daysAhead: 1);
                    var afterCount = await epgService.GetTotalProgramCountAsync();
                    var loadedPrograms = afterCount - beforeCount;

                    if (loadedPrograms > 0)
                    {
                        anySuccess = true;
                        successfulSourceUrl = source.Url;
                        _logger?.LogDebug($"[MainViewModel] EPG loaded from {source.Type} ({loadedPrograms} programs) - URL: {source.Url}");
                        lastSourceError = null;
                    }
                    else
                    {
                        // Don't overwrite last error if we already had success
                        if (!anySuccess) 
                        {
                            lastSourceError = $"{source.Type}: 0 program";
                        }
                        _logger?.LogDebug($"[MainViewModel] EPG source returned 0 programs: {source.Type}");
                    }
                    
                    // Do NOT break here; continue to load other countries/sources
                }
                catch (Exception ex)
                {
                    lastSourceError = $"{source.Type}: {UserFriendlyErrorMessage.FromException(ex)}";
                    _logger?.LogDebug($"[MainViewModel] EPG source failed: {source.Type} - {ex.Message}");
                }
            }

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

    private async Task PersistSelectedPlaylistEpgErrorAsync(string error)
    {
        if (SelectedPlaylist == null || string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
    private List<object> _myList = new();

    [ObservableProperty]
    private List<object> _favoriteChannels = new();

    [ObservableProperty]
    private List<Channel> _historyChannels = new();

    [ObservableProperty]
    private List<Channel> _historyLiveChannels = new();

    [ObservableProperty]
    private List<Channel> _historySeriesChannels = new();

    [ObservableProperty]
    private List<Channel> _historyVodChannels = new();

    [ObservableProperty]
    private List<Series> _downloadedSeriesItems = new();

    [ObservableProperty]
    private List<Channel> _downloadedVodChannels = new();

    [ObservableProperty]
    private List<DownloadItem> _activeDownloadItems = new();

    [ObservableProperty]
    private List<DownloadItem> _activeDownloadingItems = new();

    [ObservableProperty]
    private List<DownloadItem> _queuedDownloadItems = new();

    [ObservableProperty]
    private List<DownloadItem> _completedDownloadItems = new();

    [ObservableProperty]
    private int _activeDownloadCount;

    [ObservableProperty]
    private string _activeDownloadsTotalSpeedText = "0 B/sn";

    [ObservableProperty]
    private string _downloadFreeDiskSpaceText = "-";

    [ObservableProperty]
    private List<Channel> _searchLiveChannels = new();

    [ObservableProperty]
    private List<Series> _searchSeriesChannels = new();


    [ObservableProperty]
    private List<Channel> _searchVodChannels = new();

    [ObservableProperty]
    private string _searchSuggestion = string.Empty;

    [ObservableProperty]
    private List<Channel> _searchSimilarLiveChannels = new();

    [ObservableProperty]
    private List<Series> _searchSimilarSeriesChannels = new();

    [ObservableProperty]
    private List<Channel> _searchSimilarVodChannels = new();

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

    public bool ShowDownloadsLandingEmptyState => !IsDownloadCenterVisible && ShowDownloadsEmptyState;

    [RelayCommand]
    private void Navigate(AppView view)
    {
        IsSearchOverlayVisible = false;
        SearchQuery = string.Empty;
        SearchResults = new List<object>();

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
                MyList = new List<object>();
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
                FavoriteChannels = new List<object>();
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
            var latestSeriesSnapshot = LatestSeries?.ToList() ?? new List<Series>();
            var seriesViewSnapshot = SeriesViewItems?.ToList() ?? new List<Series>();

            var list = new List<object>();
            list.AddRange(channelsSnapshot
                .Where(c => c.Type != ChannelType.Series && c.IsInMyList)
                .Cast<object>());

            var seriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            foreach (var series in latestSeriesSnapshot.Concat(seriesViewSnapshot))
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
            }

            list.AddRange(seriesMap.Values.Where(s => s.IsInMyList).Cast<object>());
            MyList = list
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty)
                .ToList();
            ShowMyListEmptyState = MyList.Count == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"UpdateMyList failed: {ex}");
            MyList = new List<object>();
            ShowMyListEmptyState = true;
        }
    }

    private void UpdateFavoriteChannels()
    {
        try
        {
            var channelsSnapshot = Channels?.ToList() ?? new List<Channel>();
            var latestSeriesSnapshot = LatestSeries?.ToList() ?? new List<Series>();
            var seriesViewSnapshot = SeriesViewItems?.ToList() ?? new List<Series>();

            var list = new List<object>();
            list.AddRange(channelsSnapshot
                .Where(c => c.Type != ChannelType.Series && c.IsFavorite)
                .Cast<object>());

            var seriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            foreach (var series in latestSeriesSnapshot.Concat(seriesViewSnapshot))
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
            }

            list.AddRange(seriesMap.Values.Where(s => s.IsFavorite).Cast<object>());
            FavoriteChannels = list
                .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty)
                .ToList();
            ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"UpdateFavoriteChannels failed: {ex}");
            FavoriteChannels = new List<object>();
            ShowFavoritesEmptyState = true;
        }
    }

    private void UpdateHistoryChannels()
    {
        HistoryChannels = Channels
            .Where(c => c.LastWatched.HasValue)
            .OrderByDescending(c => c.LastWatched)
            .ToList();
        UpdateHistoryBuckets();

        _ = RefreshHistoryChannelsOnlyAsync();
    }

    private void UpdateHistoryBuckets()
    {
        HistoryLiveChannels = HistoryChannels.Where(c => c.Type == ChannelType.Live).ToList();
        HistorySeriesChannels = HistoryChannels.Where(c => c.Type == ChannelType.Series).ToList();
        HistoryVodChannels = HistoryChannels.Where(c => c.Type == ChannelType.VOD).ToList();
        ShowHistoryEmptyState = HistoryChannels.Count == 0;
    }

    private void UpdateDownloadedItems()
    {
        try
        {
            var channelsSnapshot = Channels?.ToList() ?? new List<Channel>();
            var latestSeriesSnapshot = LatestSeries?.ToList() ?? new List<Series>();
            var seriesViewSnapshot = SeriesViewItems?.ToList() ?? new List<Series>();

            DownloadedVodChannels = channelsSnapshot
                .Where(c => c.Type == ChannelType.VOD && IsDownloadedStreamUrl(c.StreamUrl))
                .OrderBy(c => c.Name)
                .ToList();

            var seriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            foreach (var series in latestSeriesSnapshot.Concat(seriesViewSnapshot))
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
            }

            DownloadedSeriesItems = seriesMap.Values
                .Where(SeriesHasDownloadedEpisode)
                .Select(BuildDownloadedOnlySeries)
                .OrderBy(s => s.Name)
                .ToList();

            ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                      DownloadedSeriesItems.Count == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"UpdateDownloadedItems failed: {ex}");
            DownloadedVodChannels = new List<Channel>();
            DownloadedSeriesItems = new List<Series>();
            ActiveDownloadItems = new List<DownloadItem>();
            ShowDownloadsEmptyState = true;
        }
    }

    private async Task RefreshDownloadedItemsFromDatabaseAsync()
    {
        if (!CurrentProfileId.HasValue)
        {
            DownloadedVodChannels = new List<Channel>();
            DownloadedSeriesItems = new List<Series>();
            ActiveDownloadItems = new List<DownloadItem>();
            ActiveDownloadingItems = new List<DownloadItem>();
            QueuedDownloadItems = new List<DownloadItem>();
            SetDownloadCenterSummaryEmpty();
            ShowDownloadsEmptyState = true;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);

        if (profilePlaylistIds.Count == 0)
        {
            DownloadedVodChannels = new List<Channel>();
            DownloadedSeriesItems = new List<Series>();
            await RefreshDownloadsFromServiceAsync(CurrentProfileId.Value);
            ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                     DownloadedSeriesItems.Count == 0;
            return;
        }

        var vodChannels = await db.Channels
            .AsNoTracking()
            .Where(c => profilePlaylistIds.Contains(c.PlaylistId) && c.Type == ChannelType.VOD)
            .OrderBy(c => c.Name)
            .ToListAsync();

        DownloadedVodChannels = vodChannels
            .Where(c => IsDownloadedStreamUrl(c.StreamUrl))
            .ToList();

        var existingDownloadedVodUrls = new HashSet<string>(
            DownloadedVodChannels
                .Select(c => c.StreamUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url)),
            StringComparer.OrdinalIgnoreCase);

        var completedVodDownloads = await db.DownloadItems
            .AsNoTracking()
            .Where(d => d.ProfileId == CurrentProfileId.Value &&
                        d.ChannelType == ChannelType.VOD &&
                        d.Status == DownloadStatus.Completed &&
                        !string.IsNullOrWhiteSpace(d.LocalEncryptedPath))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();

        var fallbackVod = new List<Channel>();
        foreach (var item in completedVodDownloads)
        {
            if (string.IsNullOrWhiteSpace(item.LocalEncryptedPath) || !File.Exists(item.LocalEncryptedPath))
            {
                continue;
            }

            if (existingDownloadedVodUrls.Contains(item.LocalEncryptedPath))
            {
                continue;
            }

            fallbackVod.Add(new Channel
            {
                Name = string.IsNullOrWhiteSpace(item.DisplayName) ? "VOD" : item.DisplayName,
                StreamUrl = item.LocalEncryptedPath,
                LogoUrl = item.PosterUrl,
                BackdropUrl = item.PosterUrl,
                Type = ChannelType.VOD,
                PlaylistId = item.PlaylistId
            });
        }

        if (fallbackVod.Count > 0)
        {
            DownloadedVodChannels = DownloadedVodChannels
                .Concat(fallbackVod)
                .GroupBy(c => c.StreamUrl ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Name)
                .ToList();
        }

        var seriesCandidates = await db.Series
            .AsNoTracking()
            .Where(s => profilePlaylistIds.Contains(s.PlaylistId))
            .Include(s => s.Seasons)
                .ThenInclude(sn => sn.Episodes)
            .OrderBy(s => s.Name)
            .ToListAsync();

        DownloadedSeriesItems = seriesCandidates
            .Where(SeriesHasDownloadedEpisode)
            .Select(BuildDownloadedOnlySeries)
            .ToList();

        var existingDownloadedEpisodeUrls = new HashSet<string>(
            DownloadedSeriesItems
                .SelectMany(s => s.Seasons)
                .SelectMany(sn => sn.Episodes)
                .Select(ep => ep.StreamUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url)),
            StringComparer.OrdinalIgnoreCase);

        var completedSeriesDownloads = await db.DownloadItems
            .AsNoTracking()
            .Where(d => d.ProfileId == CurrentProfileId.Value &&
                        d.ChannelType == ChannelType.Series &&
                        d.Status == DownloadStatus.Completed &&
                        !string.IsNullOrWhiteSpace(d.LocalEncryptedPath))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();

        var fallbackSeriesMap = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in completedSeriesDownloads)
        {
            if (string.IsNullOrWhiteSpace(item.LocalEncryptedPath) || !File.Exists(item.LocalEncryptedPath))
            {
                continue;
            }

            if (existingDownloadedEpisodeUrls.Contains(item.LocalEncryptedPath))
            {
                continue;
            }

            var seriesName = ExtractSeriesBaseName(item.DisplayName);
            var seriesKey = $"{item.PlaylistId}|{NormalizeFuzzyText(seriesName)}";
            if (!fallbackSeriesMap.TryGetValue(seriesKey, out var series))
            {
                series = new Series
                {
                    Name = seriesName,
                    CoverUrl = item.PosterUrl,
                    PlaylistId = item.PlaylistId
                };
                fallbackSeriesMap[seriesKey] = series;
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

            if (season.Episodes.Any(e => string.Equals(e.StreamUrl, item.LocalEncryptedPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var episodeNumber = parsed.EpisodeNumber > 0 ? parsed.EpisodeNumber : season.Episodes.Count + 1;
            season.Episodes.Add(new Episode
            {
                EpisodeNumber = episodeNumber,
                Name = item.DisplayName,
                StreamUrl = item.LocalEncryptedPath,
                CoverUrl = item.PosterUrl
            });
        }

        if (fallbackSeriesMap.Count > 0)
        {
            foreach (var series in fallbackSeriesMap.Values)
            {
                series.Seasons = series.Seasons
                    .OrderBy(s => s.SeasonNumber)
                    .Select(s =>
                    {
                        s.Episodes = s.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
                        return s;
                    })
                    .ToList();
            }

            DownloadedSeriesItems = DownloadedSeriesItems
                .Concat(fallbackSeriesMap.Values)
                .OrderBy(s => s.Name)
                .ToList();
        }

        await RefreshDownloadsFromServiceAsync(CurrentProfileId.Value);
        ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                 DownloadedSeriesItems.Count == 0;
    }

    private async Task RefreshDownloadsFromServiceAsync(int profileId)
    {
        try
        {
            var downloads = await _contentDownloadService.GetDownloadsAsync(profileId);
            var allActive = downloads
                .Where(d => d.IsActive)
                .OrderByDescending(d => d.CreatedAt)
                .ToList();

            ActiveDownloadItems = allActive
                .Select((d, index) =>
                {
                    d.QueueOrder = index + 1;
                    return d;
                })
                .ToList();

            ActiveDownloadingItems = allActive
                .Where(d => d.Status == DownloadStatus.Downloading || d.Status == DownloadStatus.Paused)
                .OrderBy(d => d.Status == DownloadStatus.Paused ? 1 : 0)
                .ThenBy(d => d.CreatedAt)
                .ToList();

            QueuedDownloadItems = allActive
                .Where(d => d.Status == DownloadStatus.Queued)
                .OrderBy(d => d.CreatedAt)
                .ToList();

            CompletedDownloadItems = downloads
                .Where(d => d.Status == DownloadStatus.Completed)
                .Where(d =>
                {
                    var ts = (d.CompletedAt ?? d.UpdatedAt).ToUniversalTime();
                    return ts >= _downloadCenterSessionStartUtc;
                })
                .OrderByDescending(d => d.CompletedAt ?? d.UpdatedAt)
                .Take(100)
                .ToList();
            UpdateDownloadCenterSummary(profileId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"RefreshDownloadsFromServiceAsync failed: {ex.Message}");
            ActiveDownloadItems = new List<DownloadItem>();
            ActiveDownloadingItems = new List<DownloadItem>();
            QueuedDownloadItems = new List<DownloadItem>();
            CompletedDownloadItems = new List<DownloadItem>();
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
        ActiveDownloadingItems = new List<DownloadItem>();
        QueuedDownloadItems = new List<DownloadItem>();
        CompletedDownloadItems = new List<DownloadItem>();
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
            var rootFallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Noctra",
                "Downloads");
            var configured = _settingsService.Settings.DownloadPath;
            var root = string.IsNullOrWhiteSpace(configured)
                ? rootFallback
                : configured.Trim().Trim('"');

            if (!Path.IsPathFullyQualified(root))
            {
                root = rootFallback;
            }

            var profilePath = Path.Combine(root, $"profile_{profileId}");
            Directory.CreateDirectory(profilePath);

            var driveRoot = Path.GetPathRoot(profilePath);
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
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
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
    private void ToggleDownloadCenter()
    {
        IsDownloadCenterVisible = !IsDownloadCenterVisible;
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
            await _contentDownloadService.ResumeDownloadAsync(item.Id);
        }
        else
        {
            await _contentDownloadService.PauseDownloadAsync(item.Id);
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

    private async Task RefreshPersonalListsFromDatabaseAsync()
    {
        if (!CurrentProfileId.HasValue)
        {
            MyList = new List<object>();
            FavoriteChannels = new List<object>();
            HistoryChannels = new List<Channel>();
            HistoryLiveChannels = new List<Channel>();
            HistorySeriesChannels = new List<Channel>();
            HistoryVodChannels = new List<Channel>();
            DownloadedSeriesItems = new List<Series>();
            DownloadedVodChannels = new List<Channel>();
            if (CurrentProfileId.HasValue)
            {
                await RefreshDownloadsFromServiceAsync(CurrentProfileId.Value);
            }
            else
            {
                ActiveDownloadItems = new List<DownloadItem>();
                SetDownloadCenterSummaryEmpty();
            }
            ShowMyListEmptyState = true;
            ShowFavoritesEmptyState = true;
            ShowHistoryEmptyState = true;
            ShowDownloadsEmptyState = DownloadedVodChannels.Count == 0 &&
                                     DownloadedSeriesItems.Count == 0;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watchHistoryService = scope.ServiceProvider.GetRequiredService<IWatchHistoryService>();
        await watchHistoryService.CleanupOlderThanDaysAsync(CurrentProfileId.Value, 7);
        var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);

        if (profilePlaylistIds.Count == 0)
        {
            MyList = new List<object>();
            FavoriteChannels = new List<object>();
            HistoryChannels = new List<Channel>();
            HistoryLiveChannels = new List<Channel>();
            HistorySeriesChannels = new List<Channel>();
            HistoryVodChannels = new List<Channel>();
            DownloadedSeriesItems = new List<Series>();
            DownloadedVodChannels = new List<Channel>();
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

        MyList = myListChannels
            .Cast<object>()
            .Concat(myListSeries.Cast<object>())
            .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty)
            .ToList();

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

        FavoriteChannels = favoriteChannels
            .Cast<object>()
            .Concat(favoriteSeries.Cast<object>())
            .OrderBy(item => item is Channel c ? c.Name : item is Series s ? s.Name : string.Empty)
            .ToList();

        HistoryChannels = await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds);
        UpdateHistoryBuckets();

        ShowMyListEmptyState = MyList.Count == 0;
        ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
        if (ActiveView == AppView.Downloads)
        {
            await RefreshDownloadedItemsFromDatabaseAsync();
        }
    }

    private async Task RefreshHistoryChannelsOnlyAsync()
    {
        if (!CurrentProfileId.HasValue)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watchHistoryService = scope.ServiceProvider.GetRequiredService<IWatchHistoryService>();
        await watchHistoryService.CleanupOlderThanDaysAsync(CurrentProfileId.Value, 7);
        var profilePlaylistIds = await GetProfilePlaylistIdsAsync(db, CurrentProfileId.Value);

        if (profilePlaylistIds.Count == 0)
        {
            HistoryChannels = new List<Channel>();
            HistoryLiveChannels = new List<Channel>();
            HistorySeriesChannels = new List<Channel>();
            HistoryVodChannels = new List<Channel>();
            ShowHistoryEmptyState = true;
            return;
        }

        HistoryChannels = await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds);
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
            SearchLiveChannels = new List<Channel>();
            SearchSeriesChannels = new List<Series>();
            SearchVodChannels = new List<Channel>();
            SearchSuggestion = string.Empty;
            SearchSimilarLiveChannels = new List<Channel>();
            SearchSimilarSeriesChannels = new List<Series>();
            SearchSimilarVodChannels = new List<Channel>();
            ShowSearchSimilarSection = false;
            ShowSearchEmptyState = false;
            return;
        }

        var rawQuery = SearchText.Trim();
        var normalizedSeriesQuery = NormalizeSeriesQuery(rawQuery);

        SearchLiveChannels = FilteredChannels
            .Where(c => c.Type == ChannelType.Live)
            .OrderByDescending(HasDisplayImage)
            .ThenBy(c => c.Name)
            .ToList();

        var seriesSnapshot = LatestSeries.ToList();

        SearchSeriesChannels = seriesSnapshot
            .Where(series => SeriesMatchesSearch(series, rawQuery, normalizedSeriesQuery))
            .OrderByDescending(HasDisplayImage)
            .ThenBy(series => series.Name)
            .ToList();

        SearchVodChannels = FilteredChannels
            .Where(c => c.Type == ChannelType.VOD)
            .OrderByDescending(HasDisplayImage)
            .ThenBy(c => c.Name)
            .ToList();

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
            SearchSimilarLiveChannels = new List<Channel>();
            SearchSimilarSeriesChannels = new List<Series>();
            SearchSimilarVodChannels = new List<Channel>();
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

        SearchSimilarLiveChannels = similarLive;
        SearchSimilarSeriesChannels = similarSeries;
        SearchSimilarVodChannels = similarVod;
        ShowSearchSimilarSection = similarLive.Count > 0 || similarSeries.Count > 0 || similarVod.Count > 0;
    }

    private static string ComputeBestSuggestion(string query, IEnumerable<string> candidates)
    {
        var normalizedQuery = NormalizeFuzzyText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return string.Empty;
        }

        string best = string.Empty;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalizedCandidate = NormalizeFuzzyText(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate) || normalizedCandidate == normalizedQuery)
            {
                continue;
            }

            var maxDistance = GetDistanceThreshold(Math.Max(normalizedQuery.Length, normalizedCandidate.Length));
            var distance = LevenshteinDistance(normalizedQuery, normalizedCandidate, maxDistance);
            if (distance < 0 || distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            best = candidate;
        }

        return bestDistance == int.MaxValue ? string.Empty : best;
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
        return LevenshteinDistance(normalizedQuery, normalizedCandidate, maxDistance) >= 0;
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

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];
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

    private void UpdateSeriesViewItems()
    {
        var source = LatestSeries ?? new List<Series>();
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
                var genre = series.Genre ?? string.Empty;
                groupOk = genre.Contains(selectedGroup, StringComparison.OrdinalIgnoreCase);
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
        SeriesViewItems = new List<Series>();
        _ = LoadMoreSeriesAsync();
    }

    [RelayCommand]
    private void OpenSearch()
    {
        IsSearchOverlayVisible = true;
        SearchQuery = string.Empty;
        SearchResults.Clear();
        // Notify view to focus
        OnPropertyChanged(nameof(SearchQuery)); // Just to trigger some UI logic if needed
    }

    [RelayCommand]
    private void CommitSearch()
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            // Sync with global search and navigate
            SearchText = SearchQuery;
            Navigate(AppView.Search);
            CloseSearch();
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

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOverlayVisible = false;
        SearchQuery = string.Empty;
        SearchResults.Clear();
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults = new List<object>();
            return;
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = SearchOverlayAsync(value, token);
    }

    private async Task SearchOverlayAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(_searchDelayMs, token);

            var channelsSnapshot = Channels;
            var seriesSnapshot = LatestSeries;
            var searchLower = query.ToLowerInvariant();

            var results = await Task.Run(() =>
            {
                var localResults = new List<object>();
                var normalizedSeriesQuery = NormalizeSeriesQuery(query);

                localResults.AddRange(channelsSnapshot.Where(c =>
                    c.Type != ChannelType.Series &&
                    (c.Name.ToLower().Contains(searchLower) ||
                     (c.GroupTitle?.ToLower().Contains(searchLower) ?? false)))
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
                    SearchResults = results;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected while typing quickly.
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

        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
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
            await channelService.UpdateChannelAsync(channel);
            StatusMessage = channel.IsInMyList ? "Listene eklendi" : "Listenden çıkarıldı";
        }
        else if (media is Series series)
        {
            series.IsInMyList = !series.IsInMyList;
            await mediaService.UpdateSeriesAsync(series);
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

        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
                await channelService.UpdateChannelAsync(channel);
            }
        }
        else if (media is Series series && series.IsInMyList)
        {
            series.IsInMyList = false;
            await mediaService.UpdateSeriesAsync(series);

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

        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
                await channelService.UpdateChannelAsync(channel);
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
            await mediaService.UpdateSeriesAsync(series);

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

    private async Task PlayEpisodeSafeAsync(Episode? episode)
    {
        try
        {
            await PlayEpisodeInternalAsync(episode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PlayEpisode failed.");
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Bolum oynatilamadi", ex);
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

        // Arama overlay açıkken seçim sonrası detay/oynatıcıyı kapatmasın diye önce overlay'i kapat.
        IsSearchOverlayVisible = false;
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
                            using var scope = _scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
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
            StatusMessage = $"Seçildi: {channel.Name}";
            OnMediaSelected?.Invoke(channel);
        }
        else if (media is Series series)
        {
            var selectedSeries = series;
            try
            {
                selectedSeries = await LoadSeriesWithProfileProgressAsync(series);
                EnsureSeriesEpisodes(selectedSeries);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"EnsureSeriesEpisodes failed: {ex.Message}");
            }

            _seriesDetailDownloadedOnlyMode = ActiveView == AppView.Downloads;
            OnPropertyChanged(nameof(IsDownloadedSeriesDetailMode));
            if (_seriesDetailDownloadedOnlyMode)
            {
                selectedSeries = BuildDownloadedOnlySeries(selectedSeries);
            }

            SelectedSeries = selectedSeries;
            IsSeriesDetailVisible = true;
            StatusMessage = $"Seçildi: {selectedSeries.Name}";
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
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.DownloadItems
                .AsNoTracking()
                .Where(d => d.Status == DownloadStatus.Completed &&
                            d.LocalEncryptedPath == streamUrl &&
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
        var seriesName = series.Name?.ToLowerInvariant() ?? string.Empty;
        var genre = series.Genre?.ToLowerInvariant() ?? string.Empty;
        var rawLower = rawQuery.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedQuery) && seriesName.Contains(normalizedQuery))
        {
            return true;
        }

        if (genre.Contains(rawLower) || (!string.IsNullOrWhiteSpace(normalizedQuery) && genre.Contains(normalizedQuery)))
        {
            return true;
        }

        var episodes = series.Seasons.SelectMany(s => s.Episodes);
        return episodes.Any(ep =>
        {
            var episodeName = ep.Name?.ToLowerInvariant() ?? string.Empty;
            return episodeName.Contains(rawLower) ||
                   (!string.IsNullOrWhiteSpace(normalizedQuery) && episodeName.Contains(normalizedQuery));
        });
    }

    private async Task LoadSelectedSeriesMetadataAsync(Series series)
    {
        try
        {
            IsSelectedSeriesMetadataLoading = true;

            SelectedSeriesPosterUrl = series.CoverUrl;
            SelectedSeriesBackdropUrl = null;
            SelectedSeriesOverview = series.Plot ?? string.Empty;
            SelectedSeriesCast = string.Empty;

            var metadata = await _metadataService.FetchMetadataAsync(series.Name, ChannelType.Series);
            if (metadata == null || SelectedSeries?.Id != series.Id)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(metadata.PosterUrl))
            {
                SelectedSeriesPosterUrl = metadata.PosterUrl;
            }

            if (!string.IsNullOrWhiteSpace(metadata.BackdropUrl))
            {
                SelectedSeriesBackdropUrl = metadata.BackdropUrl;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Description))
            {
                SelectedSeriesOverview = metadata.Description;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Cast))
            {
                SelectedSeriesCast = metadata.Cast;
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

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbSeries = await db.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.PlaylistId == SelectedPlaylist.Id &&
                (s.Id == series.Id || s.Name == series.Name));

        var source = dbSeries ?? series;
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
}




