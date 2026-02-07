using System.Windows;

namespace IPTVPlayer.Helpers;

public static class ThemeHelper
{
    private const string DarkThemeSource = "Resources/Themes/DarkTheme.xaml";
    private const string LightThemeSource = "Resources/Themes/LightTheme.xaml";

    public static void SetTheme(bool isDark)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(isDark ? DarkThemeSource : LightThemeSource, UriKind.Relative)
        };

        // Colors.xaml usually merges theme at index 0
        var colorsDictionary = Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Colors.xaml"));

        if (colorsDictionary != null)
        {
            colorsDictionary.MergedDictionaries.Clear();
            colorsDictionary.MergedDictionaries.Add(dictionary);
        }
    }
}
