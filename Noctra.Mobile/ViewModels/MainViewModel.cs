using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Noctra.Mobile.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedDestination = "Home";

    [ObservableProperty]
    private string _pageTitle = "Home";

    [ObservableProperty]
    private string _pageDescription = "Your channels, movies and series in one place.";

    public bool IsMoreSelected => string.Equals(
        SelectedDestination,
        "More",
        StringComparison.Ordinal);

    public void SelectDestination(string destination)
    {
        SelectedDestination = destination;
        OnPropertyChanged(nameof(IsMoreSelected));
        (PageTitle, PageDescription) = destination switch
        {
            "Live" => ("Live TV", "Browse live channels and current programs."),
            "Movies" => ("Movies", "Continue watching or explore your movie library."),
            "Series" => ("Series", "Pick up your episodes and discover new series."),
            "Search" => ("Search", "Find live channels, movies and series."),
            "Favorites" => ("Favorites", "Your saved live channels, movies and series."),
            "MyList" => ("My List", "Everything you saved for later."),
            "History" => ("History", "Continue from recently watched content."),
            "More" => ("More", "Profiles, favorites, settings and premium features."),
            _ => ("Home", "Your channels, movies and series in one place.")
        };
    }
}
