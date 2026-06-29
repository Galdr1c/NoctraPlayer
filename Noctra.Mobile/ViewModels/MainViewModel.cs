using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Mobile.Localization;
using Noctra.Services.Interfaces;

namespace Noctra.Mobile.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _selectedDestination = "Home";

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    // Design-time only — production uses the parameterized constructor via DI.
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
        var (titleKey, descriptionKey) = destination switch
        {
            "Live" => ("Shell.Nav.Live", "Mobile.Page.Live.Description"),
            "Movies" => ("Shell.Nav.Movies", "Mobile.Page.Movies.Description"),
            "Series" => ("Shell.Nav.Series", "Mobile.Page.Series.Description"),
            "Search" => ("Shell.Search.Tooltip", "Mobile.Page.Search.Description"),
            "Favorites" => ("Shell.Nav.Favorites", "Mobile.Page.Favorites.Description"),
            "MyList" => ("Shell.Nav.MyList", "Mobile.Page.MyList.Description"),
            "History" => ("Shell.Nav.History", "Mobile.Page.History.Description"),
            "Downloads" => ("Shell.Nav.Downloads", "Mobile.Page.Downloads.Description"),
            "Settings" => ("Settings.Title", "Mobile.Page.Settings.Description"),
            "More" => ("Mobile.Nav.More", "Mobile.Page.More.Description"),
            _ => ("Shell.Nav.Home", "Mobile.Page.Home.Description")
        };

        PageTitle = GetString(titleKey);
        PageDescription = GetString(descriptionKey);
    }

    private string GetString(string key)
    {
        if (_localizationService is not null)
        {
            var value = _localizationService.GetString(key);
            if (!string.Equals(value, key, StringComparison.Ordinal))
                return value;
        }

        // Fallback: LocalizationSource.Instance has its own internal fallback LocalizationService
        return LocalizationSource.Instance[key];
    }
}
