using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using System.Net.Http;
using System.Text.Json;
using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;

namespace IPTVPlayer.ViewModels;

public enum AppView
{
    Home,
    Live,
    Movies,
    Series,
    Search,
    MyList
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
        IDispatcherService dispatcherService,
        WatermarkViewModel watermarkViewModel)
    {
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        WatermarkViewModel = watermarkViewModel;
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
            var groups = await playlistService.GetGroupsAsync(playlistId);
            var channelCount = await playlistService.GetChannelCountAsync(playlistId);
            Groups = OrderGroupsByLanguagePreference(groups);
            EnsurePreferredDefaultGroupSelected();
            ResetIncrementalState();
            await LoadMoreChannelsAsync();
            
            await Task.WhenAll(
                LoadHomeContentAsync(),
                LoadFavoritesAsync());
            StatusMessage = $"{channelCount} kanal hazır";
            
            // Trigger EPG update in background
            _ = LoadEpgAsync();
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
        LatestSeries = await mediaService.GetSeriesAsync(SelectedPlaylist?.Id ?? 0);
        ContinueWatching = Channels.Where(c => c.LastWatched.HasValue).OrderByDescending(c => c.LastWatched).Take(10).ToList();

        // Hero içeriği
        FeaturedChannel = TrendingChannels.FirstOrDefault() ?? LatestMovies.FirstOrDefault();
    }

    private Task LoadFavoritesAsync()
    {
        UpdateMyList();
        return Task.CompletedTask;
    }

    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _searchCts;
    private readonly int _filterDelayMs = 300;
    private readonly int _searchDelayMs = 200;
    private int _currentPage;
    private bool _hasMoreChannels;
    private bool _isLoadingMoreChannels;
    private Timer? _epgSyncTimer;
    private int _isBackgroundEpgSyncRunning;
    private bool _suppressFilterRefresh;

    private void ResetIncrementalState()
    {
        _currentPage = 0;
        _hasMoreChannels = true;
        _isLoadingMoreChannels = false;
        Channels = new List<Channel>();
        FilteredChannels = new List<Channel>();
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

            var page = await playlistService.GetChannelsFilteredPageAsync(
                SelectedPlaylist.Id,
                skip: _currentPage * IncrementalPageSize,
                take: IncrementalPageSize,
                searchText: SearchText,
                group: SelectedGroup,
                type: SelectedChannelType,
                onlyFavorites: ShowOnlyFavorites,
                sortOrder: SelectedSortOrder);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (page.Count == 0)
            {
                _hasMoreChannels = false;
                return;
            }

            _currentPage++;
            _hasMoreChannels = page.Count == IncrementalPageSize;

            var merged = new List<Channel>(FilteredChannels.Count + page.Count);
            merged.AddRange(FilteredChannels);
            merged.AddRange(page);

            Channels = merged;
            FilteredChannels = merged;
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
            if (token.IsCancellationRequested) return;
            await LoadMoreChannelsAsync(token);
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
        
        SelectedChannel = channel;
        StatusMessage = $"Seçildi: {channel.Name}";
        
        // Update last watched
        channel.LastWatched = DateTime.Now;
        _ = channelService.UpdateChannelAsync(channel);

        // Notify UI to play
        OnMediaSelected?.Invoke(channel);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Channel channel)
    {
        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        
        channel.IsFavorite = !channel.IsFavorite;
        await channelService.UpdateChannelAsync(channel);
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
        if (SelectedPlaylist == null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistService>();
            
            IsLoading = true;
            StatusMessage = "Playlist güncelleniyor...";
            
            await playlistService.RefreshAsync(SelectedPlaylist.Id);
            await LoadChannelsAsync(SelectedPlaylist.Id);
            
            StatusMessage = "Playlist güncellendi";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Güncelleme hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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

            // b) Country-specific sources (iptv-epg.org etc)
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
        if (_epgSyncTimer != null)
        {
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
        }, null, TimeSpan.FromHours(6), TimeSpan.FromHours(6));
    }

    [ObservableProperty]
    private List<Channel> _myList = new();

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
            UpdateMyList();
        }
        else SelectedChannelType = null;
        
        ScheduleImmediateFilter();
    }

    private void UpdateMyList()
    {
        var list = new List<Channel>();
        list.AddRange(Channels.Where(c => c.IsInMyList));
        // Series logic needs specific handling if we want to show series in My List
        // For now, let's assume Channels covers series if they are in the main list
        // Or we might need to add logic to fetch series marked as favorite/mylist
        MyList = list;
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

                localResults.AddRange(channelsSnapshot.Where(c =>
                    c.Name.ToLower().Contains(searchLower) ||
                    (c.GroupTitle?.ToLower().Contains(searchLower) ?? false))
                    .Take(10));

                localResults.AddRange(seriesSnapshot.Where(s =>
                    s.Name.ToLower().Contains(searchLower) ||
                    (s.Genre?.ToLower().Contains(searchLower) ?? false))
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
        using var scope = _scopeFactory.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IChannelService>();
        var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        
        if (media is Channel channel)
        {
            channel.IsInMyList = !channel.IsInMyList;
            await channelService.UpdateChannelAsync(channel);
        }
        else if (media is Series series)
        {
            series.IsInMyList = !series.IsInMyList;
            await mediaService.UpdateSeriesAsync(series);
        }
    }

    [RelayCommand]
    private void PlayEpisode(Episode episode)
    {
        // Convert Episode to Channel for playing
        var channel = new Channel
        {
            Name = episode.Name,
            StreamUrl = episode.StreamUrl,
            LogoUrl = episode.CoverUrl,
            Type = ChannelType.Series,
            PlaylistId = SelectedPlaylist?.Id ?? 0
        };
        
        SelectChannel(channel);
        IsSeriesDetailVisible = false;
    }

    [RelayCommand]
    private void CloseSeriesDetail()
    {
        IsSeriesDetailVisible = false;
        SelectedSeries = null;
    }

    [RelayCommand]
    private void PlayFeatured()
    {
        if (FeaturedChannel != null)
        {
            SelectMedia(FeaturedChannel);
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
    private void SelectMedia(object? media)
    {
        if (media == null) return;

        if (media is Channel channel)
        {
            SelectedChannel = channel;
            StatusMessage = $"Seçildi: {channel.Name}";
            OnMediaSelected?.Invoke(channel);
        }
        else if (media is Series series)
        {
            SelectedSeries = series;
            IsSeriesDetailVisible = true;
            StatusMessage = $"Seçildi: {series.Name}";
            OnMediaSelected?.Invoke(series);
        }
    }
}


