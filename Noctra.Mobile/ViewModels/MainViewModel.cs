using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Services.Interfaces;

namespace Noctra.Mobile.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _selectedDestination = "Home";

    [ObservableProperty]
    private string _pageTitle = "Home";

    [ObservableProperty]
    private string _pageDescription = "Your channels, movies and series in one place.";

    public MainViewModel()
    {
        UpdatePageText(SelectedDestination);
    }

    public MainViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLanguageChanged;
        UpdatePageText(SelectedDestination);
    }

    public bool IsMoreSelected => string.Equals(
        SelectedDestination,
        "More",
        StringComparison.Ordinal);

    public void SelectDestination(string destination)
    {
        SelectedDestination = destination;
        OnPropertyChanged(nameof(IsMoreSelected));
        UpdatePageText(destination);
    }

    private void OnLanguageChanged()
    {
        UpdatePageText(SelectedDestination);
    }

    private void UpdatePageText(string destination)
    {
        var (titleKey, descriptionKey, fallbackTitle, fallbackDescription) = destination switch
        {
            "Live" => ("Shell.Nav.Live", "Mobile.Page.Live.Description", "Live TV", "Browse live channels and current programs."),
            "Movies" => ("Shell.Nav.Movies", "Mobile.Page.Movies.Description", "Movies", "Continue watching or explore your movie library."),
            "Series" => ("Shell.Nav.Series", "Mobile.Page.Series.Description", "Series", "Pick up your episodes and discover new series."),
            "Search" => ("Shell.Search.Tooltip", "Mobile.Page.Search.Description", "Search", "Find live channels, movies and series."),
            "Favorites" => ("Shell.Nav.Favorites", "Mobile.Page.Favorites.Description", "Favorites", "Your saved live channels, movies and series."),
            "MyList" => ("Shell.Nav.MyList", "Mobile.Page.MyList.Description", "My List", "Everything you saved for later."),
            "History" => ("Shell.Nav.History", "Mobile.Page.History.Description", "History", "Continue from recently watched content."),
            "Downloads" => ("Shell.Nav.Downloads", "Mobile.Page.Downloads.Description", "Downloads", "Watch saved movies and series offline."),
            "Settings" => ("Settings.Title", "Mobile.Page.Settings.Description", "Settings", "Adjust playback, downloads, privacy and appearance."),
            "More" => ("Mobile.Nav.More", "Mobile.Page.More.Description", "More", "Profiles, favorites, settings and premium features."),
            _ => ("Shell.Nav.Home", "Mobile.Page.Home.Description", "Home", "Your channels, movies and series in one place.")
        };

        PageTitle = GetString(titleKey, fallbackTitle);
        PageDescription = GetString(descriptionKey, fallbackDescription);
    }

    private string GetString(string key, string fallback)
    {
        if (_localizationService is null)
        {
            return fallback;
        }

        var value = _localizationService.GetString(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
