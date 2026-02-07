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
    private Channel? _featuredMedia;

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
    private string? _selectedGroup;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ChannelType? _selectedChannelType;

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
                // Try to reload or exit gracefully
                StatusMessage = "Hesap bilgileri yüklenemedi";
                return;
            }

            if (profile.ProviderAccount.Type == ProfileType.M3U)
            {
                // Profile ID ile playlist ekle
                await _playlistService.AddFromUrlAsync(profile.Name, profile.ProviderAccount.Url, profile.Id);
                await LoadPlaylistsAsync(); 
            }
            else if (profile.ProviderAccount.Type == ProfileType.XtreamCodes)
            {
                // Xtream Codes -> M3U Conversion
                StatusMessage = "Xtream bağlantısı kuruluyor...";
                
                var baseUrl = profile.ProviderAccount.Url.TrimEnd('/');
                if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;

                var m3uUrl = $"{baseUrl}/get.php?username={profile.ProviderAccount.Username}&password={profile.ProviderAccount.Password}&type=m3u_plus&output=ts";
                
                await _playlistService.AddFromUrlAsync(profile.Name, m3uUrl, profile.Id);
                await LoadPlaylistsAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Profil yüklenirken hata oluştu: {ex.Message}";
            // Log the error
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
            
            Channels = await _playlistService.GetChannelsAsync(playlistId);
            
            // Grupları çıkar
            Groups = Channels
                .Where(c => !string.IsNullOrEmpty(c.GroupTitle))
                .Select(c => c.GroupTitle!)
                .Distinct()
                .OrderBy(g => g)
                .ToList();
            
            ApplyFilters();
            await LoadHomeContentAsync();
            StatusMessage = $"{Channels.Count} kanal yüklendi";
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
        FeaturedMedia = TrendingChannels.FirstOrDefault() ?? LatestMovies.FirstOrDefault();
    }

    private CancellationTokenSource? _filterCts;

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

        // Cancel previous search
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        // Fire and forget debounce
        _ = DebounceSearchAsync(token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            await _dispatcherService.InvokeAsync(() => ApplyFilters());
        }
        catch (OperationCanceledException)
        {
            // İptal edildi
        }
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

    private async Task ApplyFilters()
    {
        IsLoading = true;
        
        try
        {
            var filtered = await Task.Run(() =>
            {
                var query = Channels.AsEnumerable();

                // Metin araması
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var searchLower = SearchText.ToLower();
                    query = query.Where(c => 
                        c.Name.ToLower().Contains(searchLower) ||
                        (c.GroupTitle?.ToLower().Contains(searchLower) ?? false));
                }

                // Grup filtresi
                if (!string.IsNullOrEmpty(SelectedGroup))
                {
                    query = query.Where(c => c.GroupTitle == SelectedGroup);
                }

                // Tür filtresi
                if (SelectedChannelType.HasValue)
                {
                    query = query.Where(c => c.Type == SelectedChannelType.Value);
                }

                // Favoriler filtresi
                if (ShowOnlyFavorites)
                {
                    query = query.Where(c => c.IsFavorite);
                }

                // Limit for UI stability
                return query.ToList();
            });

            // UI Thread güncellemesi
            _dispatcherService.Invoke(() =>
            {
                FilteredChannels = filtered;
            });
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
    private void SelectMedia(object media)
    {
        if (media is Channel channel)
        {
            if (channel.Type == ChannelType.Live)
            {
                SelectChannel(channel);
            }
            else
            {
                // VOD Detail view açılması lazım
                SelectedChannel = channel;
                // DetailView açılması için bir flag eklenebilir
            }
        }
        else if (media is Series series)
        {
            // Series Detail view
            // SelectedSeries = series;
        }
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
}
