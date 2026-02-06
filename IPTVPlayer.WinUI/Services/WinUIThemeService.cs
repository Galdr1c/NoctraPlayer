using IPTVPlayer.Services.Interfaces;
using Microsoft.UI.Xaml;

namespace IPTVPlayer.WinUI.Services;

public class WinUIThemeService : IThemeService
{
    public bool IsDarkTheme => App.Current.RequestedTheme == ApplicationTheme.Dark;

    public void SetTheme(bool isDark)
    {
        if (App.Current.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        }
    }
}
