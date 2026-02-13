using System.Windows;

namespace Noctra.Helpers;

public static class ThemeHelper
{
    private const string DarkThemeSource = "Resources/Themes/DarkTheme.xaml";
    private const string LightThemeSource = "Resources/Themes/LightTheme.xaml";

    public static void SetTheme(bool isDark)
    {
        var newTheme = new ResourceDictionary
        {
            Source = new Uri(isDark ? DarkThemeSource : LightThemeSource, UriKind.Relative)
        };

        var appResources = Application.Current.Resources.MergedDictionaries;
        
        // Find and remove existing theme dictionary (it's the first one that contains Theme in the path)
        ResourceDictionary? existingTheme = null;
        foreach (var dict in appResources)
        {
            if (dict.Source != null && 
                (dict.Source.OriginalString.Contains("DarkTheme.xaml") || 
                 dict.Source.OriginalString.Contains("LightTheme.xaml")))
            {
                existingTheme = dict;
                break;
            }
        }

        if (existingTheme != null)
        {
            int index = appResources.IndexOf(existingTheme);
            appResources.RemoveAt(index);
            appResources.Insert(index, newTheme);
        }
        else
        {
            // If not found, insert at the beginning
            appResources.Insert(0, newTheme);
        }
    }
}

