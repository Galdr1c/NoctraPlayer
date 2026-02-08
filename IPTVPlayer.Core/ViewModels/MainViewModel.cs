using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

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
    private readonly IPlaylistService _playlistService;
    private readonly IEpgService _epgService;
    private readonly IMediaService _mediaService;
    private readonly IChannelService _channelService;

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

    private readonly IDispatcherService _dispatcherService;

    public WatermarkViewModel WatermarkViewModel { get; }

    public MainViewModel(
        IPlaylistService playlistService,
        IEpgService epgService,
        IDispatcherService dispatcherService,
        WatermarkViewModel watermarkViewModel,
        IMediaService mediaService,
        IChannelService channelService)
    {
        _playlistService = playlistService;
        _epgService = epgService;
        _dispatcherService = dispatcherService;
        WatermarkViewModel = watermarkViewModel;
        _mediaService = mediaService;
        _channelService = channelService;
    }

    public async Task InitializeAsync()
    {
        // Otomatik yükleme yerine profil yüklenmesini bekle
    }

    [ObservableProperty]
    private int? _currentProfileId;

    public async Task LoadProfileAsync(Profile profile)
    {
        if (profile == null) return;

        IsLoading = true;
        StatusMessage = $"{profile.Name} yükleniyor...";
        CurrentProfileId = profile.Id;
        
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
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Using cached playlist for profile {profile.Id}");
                StatusMessage = "Önbellekten yükleniyor...";
                await LoadPlaylistsAsync();
            }
            else
            {
                // No cache - download and parse M3U
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] No cache found, downloading playlist for profile {profile.Id}");
                
                string m3uUrl;
                if (profile.ProviderAccount.Type == ProfileType.M3U)
                {
                    m3uUrl = profile.ProviderAccount.Url;
                }
                else // XtreamCodes
                {
                    StatusMessage = "Xtream bağlantısı kuruluyor...";
                    var baseUrl = profile.ProviderAccount.Url.TrimEnd('/');
                    if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
                    m3uUrl = $"{baseUrl}/get.php?username={profile.ProviderAccount.Username}&password={profile.ProviderAccount.Password}&type=m3u_plus&output=ts";
                }
                
                StatusMessage = "Kanal listesi indiriliyor...";
                await _playlistService.AddFromUrlAsync(profile.Name, m3uUrl, profile.Id);
                await LoadPlaylistsAsync();
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
        try
        {
            IsLoading = true;
            StatusMessage = "Kanallar yükleniyor...";
            
            // FAST: Only load groups and channel count initially
            Groups = await _playlistService.GetGroupsAsync(playlistId);
            var channelCount = await _playlistService.GetChannelCountAsync(playlistId);
            
            // Load filtered channels (limited to 1000 for fast UI)
            Channels = await _playlistService.GetChannelsFilteredAsync(playlistId, limit: 1000);
            FilteredChannels = Channels;
            
            await LoadHomeContentAsync();
            StatusMessage = $"{channelCount} kanal hazır";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHomeContentAsync()
    {
        // Rail içeriklerini yükle
        TrendingChannels = Channels.Where(c => c.Type == ChannelType.Live).Take(10).ToList();
        LatestMovies = Channels.Where(c => c.Type == ChannelType.VOD).Take(10).ToList();
        LatestSeries = await _mediaService.GetSeriesAsync(SelectedPlaylist?.Id ?? 0);
        ContinueWatching = Channels.Where(c => c.LastWatched.HasValue).OrderByDescending(c => c.LastWatched).Take(10).ToList();

        // Hero içeriği
        FeaturedChannel = TrendingChannels.FirstOrDefault() ?? LatestMovies.FirstOrDefault();
    }

    private CancellationTokenSource? _filterCts;
    private readonly int _filterDelayMs = 300;

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && ActiveView != AppView.Search)
        {
            ActiveView = AppView.Search;
        }
        else if (string.IsNullOrWhiteSpace(value) && ActiveView == AppView.Search)
        {
            ActiveView = AppView.Home;
        }

        // Debounce logic
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;
        
        Task.Delay(_filterDelayMs, token).ContinueWith(_ => 
        {
            if (!token.IsCancellationRequested)
            {
                _dispatcherService.Invoke(() => ApplyFilters());
            }
        }, TaskScheduler.Default);
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        ApplyFilters();
    }

    partial void OnSelectedChannelTypeChanged(ChannelType? value)
    {
        ApplyFilters();
    }

    partial void OnShowOnlyFavoritesChanged(bool value)
    {
        ApplyFilters();
    }

    private async void ApplyFilters()
    {
        if (SelectedPlaylist == null) return;
        
        IsLoading = true;
        
        try
        {
            // Use database-level filtering for performance
            var filtered = await _playlistService.GetChannelsFilteredAsync(
                SelectedPlaylist.Id,
                searchText: SearchText,
                group: SelectedGroup,
                type: SelectedChannelType,
                limit: 1000
            );

            // Apply favorites filter (in-memory since it's a local property)
            if (ShowOnlyFavorites)
            {
                filtered = filtered.Where(c => c.IsFavorite).ToList();
            }

            // UI Thread update
            _dispatcherService.Invoke(() =>
            {
                Channels = filtered;
                FilteredChannels = filtered;
            });
        }
        catch (Exception ex)
        {
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
        SelectedChannel = channel;
        StatusMessage = $"Seçildi: {channel.Name}";
        
        // Update last watched
        channel.LastWatched = DateTime.Now;
        _ = _channelService.UpdateChannelAsync(channel);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Channel channel)
    {
        channel.IsFavorite = !channel.IsFavorite;
        await _channelService.UpdateChannelAsync(channel);
        ApplyFilters();
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
            IsLoading = true;
            StatusMessage = "Playlist güncelleniyor...";
            
            await _playlistService.RefreshAsync(SelectedPlaylist.Id);
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
    private void Navigate(AppView view)
    {
        ActiveView = view;
        if (view == AppView.Live) SelectedChannelType = ChannelType.Live;
        else if (view == AppView.Movies) SelectedChannelType = ChannelType.VOD;
        else if (view == AppView.Series) SelectedChannelType = ChannelType.Series;
        else SelectedChannelType = null;
        
        ApplyFilters();
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
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults = new List<object>();
            return;
        }

        var searchLower = value.ToLower();
        
        var results = new List<object>();
        
        // Search Channels (Movies/Live)
        results.AddRange(Channels.Where(c => 
            c.Name.ToLower().Contains(searchLower) ||
            (c.GroupTitle?.ToLower().Contains(searchLower) ?? false))
            .Take(10));

        // Search Series
        results.AddRange(LatestSeries.Where(s => 
            s.Name.ToLower().Contains(searchLower) ||
            (s.Genre?.ToLower().Contains(searchLower) ?? false))
            .Take(10));

        SearchResults = results;
    }

    [RelayCommand]
    private async Task AddToMyList(object media)
    {
        if (media is Channel channel)
        {
            channel.IsInMyList = !channel.IsInMyList;
            await _channelService.UpdateChannelAsync(channel);
        }
        else if (media is Series series)
        {
            series.IsInMyList = !series.IsInMyList;
            // TODO: Series persistence service update
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
        OnMediaSelected?.Invoke(channel);
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
