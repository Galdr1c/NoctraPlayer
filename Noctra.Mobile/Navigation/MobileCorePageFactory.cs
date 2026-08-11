using System;
using Avalonia.Controls;
using Noctra.Mobile.Views;

namespace Noctra.Mobile.Navigation;

internal static class MobileCorePageFactory
{
    public static bool IsCoreDestination(string destination)
        => destination is
            "Home" or
            "Live" or
            "Movies" or
            "Series" or
            "Search" or
            "Favorites" or
            "MyList" or
            "History" or
            "Downloads" or
            "Settings";

    public static Control Create(string destination)
        => destination switch
        {
            "Home" => new MobileHomeView(),
            "Live" => new MobileLiveView(),
            "Movies" => new MobileMoviesView(),
            "Series" => new MobileSeriesView(),
            "Search" => new MobileSearchView(),
            "Favorites" => new MobileFavoritesView(),
            "MyList" => new MobileMyListView(),
            "History" => new MobileHistoryView(),
            "Downloads" => new MobileDownloadsView(),
            "Settings" => new MobileSettingsView(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination,
                "Unknown mobile core destination.")
        };
}
