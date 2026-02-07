using IPTVPlayer.Services.Interfaces;
using Microsoft.UI.Xaml;

namespace IPTVPlayer.WinUI.Services;

public class WinUIThemeService : IThemeService
{
    public bool IsDarkTheme => global::IPTVPlayer.WinUI.App.Instance.RequestedTheme == ApplicationTheme.Dark;

    public void SetTheme(bool isDark)
    {
        if (global::IPTVPlayer.WinUI.App.Instance.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        }
    }
}
