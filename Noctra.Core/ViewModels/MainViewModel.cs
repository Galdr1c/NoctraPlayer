using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using System.Net.Http;
using System.Text.Json;
using Noctra.Services.Interfaces;
using Noctra.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using System.Text.RegularExpressions;

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
    History
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
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
        ISettingsService settingsService,
        IMetadataService metadataService,
        IDispatcherService dispatcherService,
        WatermarkViewModel watermarkViewModel)
    {
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _metadataService = metadataService;
        _dispatcherService = dispatcherService;
        WatermarkViewModel = watermarkViewModel;
        _settingsService.SettingsChanged += ApplyRefreshSchedulesFromSettings;
    }

    public async Task InitializeAsync()
    {
        // Otomatik yükleme yerine profil yüklenmesini bekle
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
        
        using var scope = _scopeFactory.CreateScope();
        var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
        
        try
        {
            // Ensure provider account is loaded
            if (profile.ProviderAccount == null)
            {
                StatusMessage = "Hesap bilgileri yüklenemedi";
                return;
            }

            // Check if playlist already exists (cache-first approach)
            var existingPlaylists = await playlistService.GetAllAsync(profile.Id);
            
            if (existingPlaylists.Count > 0)
            {
                // Use cached playlist - much faster!
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Using cached playlist for profile {profile.Id}");
                StatusMessage = "Önbellekten yükleniyor...";
                await LoadPlaylistsAsync();
            }
            else
            {
                // No cache - download and parse M3U
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] No cache found, downloading playlist for profile {profile.Id}");
                
                switch (profile.ProviderAccount.Type)
                {
                    case ProfileType.M3U:
                    {
                        var m3uUrl = profile.ProviderAccount.Url;
                        _ = CheckM3UExpirationAsync(profile.ProviderAccount);
                        StatusMessage = "Kanal listesi indiriliyor...";
                        await playlistService.AddFromUrlAsync(profile.Name, m3uUrl, profile.Id);
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
                        var xtreamService = scope.ServiceProvider.GetRequiredService<IXtreamCodesService>();

                        try
                        {
                            StatusMessage = "Xtream API'den kanallar alınıyor...";
                            var xtreamChannels = await xtreamService.GetChannelsAsync(
                                baseUrl,
                                username,
                                password,
                                includeSeriesEpisodes: true);

                            var sourceUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&type=m3u_plus&output=ts";
                            await playlistService.AddFromChannelsAsync(profile.Name, sourceUrl, xtreamChannels, profile.Id);
                            await LoadPlaylistsAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Xtream API fallback to M3U: {ex.Message}");
                            var fallbackM3uUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&type=m3u_plus&output=ts";
                            StatusMessage = "Kanal listesi indiriliyor...";
                            await playlistService.AddFromUrlAsync(profile.Name, fallbackM3uUrl, profile.Id);
                            await LoadPlaylistsAsync();
                        }

                        break;
                    }
                    case ProfileType.StalkerPortal:
                    {
                        StatusMessage = "Stalker Portal bağlantısı kuruluyor...";
                        var stalkerService = scope.ServiceProvider.GetRequiredService<IStalkerPortalService>();
                        var portalUrl = profile.ProviderAccount.Url;
                        var macAddress = profile.ProviderAccount.Username ?? string.Empty;

                        var stalkerChannels = await stalkerService.GetChannelsAsync(
                            portalUrl,
                            macAddress,
                            includeVod: true);

                        var sourceUrl = $"{portalUrl.TrimEnd('/')}/stalker_portal#{macAddress}";
                        await playlistService.AddFromChannelsAsync(profile.Name, sourceUrl, stalkerChannels, profile.Id);
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
            StatusMessage = $"Profil yüklenirken hata oluştu: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"LoadProfile Error: {ex}");
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
        using var scope = _scopeFactory.CreateScope();
        var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
        await playlistService.UpdateProviderExpirationAsync(accountId, expirationDate);

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
            System.Diagnostics.Debug.WriteLine($"[CheckExpiration] Checking: {apiUrl}");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var response = await client.GetAsync(apiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[CheckExpiration] Failed with status: {response.StatusCode}");
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
            System.Diagnostics.Debug.WriteLine($"CheckExpiration Error: {ex}");
        }
    }

    [RelayCommand]
    private async Task LoadPlaylistsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
        
        try
        {
            IsLoading = true;
            Playlists = await playlistService.GetAllAsync(CurrentProfileId);
            
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
            
            await Task.WhenAll(
                LoadHomeContentAsync(),
                LoadFavoritesAsync());
            await RefreshPersonalListsFromDatabaseAsync();
            StatusMessage = $"{channelCount} kanal hazır";
            
            // Trigger EPG update in background
            _ = LoadEpgAsync();
            EnsureChannelBackgroundRefresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadChannels error: {ex}");
            StatusMessage = $"Kanallar yüklenemedi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHomeContentAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        
        // Rail içeriklerini yükle
        TrendingChannels = Channels.Where(c => c.Type == ChannelType.Live).Take(10).ToList();
        LatestMovies = Channels.Where(c => c.Type == ChannelType.VOD).Take(10).ToList();
        var playlistId = SelectedPlaylist?.Id ?? 0;
        LatestSeries = await mediaService.GetSeriesAsync(playlistId);
        UpdateSeriesViewItems();
        ContinueWatching = Channels.Where(c => c.LastWatched.HasValue).OrderByDescending(c => c.LastWatched).Take(10).ToList();

        // Hero içeriği
        FeaturedChannel = TrendingChannels.FirstOrDefault() ?? LatestMovies.FirstOrDefault();
    }

    private Task LoadFavoritesAsync()
    {
        UpdateMyList();
        UpdateFavoriteChannels();
        UpdateHistoryChannels();
        _ = RefreshPersonalListsFromDatabaseAsync();
        return Task.CompletedTask;
    }

    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _searchCts;
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
    private bool _suppressFilterRefresh;

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
            UpdateMyList();
            UpdateFavoriteChannels();
            UpdateHistoryChannels();
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

            System.Diagnostics.Debug.WriteLine($"ApplyFilters error: {ex}");
            StatusMessage = "Filtreleme sırasında hata oluştu";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectChannel(Channel channel)
    {
        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();

        var isEpisodeContext = channel.Type == ChannelType.Series &&
                               CurrentEpisodePlaybackContext != null &&
                               string.Equals(CurrentEpisodePlaybackContext.StreamUrl, channel.StreamUrl, StringComparison.OrdinalIgnoreCase);
        if (!isEpisodeContext)
        {
            CurrentEpisodePlaybackContext = null;
            NextEpisodePlaybackContext = null;
        }
        
        SelectedChannel = channel;
        StatusMessage = $"Seçildi: {channel.Name}";
        
        // Update last watched
        channel.LastWatched = DateTime.Now;
        _ = channelService.UpdateChannelAsync(channel);
        UpdateHistoryChannels();

        // Notify UI to play
        OnMediaSelected?.Invoke(channel);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(object media)
    {
        if (media is not Channel channel)
        {
            return;
        }

        if (!CurrentProfileId.HasValue)
        {
            StatusMessage = "Önce bir profil seçmelisiniz";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();

            if (!isBackground)
            {
                IsLoading = true;
                StatusMessage = "Kanal listesi güncelleniyor...";
            }

            await playlistService.RefreshAsync(SelectedPlaylist.Id);
            await LoadChannelsAsync(SelectedPlaylist.Id);

            if (!isBackground)
            {
                StatusMessage = "Kanal listesi güncellendi";
            }
        }
        catch (Exception ex)
        {
            if (!isBackground)
            {
                StatusMessage = $"Kanal listesi güncelleme hatası: {ex.Message}";
            }
        }
        finally
        {
            if (!isBackground)
            {
                IsLoading = false;
            }
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

    private async Task LoadEpgInternalAsync(bool isBackgroundSync, bool forceRefresh = false, bool setBusyState = true)
    {
        if (CurrentProfile == null) return;

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

            // Daily cache: if today's EPG already exists for this playlist and user didn't force refresh, skip download.
            if (!forceRefresh && SelectedPlaylist != null)
            {
                var playlistState = await db.Playlists
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == SelectedPlaylist.Id);

                var alreadyUpdatedToday = playlistState?.EpgLastUpdated?.Date == DateTime.Now.Date;
                var hasCachedPrograms = await HasEpgForChannelsAsync(db, channelsForMapping);

                if (alreadyUpdatedToday && hasCachedPrograms)
                {
                    if (!isBackgroundSync)
                    {
                        StatusMessage = "EPG önbellekten kullanılıyor";
                    }
                    return;
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
                .Where(c => c.Percentage > 10 || c.ChannelCount > 5) // Min threshold
                .Take(3)
                .ToList();

            if (detectedCountries.Count == 0)
                detectedCountries.Add(("TR", 0, 0));

            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Detected countries: {string.Join(", ", detectedCountries.Select(c => c.CountryCode))}");

            // 3. EPG kaynaklarını topla
            var distinctSources = new List<Services.EpgSource>();
            
            // a) Provider Source (Primary) - Only once
            if (!string.IsNullOrEmpty(providerEpgUrl))
            {
                distinctSources.Add(new Services.EpgSource 
                { 
                    Url = providerEpgUrl, 
                    Priority = 1, 
                    Type = Services.EpgSourceType.Provider,
                    IsPrimary = true 
                });
            }

            // b) Country-specific sources (noctra-epg.org etc)
            foreach (var (countryCode, _, _) in detectedCountries)
            {
                var countrySources = epgSourceResolver.ResolveEpgSources(countryCode);
                foreach (var source in countrySources)
                {
                    // Skip if provider (already added) or if already in list
                    if (source.Type == Services.EpgSourceType.Provider) continue;
                    
                    // Avoid duplicates based on URL
                    if (!distinctSources.Any(s => s.Url == source.Url))
                    {
                        distinctSources.Add(source);
                    }
                }
            }

            // Sort by priority
            var epgSources = distinctSources.OrderBy(s => s.Priority).ToList();

            // Optional custom EPG URL from settings (highest priority when provided)
            var customEpgUrl = (_settingsService.Settings.CustomEpgUrl ?? string.Empty).Trim();
            if (Uri.TryCreate(customEpgUrl, UriKind.Absolute, out _))
            {
                epgSources.Insert(0, new Services.EpgSource
                {
                    Url = customEpgUrl,
                    Priority = 0,
                    Type = Services.EpgSourceType.CustomUrl,
                    IsPrimary = false
                });
            }

            // Ensure we only clear the DB once (at the start), not for every source
            for (int i = 0; i < epgSources.Count; i++)
            {
                epgSources[i].ClearBeforeLoad = (i == 0);
            }

            if (!isBackgroundSync)
            {
                StatusMessage = "EPG kaynakları deneniyor...";
            }

            // 4. Her kaynağı indirmeyi dene
            bool anySuccess = false;
            string? lastSourceError = null;
            
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
                        System.Diagnostics.Debug.WriteLine("[MainViewModel] Clearing existing EPG data...");
                        await epgService.ClearEpgAsync();
                    }

                    var beforeCount = await epgService.GetTotalProgramCountAsync();
                    await epgService.LoadEpgAsync(source.Url, source.IsPrimary, channelsForMapping, daysAhead: 1);
                    var afterCount = await epgService.GetTotalProgramCountAsync();
                    var loadedPrograms = afterCount - beforeCount;

                    if (loadedPrograms > 0)
                    {
                        anySuccess = true;
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] EPG loaded from {source.Type} ({loadedPrograms} programs) - URL: {source.Url}");
                        lastSourceError = null;
                    }
                    else
                    {
                        // Don't overwrite last error if we already had success
                        if (!anySuccess) 
                        {
                            lastSourceError = $"{source.Type}: 0 program";
                        }
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] EPG source returned 0 programs: {source.Type}");
                    }
                    
                    // Do NOT break here; continue to load other countries/sources
                }
                catch (Exception ex)
                {
                    lastSourceError = $"{source.Type}: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] EPG source failed: {source.Type} - {ex.Message}");
                }
            }

            EnsureEpgBackgroundSync();

            if (!isBackgroundSync)
            {
                StatusMessage = anySuccess
                    ? "EPG hazır"
                    : $"EPG yüklenemedi{(string.IsNullOrWhiteSpace(lastSourceError) ? "" : $" ({lastSourceError})")}";
            }

            if (anySuccess && SelectedPlaylist != null)
            {
                var playlistToUpdate = await db.Playlists.FirstOrDefaultAsync(p => p.Id == SelectedPlaylist.Id);
                if (playlistToUpdate != null)
                {
                    playlistToUpdate.EpgLastUpdated = DateTime.Now;
                    await db.SaveChangesAsync();
                    SelectedPlaylist.EpgLastUpdated = playlistToUpdate.EpgLastUpdated;
                }
            }
        }
        catch (Exception ex)
        {
            if (!isBackgroundSync)
            {
                StatusMessage = $"EPG Hatası: {ex.Message}";
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Background EPG sync failed: {ex.Message}");
            }
        }
        finally
        {
            if (!isBackgroundSync && setBusyState)
            {
                IsLoading = false;
            }
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
    private List<Channel> _myList = new();

    [ObservableProperty]
    private List<Channel> _favoriteChannels = new();

    [ObservableProperty]
    private List<Channel> _historyChannels = new();

    [ObservableProperty]
    private List<Channel> _historyLiveChannels = new();

    [ObservableProperty]
    private List<Channel> _historySeriesChannels = new();

    [ObservableProperty]
    private List<Channel> _historyVodChannels = new();

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
        if (view == AppView.Live) SelectedChannelType = ChannelType.Live;
        else if (view == AppView.Movies) SelectedChannelType = ChannelType.VOD;
        else if (view == AppView.Series) SelectedChannelType = ChannelType.Series;
        else if (view == AppView.MyList)
        {
            SelectedChannelType = null;
            SelectedGroup = null;
            UpdateMyList();
            _ = RefreshPersonalListsFromDatabaseAsync();
        }
        else if (view == AppView.Favorites)
        {
            SelectedChannelType = null;
            SelectedGroup = null;
            ShowOnlyFavorites = false;
            UpdateFavoriteChannels();
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
        else SelectedChannelType = null;
        
        ScheduleImmediateFilter();
    }

    private void UpdateMyList()
    {
        var list = new List<Channel>();
        list.AddRange(Channels.Where(c => c.IsInMyList));
        MyList = list;
        ShowMyListEmptyState = MyList.Count == 0;
    }

    private void UpdateFavoriteChannels()
    {
        FavoriteChannels = Channels
            .Where(c => c.IsFavorite)
            .ToList();
        ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
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

    private async Task RefreshPersonalListsFromDatabaseAsync()
    {
        if (!CurrentProfileId.HasValue)
        {
            MyList = new List<Channel>();
            FavoriteChannels = new List<Channel>();
            HistoryChannels = new List<Channel>();
            HistoryLiveChannels = new List<Channel>();
            HistorySeriesChannels = new List<Channel>();
            HistoryVodChannels = new List<Channel>();
            ShowMyListEmptyState = true;
            ShowFavoritesEmptyState = true;
            ShowHistoryEmptyState = true;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watchHistoryService = scope.ServiceProvider.GetRequiredService<IWatchHistoryService>();
        await watchHistoryService.CleanupOlderThanDaysAsync(CurrentProfileId.Value, 7);
        var profilePlaylistIds = await db.Playlists
            .AsNoTracking()
            .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync();

        if (profilePlaylistIds.Count == 0)
        {
            MyList = new List<Channel>();
            FavoriteChannels = new List<Channel>();
            HistoryChannels = new List<Channel>();
            HistoryLiveChannels = new List<Channel>();
            HistorySeriesChannels = new List<Channel>();
            HistoryVodChannels = new List<Channel>();
            ShowMyListEmptyState = true;
            ShowFavoritesEmptyState = true;
            ShowHistoryEmptyState = true;
            return;
        }

        MyList = await db.Channels
            .AsNoTracking()
            .Where(c => profilePlaylistIds.Contains(c.PlaylistId) && c.IsInMyList)
            .OrderBy(c => c.Name)
            .ToListAsync();

        FavoriteChannels = await db.Channels
            .AsNoTracking()
            .Where(c => profilePlaylistIds.Contains(c.PlaylistId) && c.IsFavorite)
            .OrderBy(c => c.Name)
            .ToListAsync();

        HistoryChannels = await GetHistoryChannelsFromWatchHistoryAsync(db, profilePlaylistIds);
        UpdateHistoryBuckets();

        ShowMyListEmptyState = MyList.Count == 0;
        ShowFavoritesEmptyState = FavoriteChannels.Count == 0;
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
        var profilePlaylistIds = await db.Playlists
            .AsNoTracking()
            .Where(p => p.ProfileId == CurrentProfileId.Value && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync();

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
            .ToList();

        var seriesSnapshot = LatestSeries.ToList();

        SearchSeriesChannels = seriesSnapshot
            .Where(series => SeriesMatchesSearch(series, rawQuery, normalizedSeriesQuery))
            .OrderBy(series => series.Name)
            .ToList();

        SearchVodChannels = FilteredChannels
            .Where(c => c.Type == ChannelType.VOD)
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
            .Take(12)
            .ToList();

        var similarSeries = seriesSnapshot
            .Where(s => IsLikelySimilar(rawQuery, s.Name))
            .Where(s => !SearchSeriesChannels.Any(x => x.Id == s.Id))
            .Take(12)
            .ToList();

        var similarVod = Channels
            .Where(c => c.Type == ChannelType.VOD)
            .Where(c => IsLikelySimilar(rawQuery, c.Name))
            .Where(c => !SearchVodChannels.Any(x => x.Id == c.Id))
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
                    .Take(10));

                localResults.AddRange(seriesSnapshot.Where(s =>
                    SeriesMatchesSearch(s, query, normalizedSeriesQuery))
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
        if (!CurrentProfileId.HasValue || media is not Channel channel)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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

        StatusMessage = "Favorilerden çıkarıldı";
        await RefreshPersonalListsFromDatabaseAsync();
    }

    [RelayCommand]
    private void PlayEpisode(Episode episode)
    {
        var channel = BuildSeriesEpisodeChannel(episode);
        CurrentEpisodePlaybackContext = episode;
        NextEpisodePlaybackContext = FindNextEpisode(episode);
        
        SelectChannel(channel);
        IsSeriesDetailVisible = false;
    }

    [RelayCommand]
    private void CloseSeriesDetail()
    {
        IsSeriesDetailVisible = false;
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
                System.Diagnostics.Debug.WriteLine($"EnsureSeriesEpisodes failed: {ex.Message}");
            }
            SelectedSeries = selectedSeries;
            IsSeriesDetailVisible = true;
            StatusMessage = $"Seçildi: {selectedSeries.Name}";
            OnMediaSelected?.Invoke(selectedSeries);
            _ = LoadSelectedSeriesMetadataAsync(selectedSeries);
        }
    }


    private static readonly Regex SeriesEpisodeRegex = new(
        @"\b(?:s(?:eason)?\s*\d{1,2}\s*e(?:pisode)?\s*\d{1,3}|\d{1,2}\s*x\s*\d{1,3}|sezon\s*\d{1,2}\s*b[oö]l[uü]m\s*\d{1,3}|b[oö]l[uü]m\s*\d{1,3}|ep(?:isode)?\s*\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string NormalizeSeriesQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var normalized = SeriesEpisodeRegex.Replace(query, " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized.ToLowerInvariant();
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
            System.Diagnostics.Debug.WriteLine($"Series metadata load failed: {ex.Message}");
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

    private static (int SeasonNumber, int EpisodeNumber) ParseEpisodeNumbers(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (1, 1);
        }

        var sxe = Regex.Match(title, @"[Ss](\d{1,2})\s*[Ee](\d{1,3})", RegexOptions.IgnoreCase);
        if (sxe.Success)
        {
            return (SafeParseInt(sxe.Groups[1].Value, 1), SafeParseInt(sxe.Groups[2].Value, 1));
        }

        var xFormat = Regex.Match(title, @"(\d{1,2})\s*[Xx]\s*(\d{1,3})", RegexOptions.IgnoreCase);
        if (xFormat.Success)
        {
            return (SafeParseInt(xFormat.Groups[1].Value, 1), SafeParseInt(xFormat.Groups[2].Value, 1));
        }

        var trFormat = Regex.Match(title, @"[Ss]ezon\s*(\d{1,2}).*?[Bb][oö]l[uü]m\s*(\d{1,3})", RegexOptions.IgnoreCase);
        if (trFormat.Success)
        {
            return (SafeParseInt(trFormat.Groups[1].Value, 1), SafeParseInt(trFormat.Groups[2].Value, 1));
        }

        return (1, 1);
    }

    private static int SafeParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static string NormalizeSeriesTitleForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeSeriesQuery(value);
        normalized = Regex.Replace(normalized, @"\b(4k|2160p|1080p|720p|x264|x265|h264|h265|webrip|web-dl|bluray)\b", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
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

        var episodeIds = episodes
            .Where(e => e.Id > 0)
            .Select(e => e.Id)
            .Distinct()
            .ToList();

        if (episodeIds.Count == 0)
        {
            return;
        }

        var latestEpisodeHistories = await db.WatchHistories
            .Where(h => h.ProfileId == CurrentProfileId.Value && h.EpisodeId.HasValue && episodeIds.Contains(h.EpisodeId.Value))
            .GroupBy(h => h.EpisodeId!.Value)
            .Select(g => g.OrderByDescending(x => x.WatchedAt).First())
            .ToListAsync();

        if (latestEpisodeHistories.Count == 0)
        {
            return;
        }

        var historyByEpisodeId = latestEpisodeHistories.ToDictionary(h => h.EpisodeId!.Value);
        foreach (var episode in episodes)
        {
            if (!historyByEpisodeId.TryGetValue(episode.Id, out var history))
            {
                continue;
            }

            episode.LastWatched = history.WatchedAt;
            episode.WatchedPosition = history.StoppedAt;
            episode.IsCompleted = history.Completed;
        }
    }

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

    private Episode? FindNextEpisode(Episode episode)
    {
        var series = SelectedSeries;
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
}



