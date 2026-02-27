using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.ViewModels;
using System.Collections.ObjectModel;

namespace Noctra.Avalonia.ViewModels;

public partial class MainShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentView = "Home";

    [ObservableProperty]
    private bool _isPlayerVisible = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;


    [ObservableProperty]
    private double _contentScrollOffset;

    [ObservableProperty]
    private string _statusMessage = "Hazir";

    public ObservableCollection<MediaCard> HomeCards { get; } = new();
    public ObservableCollection<MediaCard> LiveCards { get; } = new();
    public ObservableCollection<MediaCard> MoviesCards { get; } = new();
    public ObservableCollection<MediaCard> SeriesCards { get; } = new();

    public PlayerViewModel Player { get; }

    public IEnumerable<MediaCard> CurrentCards
    {
        get
        {
            IEnumerable<MediaCard> baseSource = CurrentView switch
            {
                "Live" => LiveCards,
                "Movies" => MoviesCards,
                "Series" => SeriesCards,
                _ => HomeCards
            };

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return baseSource;
            }

            return baseSource.Where(c => c.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string CurrentViewTitle => CurrentView switch
    {
        "Live" => "Canli Yayinlar",
        "Movies" => "Filmler",
        "Series" => "Diziler",
        "Search" => "Arama",
        "MyList" => "Listem",
        "Favorites" => "Favoriler",
        "History" => "Izleme Gecmisi",
        _ => "Anasayfa"
    };

    public MainShellViewModel(PlayerViewModel player)
    {
        Player = player;
        SeedCards();
    }

    partial void OnCurrentViewChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentViewTitle));
        OnPropertyChanged(nameof(CurrentCards));
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentCards));
    }

    [RelayCommand]
    private void Navigate(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        CurrentView = target;
        StatusMessage = $"{CurrentViewTitle} acildi";
    }

    [RelayCommand]
    private void TogglePlayer()
    {
        IsPlayerVisible = !IsPlayerVisible;
    }


    [RelayCommand]
    private void CommitSearch()
    {
        SearchText = SearchQuery.Trim();
        CurrentView = "Search";
        StatusMessage = string.IsNullOrWhiteSpace(SearchText)
            ? "Arama temizlendi"
            : $"Arama uygulandi: {SearchText}";
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchQuery = string.Empty;
        StatusMessage = "Arama temizlendi";
    }

    [RelayCommand]
    private void OpenCard(MediaCard? card)
    {
        if (card == null)
        {
            return;
        }

        StatusMessage = $"Acildi: {card.Title}";
    }

    [RelayCommand]
    private void AddToMyList(MediaCard? card)
    {
        if (card == null)
        {
            return;
        }

        card.IsInMyList = !card.IsInMyList;
        StatusMessage = card.IsInMyList
            ? $"Listeye eklendi: {card.Title}"
            : $"Listeden cikarildi: {card.Title}";
    }

    [RelayCommand]
    private void ToggleFavorite(MediaCard? card)
    {
        if (card == null)
        {
            return;
        }

        card.IsFavorite = !card.IsFavorite;
        StatusMessage = card.IsFavorite
            ? $"Favoriye eklendi: {card.Title}"
            : $"Favoriden cikarildi: {card.Title}";
    }

    public void UpdateScroll(double offset)
    {
        ContentScrollOffset = offset;
    }

    private void SeedCards()
    {
        HomeCards.Clear();
        LiveCards.Clear();
        MoviesCards.Clear();
        SeriesCards.Clear();

        HomeCards.Add(new MediaCard("Noctra Spotlight", "Home"));
        HomeCards.Add(new MediaCard("Trending Picks", "Home"));
        HomeCards.Add(new MediaCard("Continue Watching", "Home"));

        LiveCards.Add(new MediaCard("TRT 1 HD", "Live"));
        LiveCards.Add(new MediaCard("A Spor", "Live"));
        LiveCards.Add(new MediaCard("BBC World", "Live"));

        MoviesCards.Add(new MediaCard("Inception", "Movies"));
        MoviesCards.Add(new MediaCard("Interstellar", "Movies"));
        MoviesCards.Add(new MediaCard("Dune", "Movies"));

        SeriesCards.Add(new MediaCard("Dark", "Series"));
        SeriesCards.Add(new MediaCard("Breaking Bad", "Series"));
        SeriesCards.Add(new MediaCard("Severance", "Series"));
    }
}
